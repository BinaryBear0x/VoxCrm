using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Security;
using VoxCrm.Infrastructure.WhatsApp;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

public sealed class WhatsAppOptOutRulesTests
{
    [Theory]
    [InlineData("DUR")]
    [InlineData("dur")]
    [InlineData("Dur!")]
    [InlineData("  STOP ")]
    [InlineData("Abonelikten çık")]
    [InlineData("LİSTEDEN ÇIK")]
    [InlineData("mesaj istemiyorum")]
    public void Stop_requests_are_recognized(string text) => Assert.True(WhatsAppOptOut.IsOptOutMessage(text));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Merhaba")]
    [InlineData("Durum nasıl?")]
    [InlineData("Randevuyu iptal etmek istiyorum")]
    [InlineData("Aşı ne zaman durdurulmalı?")]
    public void Ordinary_messages_never_switch_consent_off(string? text) => Assert.False(WhatsAppOptOut.IsOptOutMessage(text));

    [Theory]
    [InlineData("905551112233@s.whatsapp.net", "905551112233")]
    [InlineData("905551112233:12@s.whatsapp.net", "905551112233")]
    [InlineData("+90 555 111 22 33", "905551112233")]
    [InlineData("123456789012345@lid", null)]
    [InlineData("", null)]
    [InlineData("12345", null)]
    public void Sender_address_becomes_digits_only_when_it_is_a_phone_number(string input, string? expected) =>
        Assert.Equal(expected, WhatsAppOptOut.PhoneDigits(input));

    [Fact]
    public void Every_common_spelling_of_a_turkish_mobile_number_is_looked_up()
    {
        var candidates = WhatsAppOptOut.PhoneLookupCandidates("905551112233");

        Assert.Contains("905551112233", candidates);
        Assert.Contains("05551112233", candidates);
        Assert.Contains("5551112233", candidates);
        Assert.Contains("00905551112233", candidates);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class WhatsAppOptOutTests(PostgresDatabaseFixture database)
{
    private const string CustomerAddress = "905551112233@s.whatsapp.net";

    private static HttpClient Client(ApiApplicationFactory factory, string scope)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create(scope));
        return client;
    }

