using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Vaccinations;
using VoxCrm.Infrastructure.WhatsApp;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class WhatsAppSafetyIntegrationTests(PostgresDatabaseFixture database)
{
    private async Task<(Clinic Clinic, PetOwner Owner, Patient Patient, VaccinationRecord Record)> Seed()
    {
        await using var db = database.CreateDbContext(); await TestData.ClearWhatsAppDataAsync(db);
        var (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Safe WA " + Guid.NewGuid().ToString("N"));
        owner.FirstName = "Burak";
        var patient = new Patient { ClinicID = clinic.ID, Name = "Boncuk" };
        var vaccine = new VaccineType { ClinicID = clinic.ID, Name = "Kuduz", ValidityDays = 365, ReminderDaysBefore = 3 };
        var record = new VaccinationRecord { ClinicID = clinic.ID, PatientId = patient.ID, Patient = patient, VaccineTypeId = vaccine.ID, VaccineType = vaccine, NextDueDate = DateTime.UtcNow.Date.AddDays(1), AdministeredDate = DateTime.UtcNow.Date };
        db.AddRange(patient, vaccine, record, new PatientOwner { ClinicID = clinic.ID, PatientId = patient.ID, PetOwnerId = owner.ID, IsPrimaryOwner = true });
        await db.SaveChangesAsync(); return (clinic, owner, patient, record);
    }
    private static WhatsAppNotification Notification(Clinic c, PetOwner o, Patient p, VaccinationRecord r) => new()
    { ClinicID = c.ID, PetOwnerId = o.ID, PatientId = p.ID, VaccinationRecordId = r.ID, PhoneNumber = o.Phone!, MessageContent = "Merhaba Burak, Boncuk aşı hatırlatması", RecipientSnapshot = WhatsAppRecipientGuard.Snapshot(o, p, r) };

    [Theory]
    [InlineData("phone")][InlineData("consent")][InlineData("owner")][InlineData("link")][InlineData("patient")][InlineData("name")][InlineData("date")]
    public async Task Changing_recipient_or_relationship_stops_existing_message(string change)
    {
        var (c, o, p, r) = await Seed(); var n = Notification(c, o, p, r);
        await using var db = database.CreateDbContext(); var guard = new WhatsAppRecipientGuard(db);
        Assert.Null(await guard.CheckAsync(n));
        switch (change)
        {
            case "phone": (await db.PetOwners.SingleAsync()).Phone = "+905559999999"; break;
            case "consent": (await db.PetOwners.SingleAsync()).WhatsAppConsent = false; break;
            case "owner": (await db.PetOwners.SingleAsync()).IsActive = false; break;
            case "link": (await db.PatientOwners.SingleAsync()).IsActive = false; break;
            case "patient": (await db.Patients.SingleAsync()).IsActive = false; break;
            case "name": (await db.PetOwners.SingleAsync()).FirstName = "Başka Burak"; break;
            case "date": (await db.VaccinationRecords.SingleAsync()).NextDueDate = r.NextDueDate.AddDays(1); break;
        }
        await db.SaveChangesAsync(); Assert.NotNull(await guard.CheckAsync(n));
    }

    [Fact]
    public async Task Removed_primary_is_not_selected_and_auto_reminders_require_opt_in()
    {
        var (c, o, p, r) = await Seed();
        await using var db = database.CreateDbContext(); var clinic = await db.Clinics.SingleAsync(); clinic.AutomaticVaccinationRemindersEnabled = false; await db.SaveChangesAsync();
        var scheduler = new VaccinationReminderScheduler(db, new ClinicSendWindowCalculator());
        Assert.Equal(0, await scheduler.ReconcileAsync(r.ID)); Assert.Empty(await db.WhatsAppNotifications.ToListAsync());
        clinic.AutomaticVaccinationRemindersEnabled = true;
        (await db.PatientOwners.SingleAsync()).IsActive = false;
        var other = new PetOwner { ClinicID = c.ID, FirstName = "Burak", LastName = "Diğer", Phone = "+905559999999", WhatsAppConsent = true };
        db.Add(other); db.Add(new PatientOwner { ClinicID = c.ID, PatientId = p.ID, PetOwnerId = other.ID, IsPrimaryOwner = true }); await db.SaveChangesAsync();
        Assert.Equal(1, await scheduler.ReconcileAsync(r.ID)); var n = await db.WhatsAppNotifications.SingleAsync(); Assert.Equal(other.ID, n.PetOwnerId); Assert.NotEqual(o.ID, n.PetOwnerId);
        n.Status = WhatsAppNotificationStatuses.Cancelled; await db.SaveChangesAsync(); await scheduler.ReconcileAsync(r.ID);
        Assert.Equal(WhatsAppNotificationStatuses.Cancelled, n.Status);
    }

    [Fact]
    public async Task Gateway_final_check_rejects_changed_phone_without_sending()
    {
        var (c, o, p, r) = await Seed(); var n = Notification(c, o, p, r); n.Status = WhatsAppNotificationStatuses.Processing; n.LockExpiresAt = DateTime.UtcNow.AddMinutes(2);
        await using (var db = database.CreateDbContext()) { db.Add(n); await db.SaveChangesAsync(); }
        await using var factory = new ApiApplicationFactory(database.ConnectionString); using var client = factory.CreateClient();
        async Task<bool> Validate()
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create("whatsapp.notifications.status"));
            using var response = await client.PostAsJsonAsync($"/api/whatsapp/notifications/{n.ID}/validate", new { clinicId = c.ID, phone = n.PhoneNumber, message = n.MessageContent });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("allowed").GetBoolean();
        }
        Assert.True(await Validate());
        await using (var db = database.CreateDbContext()) { (await db.PetOwners.SingleAsync()).Phone = "+905559999999"; await db.SaveChangesAsync(); }
        Assert.False(await Validate()); await using var verify = database.CreateDbContext(); var blocked = await verify.WhatsAppNotifications.SingleAsync();
        Assert.Equal(WhatsAppNotificationStatuses.NeedsReview, blocked.Status); Assert.Null(blocked.SentAt);
    }

    [Fact]
    public async Task Claim_stops_changed_recipient_and_late_status_cannot_revive_the_message()
    {
        var (c, o, p, r) = await Seed(); var n = Notification(c, o, p, r);
        await using(var db = database.CreateDbContext()) { db.Add(n); (await db.PetOwners.SingleAsync()).WhatsAppConsent = false; await db.SaveChangesAsync(); }
        await using var factory = new ApiApplicationFactory(database.ConnectionString); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create("whatsapp.notifications.claim"));
        using var claimed = await client.PostAsJsonAsync("/api/whatsapp/notifications/claim", new { clinicIds = new[] { c.ID }, batchSize = 10 });
        Assert.Equal(HttpStatusCode.OK, claimed.StatusCode); Assert.Empty(await claimed.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create("whatsapp.notifications.status"));
        using var late = await client.PostAsJsonAsync($"/api/whatsapp/notifications/{n.ID}/status", new { status = "RetryScheduled", retryCount = 1 });
        Assert.Equal(HttpStatusCode.NotFound, late.StatusCode);
        await using var verify = database.CreateDbContext(); Assert.Equal(WhatsAppNotificationStatuses.NeedsReview, (await verify.WhatsAppNotifications.SingleAsync()).Status);
    }

    [Fact]
    public async Task Manual_preview_confirmation_is_idempotent_and_queue_can_be_cancelled()
    {
        var (c, o, p, r) = await Seed();
        await using var factory = new WebApplicationFactoryForTests(database.ConnectionString);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = new ApplicationUser { UserName = "safe-wa@example.test", Email = "safe-wa@example.test", ClinicId = c.ID };
            Assert.True((await manager.CreateAsync(u, "SafeWaTest123!")).Succeeded); Assert.True((await manager.AddToRoleAsync(u, "Clinic")).Succeeded);
        }
        static string Field(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"" + name + "\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task<HttpResponseMessage> Post(string url, string html, Dictionary<string,string> form)
        { form["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken"); return await client.PostAsync(url, new FormUrlEncodedContent(form)); }
        var login = await client.GetStringAsync("/Auth/Login"); using(var result = await Post("/Auth/Login", login, new() { ["email"]="safe-wa@example.test", ["password"]="SafeWaTest123!" })) Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        var page = await client.GetStringAsync("/WhatsApp"); Assert.Contains("Burak", page); Assert.DoesNotContain("Gelen Mesajlar", page);
        using var preview = await Post("/WhatsApp/SendManual", page, new() { ["clinicId"]=c.ID.ToString(), ["ownerId"]=o.ID.ToString(), ["patientId"]=p.ID.ToString(), ["message"]="Merhaba Burak, Boncuk için randevu." });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode); var html = await preview.Content.ReadAsStringAsync(); Assert.Contains("Boncuk", html); Assert.Contains(o.Phone!, WebUtility.HtmlDecode(html));
        await using(var db = database.CreateDbContext()) Assert.Empty(await db.WhatsAppNotifications.ToListAsync());
        var signed = Field(html, "confirmationToken"); Assert.NotEmpty(signed);
        for (var i=0;i<2;i++) { using var confirmed = await Post("/WhatsApp/ConfirmManual", html, new() { ["clinicId"]=c.ID.ToString(), ["confirmationToken"]=signed }); Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode); }
        Guid id; await using(var db = database.CreateDbContext()) { var n = await db.WhatsAppNotifications.SingleAsync(); id=n.ID; Assert.Equal(o.ID,n.PetOwnerId); Assert.Null(n.SentAt); }
        page = await client.GetStringAsync("/WhatsApp"); using var cancelled = await Post("/WhatsApp/ManageNotification", page, new() { ["clinicId"]=c.ID.ToString(), ["id"]=id.ToString(), ["operation"]="cancel" });
        await using var verify = database.CreateDbContext(); Assert.Equal(WhatsAppNotificationStatuses.Cancelled,(await verify.WhatsAppNotifications.SingleAsync()).Status);
    }
}
