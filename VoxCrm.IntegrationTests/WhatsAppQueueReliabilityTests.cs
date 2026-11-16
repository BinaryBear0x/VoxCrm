using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.WhatsApp;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class WhatsAppQueueReliabilityTests(PostgresDatabaseFixture database)
{
    private async Task<(Clinic Clinic, PetOwner Owner)> SeedClinic()
    {
        await using var db = database.CreateDbContext();
        await TestData.ClearWhatsAppDataAsync(db);
        return await TestData.CreateClinicWithOwnerAsync(db, "Queue " + Guid.NewGuid().ToString("N"));
    }

    private static WhatsAppNotification Manual(Clinic clinic, PetOwner owner, string status, string text = "Merhaba") => new()
    {
        ClinicID = clinic.ID,
        PetOwnerId = owner.ID,
        PhoneNumber = owner.Phone!,
        RecipientSnapshot = WhatsAppRecipientGuard.Snapshot(owner, null),
        MessageContent = text,
        NotificationType = WhatsAppNotificationTypes.ManualMessage,
        Status = status,
        NextAttemptAt = DateTime.UtcNow.AddMinutes(-1),
    };

    private static HttpClient Client(ApiApplicationFactory factory, string scope)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create(scope));
        return client;
    }

    [Fact]
    public async Task Repeated_lock_expiry_ends_in_needs_review_instead_of_looping_forever()
    {
        var (clinic, owner) = await SeedClinic();
        WhatsAppNotification notification;
        await using (var db = database.CreateDbContext())
        {
            notification = Manual(clinic, owner, WhatsAppNotificationStatuses.Processing);
            notification.LockedBy = "gateway";
            notification.LockedAt = DateTime.UtcNow.AddMinutes(-20);
            notification.LockExpiresAt = DateTime.UtcNow.AddMinutes(-10);
            db.Add(notification);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        var statuses = new List<string>();
        for (var round = 0; round < 4; round++)
        {
            using var response = await Client(factory, "whatsapp.notifications.recover")
                .PostAsync("/api/whatsapp/notifications/recover-expired-processing", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            await using var db = database.CreateDbContext();
            var current = await db.WhatsAppNotifications.SingleAsync();
            statuses.Add(current.Status);
            if (current.Status == WhatsAppNotificationStatuses.RetryScheduled)
            {
                // Simulate the next claim whose lock expires again.
                current.Status = WhatsAppNotificationStatuses.Processing;
                current.LockExpiresAt = DateTime.UtcNow.AddMinutes(-1);
                await db.SaveChangesAsync();
            }
        }

        Assert.Equal(WhatsAppNotificationStatuses.RetryScheduled, statuses[0]);
        Assert.Equal(WhatsAppNotificationStatuses.NeedsReview, statuses[^1]);
        await using var verify = database.CreateDbContext();
        var final = await verify.WhatsAppNotifications.SingleAsync();
        Assert.Null(final.NextAttemptAt);
        Assert.True(final.RetryCount > 0);
    }

    [Fact]
    public async Task Validate_keeps_the_lock_alive_while_the_message_is_being_sent()
    {
        var (clinic, owner) = await SeedClinic();
        WhatsAppNotification notification;
        await using (var db = database.CreateDbContext())
        {
            notification = Manual(clinic, owner, WhatsAppNotificationStatuses.Processing);
            notification.LockedBy = "gateway";
            notification.LockedAt = DateTime.UtcNow;
            notification.LockExpiresAt = DateTime.UtcNow.AddSeconds(20);
            db.Add(notification);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        using var response = await Client(factory, "whatsapp.notifications.status").PostAsJsonAsync(
            $"/api/whatsapp/notifications/{notification.ID}/validate",
            new { clinicId = clinic.ID, phone = notification.PhoneNumber, message = notification.MessageContent });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("allowed").GetBoolean());
        await using var verify = database.CreateDbContext();
        var stored = await verify.WhatsAppNotifications.SingleAsync();
        Assert.True(stored.LockExpiresAt > DateTime.UtcNow.AddMinutes(4));
        Assert.Equal(WhatsAppNotificationStatuses.Processing, stored.Status);
    }

    [Fact]
    public async Task Validate_with_expired_lock_is_retryable_and_leaves_the_state_to_voxcrm()
    {
        var (clinic, owner) = await SeedClinic();
        WhatsAppNotification notification;
        await using (var db = database.CreateDbContext())
        {
            notification = Manual(clinic, owner, WhatsAppNotificationStatuses.Processing);
            notification.LockedBy = "gateway";
            notification.LockExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            db.Add(notification);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        using var response = await Client(factory, "whatsapp.notifications.status").PostAsJsonAsync(
            $"/api/whatsapp/notifications/{notification.ID}/validate",
            new { clinicId = clinic.ID, phone = notification.PhoneNumber, message = notification.MessageContent });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("allowed").GetBoolean());
        Assert.True(body.GetProperty("retryable").GetBoolean());
        await using var verify = database.CreateDbContext();
        Assert.Equal(WhatsAppNotificationStatuses.Processing, (await verify.WhatsAppNotifications.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("2026-01-01T10:00:00+03:00", 1, 2)]   // past, with offset: clamped forward, must not 500
    [InlineData("2099-01-01T10:00:00Z", 23, 25)]      // far future: clamped to 24 hours
    public async Task Retry_report_accepts_offset_timestamps_and_clamps_the_next_attempt(string requested, int minHours, int maxHours)
    {
        var (clinic, owner) = await SeedClinic();
        WhatsAppNotification notification;
        await using (var db = database.CreateDbContext())
        {
            notification = Manual(clinic, owner, WhatsAppNotificationStatuses.Processing);
            notification.LockExpiresAt = DateTime.UtcNow.AddMinutes(3);
            db.Add(notification);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        using var response = await Client(factory, "whatsapp.notifications.status").PostAsJsonAsync(
            $"/api/whatsapp/notifications/{notification.ID}/status",
            new { status = "RetryScheduled", retryCount = 1, lastError = "temporary", nextAttemptAt = requested });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verify = database.CreateDbContext();
        var stored = await verify.WhatsAppNotifications.SingleAsync();
        Assert.Equal(WhatsAppNotificationStatuses.RetryScheduled, stored.Status);
        Assert.NotNull(stored.NextAttemptAt);
        var delay = stored.NextAttemptAt!.Value - DateTime.UtcNow;
        if (maxHours == 2) Assert.InRange(delay, TimeSpan.FromSeconds(30), TimeSpan.FromHours(maxHours));
        else Assert.InRange(delay, TimeSpan.FromHours(minHours), TimeSpan.FromHours(maxHours));
    }

    [Fact]
    public async Task Stale_failure_report_cannot_overwrite_a_message_that_already_moved_on_but_a_late_sent_is_recorded()
    {
        var (clinic, owner) = await SeedClinic();
        WhatsAppNotification notification;
        await using (var db = database.CreateDbContext())
        {
            notification = Manual(clinic, owner, WhatsAppNotificationStatuses.RetryScheduled);
            db.Add(notification);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        var client = Client(factory, "whatsapp.notifications.status");

        using var stale = await client.PostAsJsonAsync(
            $"/api/whatsapp/notifications/{notification.ID}/status",
            new { status = "Failed", retryCount = 3, lastError = "old gateway attempt" });
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        await using (var verify = database.CreateDbContext())
            Assert.Equal(WhatsAppNotificationStatuses.RetryScheduled, (await verify.WhatsAppNotifications.SingleAsync()).Status);

        // The gateway really delivered it (its report was just late): the truth wins.
        client = Client(factory, "whatsapp.notifications.status");
        using var sent = await client.PostAsJsonAsync(
            $"/api/whatsapp/notifications/{notification.ID}/status",
            new { status = "Sent", gatewayMessageId = "wa-1" });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        await using var final = database.CreateDbContext();
        var stored = await final.WhatsAppNotifications.SingleAsync();
        Assert.Equal(WhatsAppNotificationStatuses.Sent, stored.Status);
        Assert.NotNull(stored.SentAt);
    }

    [Fact]
    public async Task One_undecryptable_message_is_quarantined_and_does_not_block_the_claim()
    {
        var (clinic, owner) = await SeedClinic();
        Guid badId;
        Guid goodId;
        await using (var db = database.CreateDbContext())
        {
            var bad = Manual(clinic, owner, WhatsAppNotificationStatuses.Pending, "bad");
            var good = Manual(clinic, owner, WhatsAppNotificationStatuses.Pending, "good");
            bad.CreatedAt = DateTime.UtcNow.AddMinutes(-10);
            good.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
            db.AddRange(bad, good);
            await db.SaveChangesAsync();
            badId = bad.ID;
            goodId = good.ID;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"WhatsAppNotifications\" SET \"PhoneNumber\" = 'enc:v1:not-valid-ciphertext' WHERE \"ID\" = {badId}");
        }

        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        using var response = await Client(factory, "whatsapp.notifications.claim").PostAsJsonAsync(
            "/api/whatsapp/notifications/claim",
            new { clinicIds = new[] { clinic.ID }, batchSize = 10, gatewayId = "gateway", lockSeconds = 180 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var claimed = (await response.Content.ReadFromJsonAsync<JsonElement[]>())!
            .Select(item => item.GetProperty("notificationId").GetGuid()).ToList();
        Assert.Equal(new[] { goodId }, claimed);

        await using var verify = database.CreateDbContext();
        var quarantined = await verify.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.ID == badId).Select(n => new { n.Status, n.LockedBy }).SingleAsync();
        Assert.Equal(WhatsAppNotificationStatuses.NeedsReview, quarantined.Status);
        Assert.Null(quarantined.LockedBy);
    }

    [Fact]
    public async Task Widening_the_send_window_releases_messages_that_were_queued_for_the_next_opening()
    {
        var (clinic, owner) = await SeedClinic();
        WhatsAppNotification deferred;
        WhatsAppNotification backingOff;
        await using (var db = database.CreateDbContext())
        {
            // Queued while the window was closed: a future time that exists only because of the window.
            deferred = Manual(clinic, owner, WhatsAppNotificationStatuses.Pending, "deferred");
            deferred.NextAttemptAt = DateTime.UtcNow.AddHours(12);
            // A real retry back-off must keep waiting even though the window is open.
            backingOff = Manual(clinic, owner, WhatsAppNotificationStatuses.RetryScheduled, "backing off");
            backingOff.NextAttemptAt = DateTime.UtcNow.AddMinutes(30);
            backingOff.RetryCount = 1;
            db.AddRange(deferred, backingOff);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiApplicationFactory(database.ConnectionString);
        using var response = await Client(factory, "whatsapp.notifications.claim").PostAsJsonAsync(
            "/api/whatsapp/notifications/claim",
            new { clinicIds = new[] { clinic.ID }, batchSize = 10, gatewayId = "gateway", lockSeconds = 180 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var claimed = (await response.Content.ReadFromJsonAsync<JsonElement[]>())!
            .Select(item => item.GetProperty("notificationId").GetGuid()).ToList();
        Assert.Equal(new[] { deferred.ID }, claimed);
        await using var verify = database.CreateDbContext();
        var waiting = await verify.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.ID == backingOff.ID);
        Assert.Equal(WhatsAppNotificationStatuses.RetryScheduled, waiting.Status);
    }
}