    private static async Task LinkLookupHashAsync(ApiApplicationFactory factory, VoxCrm.Infrastructure.Data.VoxCrmDbContext db, Clinic clinic, PetOwner owner)
    {
        using var scope = factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IPiiProtector>();
        var hash = protector.BlindIndex(clinic.ID, "905551112233");
        await db.PetOwners.IgnoreQueryFilters().Where(o => o.ID == owner.ID)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.NormalizedPhone, hash));
    }

    private static Task<HttpResponseMessage> Inbound(ApiApplicationFactory factory, Guid clinicId, string from, string text, string id) =>
        Client(factory, "whatsapp.inbound.write").PostAsJsonAsync("/api/whatsapp/inbound", new
        {
            clinicId,
            fromPhone = from,
            message = text,
            receivedAt = DateTime.UtcNow.ToString("O"),
            gatewaySessionId = "gateway-test",
            providerMessageId = id,
        });

    private static WhatsAppNotification Notification(Clinic clinic, PetOwner owner, string status) => new()
    {
        ClinicID = clinic.ID,
        PetOwnerId = owner.ID,
        PhoneNumber = owner.Phone!,
        RecipientSnapshot = WhatsAppRecipientGuard.Snapshot(owner, null),
        MessageContent = "Hatırlatma",
        NotificationType = WhatsAppNotificationTypes.ManualMessage,
        Status = status,
    };

    [Fact]
    public async Task Stop_message_withdraws_consent_and_cancels_queued_messages_only_for_that_clinic()
    {
        Clinic clinic, otherClinic;
        PetOwner owner, otherOwner;
        WhatsAppNotification pending, retry, inFlight, otherPending;
        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Optout One");
            (otherClinic, otherOwner) = await TestData.CreateClinicWithOwnerAsync(db, "Optout Two");
            await LinkLookupHashAsync(factory, db, clinic, owner);
            await LinkLookupHashAsync(factory, db, otherClinic, otherOwner);
            pending = Notification(clinic, owner, WhatsAppNotificationStatuses.Pending);
            retry = Notification(clinic, owner, WhatsAppNotificationStatuses.RetryScheduled);
            inFlight = Notification(clinic, owner, WhatsAppNotificationStatuses.Processing);
            otherPending = Notification(otherClinic, otherOwner, WhatsAppNotificationStatuses.Pending);
            db.AddRange(pending, retry, inFlight, otherPending);
            await db.SaveChangesAsync();
        }

        using var response = await Inbound(factory, clinic.ID, CustomerAddress, "DUR", "stop-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verify = database.CreateDbContext();
        var owners = await verify.PetOwners.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(o => o.ID, o => o.WhatsAppConsent);
        Assert.False(owners[owner.ID]);
        Assert.True(owners[otherOwner.ID]);

        var statuses = await verify.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(n => n.ID, n => n.Status);
        Assert.Equal(WhatsAppNotificationStatuses.Cancelled, statuses[pending.ID]);
        Assert.Equal(WhatsAppNotificationStatuses.Cancelled, statuses[retry.ID]);
        // Already handed to the gateway: the final delivery check blocks it because consent is gone.
        Assert.Equal(WhatsAppNotificationStatuses.Processing, statuses[inFlight.ID]);
        Assert.Equal(WhatsAppNotificationStatuses.Pending, statuses[otherPending.ID]);

        var audit = await verify.SystemAuditLogs.AsNoTracking().SingleAsync(l => l.Action == "WhatsAppInbound.OptOut");
        Assert.Equal(clinic.ID, audit.ClinicId);
        Assert.DoesNotContain("905551112233", audit.MetadataJson ?? string.Empty);
    }

    [Fact]
    public async Task Ordinary_message_leaves_consent_and_queue_untouched()
    {
        Clinic clinic;
        PetOwner owner;
        WhatsAppNotification pending;
        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Optout Normal");
            await LinkLookupHashAsync(factory, db, clinic, owner);
            pending = Notification(clinic, owner, WhatsAppNotificationStatuses.Pending);
            db.Add(pending);
            await db.SaveChangesAsync();
        }

        using var response = await Inbound(factory, clinic.ID, CustomerAddress, "Merhaba, randevuyu iptal etmek istiyorum", "normal-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verify = database.CreateDbContext();
        Assert.True((await verify.PetOwners.IgnoreQueryFilters().AsNoTracking().SingleAsync()).WhatsAppConsent);
        Assert.Equal(WhatsAppNotificationStatuses.Pending, (await verify.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync()).Status);
        Assert.False(await verify.SystemAuditLogs.AnyAsync(l => l.Action.StartsWith("WhatsAppInbound.OptOut")));
    }

    [Fact]
    public async Task Stop_message_from_an_unknown_sender_is_flagged_for_staff_instead_of_being_lost()
    {
        Clinic clinic;
        PetOwner owner;
        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Optout Unknown");
            await LinkLookupHashAsync(factory, db, clinic, owner);
        }

        using var response = await Inbound(factory, clinic.ID, "123456789012345@lid", "STOP", "lid-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verify = database.CreateDbContext();
        Assert.True((await verify.PetOwners.IgnoreQueryFilters().AsNoTracking().SingleAsync()).WhatsAppConsent);
        var audit = await verify.SystemAuditLogs.AsNoTracking().SingleAsync(l => l.Action == "WhatsAppInbound.OptOutUnmatched");
        Assert.Equal("Warning", audit.Level);
    }

    [Fact]
    public async Task Inbound_audit_trail_does_not_contain_the_customers_message_or_full_number()
    {
        Clinic clinic;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, _) = await TestData.CreateClinicWithOwnerAsync(db, "Optout Audit");
        }

        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        using var response = await Inbound(factory, clinic.ID, CustomerAddress, "Köpeğimin tahlil sonucu gizli-metin", "audit-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verify = database.CreateDbContext();
        var audit = await verify.SystemAuditLogs.AsNoTracking().SingleAsync(l => l.Action == "WhatsAppInbound.Write");
        Assert.DoesNotContain("gizli-metin", audit.MetadataJson ?? string.Empty);
        Assert.DoesNotContain("905551112233", audit.MetadataJson ?? string.Empty);
    }

    [Fact]
    public async Task Inbound_without_a_provider_message_id_is_rejected_instead_of_swallowing_later_messages()
    {
        Clinic clinic;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, _) = await TestData.CreateClinicWithOwnerAsync(db, "Optout Validation");
        }

        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        using var empty = await Inbound(factory, clinic.ID, CustomerAddress, "ilk", "");
        using var oversized = await Inbound(factory, clinic.ID, CustomerAddress, "ikinci", new string('x', 300));
        using var first = await Inbound(factory, clinic.ID, CustomerAddress, "üçüncü", "valid-1");
        using var second = await Inbound(factory, clinic.ID, CustomerAddress, "dördüncü", "valid-2");

        // Only blanks is still a stored message (the gateway must never get a 400 for it).
        using var blanks = await Inbound(factory, clinic.ID, CustomerAddress, "  ", "blank-1");
        Assert.Equal(HttpStatusCode.OK, blanks.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await using var verify = database.CreateDbContext();
        Assert.Equal(3, await verify.WhatsAppInboundMessages.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Redelivering_the_same_inbound_message_returns_the_stored_row()
    {
        Clinic clinic;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, _) = await TestData.CreateClinicWithOwnerAsync(db, "Optout Dedupe");
        }

        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Inbound(factory, clinic.ID, CustomerAddress, "Merhaba", "same-id")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.WhatsAppInboundMessages.IgnoreQueryFilters().CountAsync());
    }
}
