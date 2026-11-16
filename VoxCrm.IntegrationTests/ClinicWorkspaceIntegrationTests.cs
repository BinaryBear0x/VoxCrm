using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using VoxCrm.Domain.Common;
using VoxCrm.Application.Dashboard;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ClinicWorkspaceIntegrationTests(PostgresDatabaseFixture database)
{
    private sealed record Tenant(Guid Id) : ITenantService { public Guid GetClinicId() => Id; public bool IsSystemContext => false; }
    [Fact]
    public async Task Global_patient_search_matches_encrypted_contact_phone_and_full_owner_name()
    {
        Guid clinicId;
        await using(var seed = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(seed); var (c,o) = await TestData.CreateClinicWithOwnerAsync(seed,"Search Clinic"); clinicId=c.ID;
            var p = new Patient { ClinicID=c.ID, Name="Pamuk" }; seed.Add(p); seed.Add(new PatientOwner{ClinicID=c.ID,PatientId=p.ID,PetOwnerId=o.ID}); await seed.SaveChangesAsync();
        }
        var keyFile = Path.Combine(Path.GetTempPath(),"voxcrm-workspace-"+Guid.NewGuid().ToString("N")+".key");
        await File.WriteAllTextAsync(keyFile,Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["PiiEncryption:KeyFile"]=keyFile}).Build();
            var protector = new VoxCrm.Infrastructure.Security.AesGcmPiiProtector(config);
            var options = new DbContextOptionsBuilder<VoxCrmDbContext>().UseNpgsql(database.ConnectionString).ConfigureWarnings(w => w.Log(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)).AddInterceptors(new VoxCrm.Infrastructure.Security.PiiEncryptionInterceptor(protector)).Options;
            await using(var db = new VoxCrmDbContext(options,new Tenant(clinicId)))
            { var o=await db.PetOwners.SingleAsync();o.Phone="+905559876543";o.FirstName="Burak";o.LastName="Demir";(await db.Clinics.SingleAsync()).DashboardNote="Özel klinik notu";await db.SaveChangesAsync(); }
            await using(var raw = database.CreateDbContext()) { Assert.StartsWith("enc:v1:",(await raw.Clinics.SingleAsync()).DashboardNote!);Assert.StartsWith("enc:v1:",(await raw.PetOwners.SingleAsync()).Phone!); }
            await using(var db = new VoxCrmDbContext(options,new Tenant(clinicId)))
            {
                var service = new VoxCrm.Infrastructure.Patients.PatientService(db,new Tenant(clinicId),new VoxCrm.Infrastructure.Audit.DbAuditLogger(db));
                Assert.Single(await service.ListAsync("0555 987 65 43")); Assert.Single(await service.SearchAsync("Burak Demir"));Assert.Single(await service.SearchAsync("Pamuk"));
                Assert.Equal("Özel klinik notu",(await db.Clinics.SingleAsync()).DashboardNote);
            }
        }
        finally { File.Delete(keyFile); }
    }
    [Fact]
    public async Task Home_uses_real_day_data_and_callback_identity_and_saves_note_without_messages()
    {
        await using var factory = new WebApplicationFactoryForTests(database.ConnectionString);
        Guid clinicId; Guid taskId; long revision;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        using(var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VoxCrmDbContext>(); await TestData.ClearWhatsAppDataAsync(db);
            var (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Pati Test Kliniği"); clinicId = clinic.ID;
            owner.FirstName = "Burak"; owner.LastName = "Demir";
            var patient = new Patient { ClinicID = clinic.ID, Name = "Boncuk", Species = "Kedi" };
            db.Add(patient); db.Add(new PatientOwner { ClinicID = clinic.ID, PatientId = patient.ID, PetOwnerId = owner.ID });
            var local = today.ToDateTime(new TimeOnly(11,0)); var scheduled = TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));
            db.Add(new Appointment { ClinicID = clinic.ID, PatientId = patient.ID, ScheduledAt = scheduled, AppointmentType = "Kontrol", Status = "Planlandı" });
            var task = new ClinicTask { ClinicID = clinic.ID, Title = "Tahlil sonucunu kontrol et", PatientId = patient.ID, DueDate = today, SubmissionId = Guid.NewGuid() }; db.Add(task); taskId = task.ID; revision = task.Revision;
            db.Add(new ClinicTask { ClinicID = clinic.ID, Title = "Görüşme talebi", PatientId = patient.ID, DueDate = today, Kind = "CareContact", SubmissionId = Guid.NewGuid() });
            var account = new FinanceAccount { ClinicID = clinic.ID, PetOwnerId = owner.ID, PatientId = patient.ID, CustomerName = "Burak Demir", DueDate = today };
            db.Add(account); db.Add(new FinanceCharge { ClinicID = clinic.ID, FinanceAccountId = account.ID, Amount = 200, ChargeDate = today });
            db.Add(new FinanceReceipt { ClinicID = clinic.ID, FinanceAccountId = account.ID, Amount = 75, PaidDate = today, PaymentMethod = "Nakit" });
            db.Add(new Muayene { ClinicID = clinic.ID, PatientId = patient.ID, Assessment = "Kontrol", Plan = "Takip" }); await db.SaveChangesAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var u = new ApplicationUser { UserName = "workspace@example.test", Email = "workspace@example.test", ClinicId = clinic.ID };
            Assert.True((await users.CreateAsync(u, "WorkspaceTest123!")).Succeeded); Assert.True((await users.AddToRoleAsync(u, "Clinic")).Succeeded);
        }
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        static string Token(string page) => WebUtility.HtmlDecode(Regex.Match(page,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task<HttpResponseMessage> Post(string url, string html, Dictionary<string,string> fields)
        { fields["__RequestVerificationToken"] = Token(html); return await client.PostAsync(url,new FormUrlEncodedContent(fields)); }
        var login = await client.GetStringAsync("/Auth/Login"); using(var result = await Post("/Auth/Login",login,new(){["email"]="workspace@example.test",["password"]="WorkspaceTest123!"})) Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);
        var page = await client.GetStringAsync("/"); var decoded = WebUtility.HtmlDecode(page);
        Assert.Contains("Geri Arama Talepleri",decoded); Assert.Contains("Hızlı İşlemler",decoded); Assert.Contains("Klinik Notu",decoded); Assert.Contains("Burak Demir",decoded); Assert.Contains("Boncuk",decoded); Assert.Contains("11:00",decoded); Assert.Contains("crm.css",page); Assert.Contains("data-appointment-search",page);
        var artifactDir = Environment.GetEnvironmentVariable("VOXCRM_UI_REVIEW_DIR");
        if (!string.IsNullOrEmpty(artifactDir)) { Directory.CreateDirectory(artifactDir); await File.WriteAllTextAsync(Path.Combine(artifactDir,"index.html"), page.Replace("href=\"/", "href=\"http://127.0.0.1:5114/").Replace("src=\"/", "src=\"http://127.0.0.1:5114/")); }
        using(var changed = await Post("/Home/SaveClinicNote",page,new(){["note"]="Bugün aşı stoğunu kontrol et.",["revision"]="0"})) Assert.Equal(HttpStatusCode.Redirect,changed.StatusCode);
        page = await client.GetStringAsync("/"); Assert.Contains("Bugün aşı",WebUtility.HtmlDecode(page));
        using(var stale = await Post("/Home/SaveClinicNote",page,new(){["note"]="Eski ekran",["revision"]="0"})) Assert.Equal(HttpStatusCode.Redirect,stale.StatusCode);
        using(var changed = await Post("/Workday/TaskStatus",page,new(){["id"]=taskId.ToString(),["revision"]=revision.ToString(),["status"]="Done",["returnUrl"]="/"})) Assert.Equal("/",changed.Headers.Location!.ToString());
        await using var verify = database.CreateDbContext(); Assert.Equal("Bugün aşı stoğunu kontrol et.",(await verify.Clinics.SingleAsync(x=>x.ID==clinicId)).DashboardNote); Assert.Equal("Done",(await verify.ClinicTasks.SingleAsync(x=>x.ID==taskId)).Status); Assert.Empty(await verify.WhatsAppNotifications.ToListAsync());
        using var otherDay = await client.GetAsync("/?date="+today.AddDays(-1).ToString("yyyy-MM-dd")); Assert.Equal(HttpStatusCode.OK,otherDay.StatusCode);
        var otherText = WebUtility.HtmlDecode(await otherDay.Content.ReadAsStringAsync()); Assert.DoesNotContain("11:00",otherText);
    }
}
