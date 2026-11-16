using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using VoxCrm.Application.Workday;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Workday;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class WorkdayIntegrationTests(PostgresDatabaseFixture database)
{
    private sealed record Tenant(Guid Id) : ITenantService { public Guid GetClinicId() => Id; public bool IsSystemContext => Id == Guid.Empty; }
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
    private async Task<(Clinic Clinic, PetOwner Owner, Patient Patient, Muayene Exam, ApplicationUser User)> Seed()
    {
        await using var db = database.CreateDbContext(); await TestData.ClearWhatsAppDataAsync(db);
        var (c, o) = await TestData.CreateClinicWithOwnerAsync(db, "Workday " + Guid.NewGuid().ToString("N"));
        var p = new Patient { ClinicID = c.ID, Name = "Boncuk" };
        var e = new Muayene { ClinicID = c.ID, PatientId = p.ID, Patient = p, Subjective = "INTERNAL-PRIVATE-NOTE" };
        var u = new ApplicationUser { ClinicId = c.ID, Email = "workday@example.test", UserName = "workday@example.test", NormalizedEmail = "WORKDAY@EXAMPLE.TEST", NormalizedUserName = "WORKDAY@EXAMPLE.TEST", SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), CanApproveCareCards = true };
        var role = await db.Roles.SingleOrDefaultAsync(x => x.Name == "Clinic");
        if (role == null) { role = new IdentityRole<Guid>("Clinic") { Id = Guid.NewGuid(), NormalizedName = "CLINIC" }; db.Add(role); }
        db.AddRange(p, e, u, new PatientOwner { ClinicID = c.ID, PatientId = p.ID, PetOwnerId = o.ID, IsPrimaryOwner = true });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = u.Id, RoleId = role.Id }); await db.SaveChangesAsync(); return (c, o, p, e, u);
    }
    private static CareInput Card(Muayene e, long revision = 0) => new(e.ID, revision, "Kontrol muayenesi yapıldı.", "Veterinerinizin belirttiği adımları uygulayın.", "Belirtilen durumda kliniği arayın.", Today.AddDays(2));

    [Fact]
    public async Task Tasks_support_idempotent_create_assignment_edit_complete_reopen_and_stale_protection()
    {
        var (c, o, p, e, u) = await Seed(); await using var db = database.CreateDbContext(); var s = new WorkdayService(db, new Tenant(c.ID));
        var input = new TaskInput(null, 0, Guid.NewGuid(), "Sonucu kontrol et", "Not", Today.AddDays(-1), p.ID, u.Id, true);
        await s.SaveTaskAsync(input, u.Id); await s.SaveTaskAsync(input, u.Id);
        var task = await db.ClinicTasks.SingleAsync(); Assert.Equal(u.Id, task.AssignedUserId);
        Assert.Contains((await s.GetAsync(null)).Tasks, x => x.ID == task.ID);
        var revision = task.Revision; await s.SaveTaskAsync(input with { Id = task.ID, Revision = revision, Title = "Güncellenmiş görev" }, u.Id);
        await Assert.ThrowsAsync<WorkdayException>(() => s.SetTaskStatusAsync(task.ID, revision, "Done", u.Id));
        await s.SetTaskStatusAsync(task.ID, task.Revision, "Done", u.Id); Assert.Equal(u.Id, task.CompletedBy);
        Assert.DoesNotContain((await s.GetAsync(null)).Tasks, x => x.ID == task.ID);
        await s.SetTaskStatusAsync(task.ID, task.Revision, "Open", u.Id); Assert.Null(task.CompletedAt);
        await s.SetTaskStatusAsync(task.ID, task.Revision, "Cancelled", u.Id);
        Assert.Equal(5, await db.SystemAuditLogs.CountAsync(x => x.Category == "Workday")); Assert.Empty(await db.WhatsAppNotifications.ToListAsync());
    }

    [Fact]
    public async Task Tenant_and_actor_checks_reject_foreign_records_and_assignments()
    {
        var (c, o, p, e, u) = await Seed(); await using var db = database.CreateDbContext();
        var (other, _) = await TestData.CreateClinicWithOwnerAsync(db, "Foreign " + Guid.NewGuid().ToString("N"));
        var s = new WorkdayService(db, new Tenant(other.ID));
        await Assert.ThrowsAsync<WorkdayException>(() => s.GetCareEditorAsync(e.ID, u.Id));
        await Assert.ThrowsAsync<WorkdayException>(() => s.SaveCareAsync(Card(e), u.Id));
        var own = new WorkdayService(db, new Tenant(c.ID));
        await Assert.ThrowsAsync<WorkdayException>(() => own.SaveTaskAsync(new(null,0,Guid.NewGuid(),"Görev",null,Today,Guid.NewGuid(),u.Id,false), u.Id));
        await Assert.ThrowsAsync<WorkdayException>(() => own.ReviewBillingAsync(e.ID, Guid.NewGuid(), "Kontrol", u.Id));
        Assert.Empty((await s.GetAsync(null)).Tasks); Assert.Empty(await db.CareCards.ToListAsync());
    }

    [Fact]
    public async Task Care_card_requires_veterinarian_approval_and_never_exposes_internal_notes_or_sends_messages()
    {
        var (c, o, p, e, u) = await Seed(); await using var db = database.CreateDbContext(); var s = new WorkdayService(db, new Tenant(c.ID));
        await s.SaveCareAsync(Card(e), u.Id); var card = await db.CareCards.SingleAsync();
        Assert.Equal("Draft", card.State); Assert.Null(card.TokenHash);
        var user = await db.Users.SingleAsync(); user.CanApproveCareCards = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<WorkdayException>(() => s.PublishCareAsync(e.ID, card.Revision, u.Id, true));
        user.CanApproveCareCards = true; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<WorkdayException>(() => s.PublishCareAsync(e.ID, card.Revision, u.Id, false));
        var token = await s.PublishCareAsync(e.ID, card.Revision, u.Id, true);
        Assert.Equal(64, token.Length); Assert.NotEqual(token, card.TokenHash);
        var publicCard = await s.ReadPublicAsync(token); Assert.NotNull(publicCard);
        Assert.DoesNotContain("INTERNAL-PRIVATE-NOTE", System.Text.Json.JsonSerializer.Serialize(publicCard));
        Assert.DoesNotContain(token, string.Join(" ", (await db.SystemAuditLogs.ToListAsync()).Select(x => x.Message)));
        Assert.Single(await db.ClinicTasks.ToListAsync()); Assert.Empty(await db.WhatsAppNotifications.ToListAsync()); Assert.Empty(await db.FinanceAccounts.ToListAsync());
        await s.SaveCareAsync(Card(e, card.Revision) with { HomeSteps = "Düzeltilmiş onay bekleyen bakım." }, u.Id);
        Assert.Null(await s.ReadPublicAsync(token)); Assert.Equal("Draft", card.State);
    }

    [Fact]
    public async Task Owner_response_creates_only_one_actionable_contact_task_and_applied_is_quiet()
    {
        var (c, o, p, e, u) = await Seed(); await using var db = database.CreateDbContext(); var s = new WorkdayService(db, new Tenant(c.ID));
        await s.SaveCareAsync(Card(e) with { FollowUpDate = null }, u.Id); var card = await db.CareCards.SingleAsync(); var token = await s.PublishCareAsync(e.ID, card.Revision, u.Id, true);
        var anonymous = new WorkdayService(db, new Tenant(Guid.Empty));
        Assert.True(await anonymous.RespondAsync(token, "Applied")); Assert.Empty(await db.ClinicTasks.ToListAsync());
        Assert.True(await anonymous.RespondAsync(token, "ContactRequested")); Assert.True(await anonymous.RespondAsync(token, "ContactRequested"));
        var task = await db.ClinicTasks.SingleAsync(); Assert.Equal("CareContact", task.Kind); Assert.True(task.Important); Assert.Equal(p.ID, task.PatientId);
        Assert.Contains((await s.GetAsync(null)).Tasks, x => x.ID == task.ID);
        Assert.False(await anonymous.RespondAsync(token, "anything")); Assert.Empty(await db.WhatsAppNotifications.ToListAsync());
    }

    [Fact]
    public async Task Expired_revoked_regenerated_and_changed_owner_links_are_unavailable()
    {
        var (c, o, p, e, u) = await Seed(); await using var db = database.CreateDbContext(); var s = new WorkdayService(db, new Tenant(c.ID));
        await s.SaveCareAsync(Card(e), u.Id); var card = await db.CareCards.SingleAsync();
        var first = await s.PublishCareAsync(e.ID, card.Revision, u.Id, true); var second = await s.PublishCareAsync(e.ID, card.Revision, u.Id, true);
        Assert.Null(await s.ReadPublicAsync(first)); Assert.NotNull(await s.ReadPublicAsync(second));
        (await db.PatientOwners.SingleAsync()).IsActive = false; await db.SaveChangesAsync(); Assert.Null(await s.ReadPublicAsync(second));
        (await db.PatientOwners.SingleAsync()).IsActive = true; card.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); Assert.False(await s.RespondAsync(second, "ContactRequested"));
        var third = await s.PublishCareAsync(e.ID, card.Revision, u.Id, true); await s.RevokeCareAsync(e.ID, card.Revision, u.Id); Assert.Null(await s.ReadPublicAsync(third));
    }

    [Fact]
    public async Task Closing_warns_on_real_missing_fields_and_billing_review_does_not_create_debt()
    {
        var (c, o, p, e, u) = await Seed(); await using var db = database.CreateDbContext(); var s = new WorkdayService(db, new Tenant(c.ID));
        var page = await s.GetAsync(Today); Assert.Contains(page.Issues, x => x.Key == "billing:" + e.ID); Assert.Contains(page.Issues, x => x.Key == "notes:" + e.ID);
        await s.AcknowledgeAsync(Today, "notes:" + e.ID, "Hekim kontrol edecek", u.Id);
        Assert.Contains((await s.GetAsync(Today)).Issues, x => x.Key == "notes:" + e.ID && x.Acknowledgement == "Hekim kontrol edecek");
        await s.ReviewBillingAsync(e.ID, null, "Ücretsiz kontrol muayenesi", u.Id);
        Assert.DoesNotContain((await s.GetAsync(Today)).Issues, x => x.Key == "billing:" + e.ID);
        var exam = await db.Muayeneler.SingleAsync(); exam.Assessment = "Tamamlandı"; exam.Plan = "Kontrol"; await db.SaveChangesAsync();
        Assert.DoesNotContain((await s.GetAsync(Today)).Issues, x => x.Key == "notes:" + e.ID);
        Assert.Empty(await db.FinanceAccounts.ToListAsync()); Assert.Empty(await db.WhatsAppNotifications.ToListAsync());
    }

    [Fact]
    public async Task Encrypted_storage_roundtrips_care_tasks_and_finance_labels()
    {
        var (c, o, p, e, u) = await Seed();
        var keyFile = Path.Combine(Path.GetTempPath(), "voxcrm-workday-" + Guid.NewGuid().ToString("N") + ".key");
        await File.WriteAllTextAsync(keyFile, Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        try
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["PiiEncryption:KeyFile"] = keyFile }).Build();
            var protector = new VoxCrm.Infrastructure.Security.AesGcmPiiProtector(config);
            var options = new DbContextOptionsBuilder<VoxCrmDbContext>().ConfigureWarnings(w => w.Log(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)).UseNpgsql(database.ConnectionString).AddInterceptors(new VoxCrm.Infrastructure.Security.PiiEncryptionInterceptor(protector)).Options;
            string token;
            await using(var db = new VoxCrmDbContext(options, new Tenant(c.ID)))
            {
                var s = new WorkdayService(db, new Tenant(c.ID));
                db.Add(new FinanceAccount { ClinicID = c.ID, PatientId = p.ID, PetOwnerId = o.ID, CustomerName = "Özel müşteri", Description = "Muayene hesabı", DueDate = Today }); await db.SaveChangesAsync();
                await s.SaveCareAsync(Card(e), u.Id); token = await s.PublishCareAsync(e.ID, (await db.CareCards.SingleAsync()).Revision, u.Id, true);
            }
            await using(var raw = database.CreateDbContext())
            {
                Assert.StartsWith("enc:v1:", (await raw.CareCards.SingleAsync()).HomeSteps);
                Assert.StartsWith("enc:v1:", (await raw.ClinicTasks.SingleAsync()).Title);
            }
            await using(var db = new VoxCrmDbContext(options, new Tenant(c.ID)))
            {
                var s = new WorkdayService(db, new Tenant(c.ID)); var editor = await s.GetCareEditorAsync(e.ID, u.Id);
                Assert.Equal("Özel müşteri — Muayene hesabı", Assert.Single(editor.FinanceAccounts).Label);
                Assert.Equal(Card(e).HomeSteps, (await s.ReadPublicAsync(token))!.HomeSteps);
            }
        }
        finally { File.Delete(keyFile); }
    }

    [Fact]
    public async Task Http_forms_render_and_public_response_requires_antiforgery()
    {
        var (c, o, p, e, u) = await Seed(); string token;
        await using(var db = database.CreateDbContext()) { var s = new WorkdayService(db, new Tenant(c.ID)); await s.SaveCareAsync(Card(e), u.Id); token = await s.PublishCareAsync(e.ID, (await db.CareCards.SingleAsync()).Revision, u.Id, true); }
        await using var factory = new WebApplicationFactoryForTests(database.ConnectionString);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using(var scope = factory.Services.CreateScope()) { var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await manager.FindByIdAsync(u.Id.ToString()))!; Assert.True((await manager.AddPasswordAsync(user, "WorkdayTest123!")).Succeeded); }
        static string Token(string page) => WebUtility.HtmlDecode(Regex.Match(page,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var login = await client.GetStringAsync("/Auth/Login"); using(var result = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string,string> { ["email"]=u.Email!, ["password"]="WorkdayTest123!", ["__RequestVerificationToken"]=Token(login) }))) Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        foreach(var url in new[] { "/Workday", "/Workday/Task", "/Workday/Care/"+e.ID }) { using var page = await client.GetAsync(url); Assert.Equal(HttpStatusCode.OK, page.StatusCode); }
        using var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var publicPage = await anonymous.GetStringAsync("/Care/"+token); Assert.Contains("Boncuk", publicPage); Assert.DoesNotContain("INTERNAL-PRIVATE-NOTE", publicPage); Assert.DoesNotContain(u.Email!, publicPage);
        using(var invalid = await anonymous.PostAsync($"/Care/{token}/Respond",new FormUrlEncodedContent(new Dictionary<string,string> { ["response"]="ContactRequested" }))) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using(var valid = await anonymous.PostAsync($"/Care/{token}/Respond",new FormUrlEncodedContent(new Dictionary<string,string> { ["response"]="ContactRequested", ["__RequestVerificationToken"]=Token(publicPage) }))) Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        await using var verify = database.CreateDbContext(); Assert.Single(await verify.ClinicTasks.Where(x => x.Kind == "CareContact").ToListAsync());
        Assert.DoesNotContain(token, string.Join(" ",(await verify.SystemAuditLogs.ToListAsync()).Select(x => x.Path + x.Message + x.MetadataJson)));
    }
}
