using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxCrm.Application.Workday;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.FollowUp;
using VoxCrm.IntegrationTests.Infrastructure;
namespace VoxCrm.IntegrationTests;
[Collection(DatabaseCollection.Name)]
public sealed class OwnerFollowUpIntegrationTests(PostgresDatabaseFixture database)
{
    private sealed record Tenant(Guid Id):ITenantService{public Guid GetClinicId()=>Id;public bool IsSystemContext=>false;}
    private async Task<(Guid Clinic,Guid Owner,Guid Patient,Guid Other,Guid Foreign,Guid User)> Seed()
    {
        await using var db=database.CreateDbContext();await TestData.ClearWhatsAppDataAsync(db);
        var(c,o)=await TestData.CreateClinicWithOwnerAsync(db,"FollowUp "+Guid.NewGuid().ToString("N"));
        var(f,fo)=await TestData.CreateClinicWithOwnerAsync(db,"Foreign "+Guid.NewGuid().ToString("N"));
        c.Name="Pati Veteriner Kliniği";c.phone="0212 555 01 23";
        var p=new Patient{ClinicID=c.ID,Name="Boncuk",Species="Kedi"};var other=new Patient{ClinicID=c.ID,Name="PRIVATE-OTHER-PATIENT"};var foreign=new Patient{ClinicID=f.ID,Name="PRIVATE-FOREIGN"};
        var u=new ApplicationUser{Id=Guid.NewGuid(),ClinicId=c.ID,Email="followup@example.test",UserName="followup@example.test",NormalizedEmail="FOLLOWUP@EXAMPLE.TEST",NormalizedUserName="FOLLOWUP@EXAMPLE.TEST",SecurityStamp=Guid.NewGuid().ToString()};
        db.AddRange(p,other,foreign,u,new PatientOwner{ClinicID=c.ID,PatientId=p.ID,PetOwnerId=o.ID},new PatientOwner{ClinicID=c.ID,PatientId=other.ID,PetOwnerId=o.ID},new PatientOwner{ClinicID=f.ID,PatientId=foreign.ID,PetOwnerId=fo.ID});
        var role=await db.Roles.SingleOrDefaultAsync(x=>x.Name=="Clinic");if(role==null){role=new IdentityRole<Guid>("Clinic"){Id=Guid.NewGuid(),NormalizedName="CLINIC"};db.Add(role);}db.UserRoles.Add(new IdentityUserRole<Guid>{UserId=u.Id,RoleId=role.Id});
        var type=new VaccineType{ClinicID=c.ID,Name="İç parazit",ValidityDays=90,ReminderDaysBefore=1};var dose=new VaccineType{ClinicID=c.ID,Name="İç parazit 2. doz",ValidityDays=10,ReminderDaysBefore=1};db.AddRange(type,dose);
        db.AddRange(new VaccinationRecord{ClinicID=c.ID,PatientId=p.ID,VaccineTypeId=type.ID,AdministeredDate=DateTime.UtcNow.Date.AddDays(-90),NextDueDate=DateTime.UtcNow.Date,VaccineCode="PRIVATE-BATCH-CODE"},new VaccinationRecord{ClinicID=c.ID,PatientId=p.ID,VaccineTypeId=type.ID,AdministeredDate=DateTime.UtcNow.Date,NextDueDate=DateTime.UtcNow.Date.AddDays(90)},new VaccinationRecord{ClinicID=c.ID,PatientId=p.ID,VaccineTypeId=dose.ID,AdministeredDate=DateTime.UtcNow.Date,NextDueDate=DateTime.UtcNow.Date.AddDays(10)});
        db.Add(new Muayene{ClinicID=c.ID,PatientId=p.ID,Subjective="PRIVATE-EXAM-NOTE"});await db.SaveChangesAsync();return(c.ID,o.ID,p.ID,other.ID,foreign.ID,u.Id);
    }
    private OwnerFollowUpService Service(VoxCrmDbContext db,Guid clinic)=>new(db,new Tenant(clinic));
    private static FollowUpInput Input(Guid owner,Guid patient)=>new(){OwnerId=owner,PatientIds=new(){patient},ConfirmRecipient=true};
    [Fact]
    public async Task Disabled_followup_module_blocks_existing_public_links_and_requests_without_deleting_them()
    {
        var(c,o,p,_,_,u)=await Seed();await using var db=database.CreateDbContext();var service=Service(db,c);var token=await service.IssueAsync(Input(o,p),u);
        var clinic=await db.Clinics.SingleAsync(x=>x.ID==c);clinic.EnabledModules=0;await db.SaveChangesAsync();Assert.Null(await service.ReadAsync(token));Assert.False(await service.RequestAsync(token,p,"OwnerContact"));Assert.Single(await db.OwnerFollowUpShares.ToListAsync());
        clinic.EnabledModules=3;await db.SaveChangesAsync();Assert.NotNull(await service.ReadAsync(token));
    }
    [Fact]
    public async Task Only_selected_patients_and_explicit_information_are_public()
    {
        var(c,o,p,other,foreign,u)=await Seed();await using var db=database.CreateDbContext();var service=Service(db,c);var input=Input(o,p);
        var token=await service.IssueAsync(input,u);var page=(await service.ReadAsync(token))!;Assert.Single(page.Patients);Assert.Equal(p,page.Patients[0].Id);Assert.Equal(3,page.Patients[0].Vaccines.Count);Assert.Equal(2,page.Patients[0].Vaccines.Count(x=>x.Current));
        Assert.DoesNotContain("PRIVATE",System.Text.Json.JsonSerializer.Serialize(page));Assert.Equal(64,token.Length);Assert.NotEqual(token,(await db.OwnerFollowUpShares.SingleAsync()).TokenHash);
        var duplicate=await Assert.ThrowsAsync<WorkdayException>(()=>service.IssueAsync(input,u));Assert.Contains("zaten",duplicate.Message);Assert.NotNull(await service.ReadAsync(token));
        Assert.Empty(await db.WhatsAppNotifications.ToListAsync());
        await Assert.ThrowsAsync<WorkdayException>(()=>service.IssueAsync(new(){OwnerId=o,PatientIds=new(){foreign},ConfirmRecipient=true,Revision=1},u));
        await Assert.ThrowsAsync<WorkdayException>(()=>Service(db,Guid.NewGuid()).ManageAsync(o));
    }
    [Fact]
    public async Task Rotation_expiry_revocation_and_owner_changes_invalidate_capability()
    {
        var(c,o,p,_,_,u)=await Seed();await using var db=database.CreateDbContext();var service=Service(db,c);var token=await service.IssueAsync(Input(o,p),u);
        var input=Input(o,p);input.Revision=1;var rotated=await service.IssueAsync(input,u);Assert.Null(await service.ReadAsync(token));Assert.NotNull(await service.ReadAsync(rotated));
        var owner=await db.PetOwners.SingleAsync(x=>x.ID==o);owner.Phone="+905550009999";await db.SaveChangesAsync();Assert.Null(await service.ReadAsync(rotated));owner.Phone="+905551112233";await db.SaveChangesAsync();Assert.NotNull(await service.ReadAsync(rotated));
        var link=await db.PatientOwners.SingleAsync(x=>x.PatientId==p);link.IsActive=false;await db.SaveChangesAsync();Assert.Null(await service.ReadAsync(rotated));link.IsActive=true;await db.SaveChangesAsync();
        var share=await db.OwnerFollowUpShares.SingleAsync();share.ExpiresAt=DateTime.UtcNow.AddDays(-1);await db.SaveChangesAsync();Assert.Null(await service.ReadAsync(rotated));
        input=Input(o,p);input.Revision=share.Revision;var fresh=await service.IssueAsync(input,u);await service.RevokeAsync(share.ID,share.Revision,u);Assert.Null(await service.ReadAsync(fresh));
    }
    [Fact]
    public async Task Anonymous_requests_are_tenant_bound_idempotent_and_do_not_book_or_message()
    {
        var(c,o,p,other,_,u)=await Seed();string token;
        await using(var db=database.CreateDbContext())token=await Service(db,c).IssueAsync(Input(o,p),u);
        async Task<bool> Request(){await using var db=database.CreateDbContext();return await Service(db,Guid.Empty).RequestAsync(token,p,"OwnerContact");}
        Assert.All(await Task.WhenAll(Request(),Request(),Request()),x=>Assert.True(x));
        await using var verify=database.CreateDbContext();Assert.Single(await verify.ClinicTasks.ToListAsync());var task=await verify.ClinicTasks.SingleAsync();Assert.Equal(c,task.ClinicID);Assert.Equal(p,task.PatientId);Assert.Equal("OwnerContact",task.Kind);
        Assert.False(await Service(verify,Guid.Empty).RequestAsync(token,other,"OwnerContact"));Assert.False(await Service(verify,Guid.Empty).RequestAsync(token,p,"BadKind"));
        Assert.True(await Service(verify,Guid.Empty).RequestAsync(token,p,"OwnerAppointment"));Assert.Equal(2,await verify.ClinicTasks.CountAsync());Assert.Empty(await verify.Appointments.ToListAsync());Assert.Empty(await verify.WhatsAppNotifications.ToListAsync());
    }
    [Fact]
    public async Task Clinic_user_can_share_notes_without_separate_approval_permission()
    {
        var(c,o,p,_,_,u)=await Seed();await using var db=database.CreateDbContext();var user=await db.Users.SingleAsync(x=>x.Id==u);Assert.False(user.CanApproveCareCards);
        var input=Input(o,p);input.ShowHistory=false;input.ShowVaccinations=false;input.ShowAppointments=false;input.PublicNote="Onaylı evde bakım bilgisi";
        var token=await Service(db,c).IssueAsync(input,u);var page=(await Service(db,Guid.Empty).ReadAsync(token))!;Assert.Equal(input.PublicNote,page.Note);Assert.Empty(page.Patients[0].Vaccines);Assert.False(page.ShowHistory);
        Assert.Equal(input.PublicNote,(await Service(db,Guid.Empty).ReadAsync(token))!.Note);
        var clinic=await db.Clinics.SingleAsync(x=>x.ID==c);clinic.IsActive=false;await db.SaveChangesAsync();Assert.Null(await Service(db,Guid.Empty).ReadAsync(token));
    }
    [Fact]
    public async Task Approved_care_is_patient_bound_and_disappears_when_revoked()
    {
        var(c,o,p,other,_,u)=await Seed();await using var db=database.CreateDbContext();var user=await db.Users.SingleAsync(x=>x.Id==u);user.CanApproveCareCards=true;var owner=await db.PetOwners.SingleAsync(x=>x.ID==o);var patient=await db.Patients.SingleAsync(x=>x.ID==p);var exam=await db.Muayeneler.SingleAsync(x=>x.PatientId==p);
        var snapshot=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{o}|{owner.FirstName}|{owner.LastName}|{owner.Phone}|{p}|{patient.Name}")));
        var card=new CareCard{ClinicID=c,ExaminationId=exam.ID,PetOwnerId=o,State="Published",ApprovedBy=u,ApprovedAt=DateTime.UtcNow,ExpiresAt=DateTime.UtcNow.AddDays(14),RecipientSnapshot=snapshot,TodayActions="Onaylı işlem özeti",HomeSteps="Onaylı bakım adımları",CallInstructions="Veterinerin talimatı"};db.Add(card);await db.SaveChangesAsync();
        var input=Input(o,p);input.PatientIds.Add(other);input.ShowCare=true;var token=await Service(db,c).IssueAsync(input,u);
        var page=(await Service(db,Guid.Empty).ReadAsync(token))!;Assert.Single(page.Patients.Single(x=>x.Id==p).Care);Assert.Empty(page.Patients.Single(x=>x.Id==other).Care);
        card.State="Revoked";await db.SaveChangesAsync();Assert.Empty((await Service(db,Guid.Empty).ReadAsync(token))!.Patients.Single(x=>x.Id==p).Care);
    }
    [Fact]
    public async Task Real_management_and_anonymous_http_forms_enforce_csrf_and_mask_token_in_audit()
    {
        var(c,o,p,_,_,u)=await Seed();
        await using(var prepare=database.CreateDbContext()){prepare.Add(new Appointment{ClinicID=c,PatientId=p,ScheduledAt=DateTime.UtcNow.Date.AddDays(5).AddHours(8),Status="Planlandı",AppointmentType="Kontrol",Reason="PRIVATE-APPOINTMENT-NOTE"});await prepare.SaveChangesAsync();}
        await using var factory=new WebApplicationFactoryForTests(database.ConnectionString);
        using(var scope=factory.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();Assert.True((await users.AddPasswordAsync((await users.FindByIdAsync(u.ToString()))!,"FollowUpTest123!")).Succeeded);}
        using var clinic=factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        static string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\""+Regex.Escape(name)+"\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var login=await clinic.GetStringAsync("/Auth/Login");using(var logged=await clinic.PostAsync("/Auth/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"email","followup@example.test"},{"password","FollowUpTest123!"},{"__RequestVerificationToken",Field(login,"__RequestVerificationToken")}})))Assert.Equal(HttpStatusCode.Redirect,logged.StatusCode);
        var manager=await clinic.GetStringAsync("/OwnerFollowUp?ownerId="+o);Assert.Contains("Hasta Sahibi Takibi",WebUtility.HtmlDecode(manager));
        using var issued=await clinic.PostAsync("/OwnerFollowUp/Issue",new FormUrlEncodedContent(new Dictionary<string,string>{{"OwnerId",o.ToString()},{"PatientIds",p.ToString()},{"IssueId",Field(manager,"IssueId")},{"Revision","0"},{"Days","14"},{"ConfirmRecipient","true"},{"ShowVaccinations","true"},{"ShowHistory","true"},{"PublicNote","Boncuk’un takip bilgilerini burada bulabilirsiniz. Kontrolünüzü birlikte planlamak için bize bir talep bırakmanız yeterli."},{"__RequestVerificationToken",Field(manager,"__RequestVerificationToken")}}));Assert.Equal(HttpStatusCode.OK,issued.StatusCode);
        var html=await issued.Content.ReadAsStringAsync();var token=Regex.Match(html,"/OwnerTrack/([A-F0-9]{64})").Groups[1].Value;Assert.Equal(64,token.Length);
        using var anon=factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        using var read=await anon.GetAsync("/OwnerTrack/"+token);Assert.Equal(HttpStatusCode.OK,read.StatusCode);Assert.Contains("no-store",read.Headers.CacheControl!.ToString());Assert.Equal("no-referrer",read.Headers.GetValues("Referrer-Policy").Single());var publicHtml=await read.Content.ReadAsStringAsync();Assert.DoesNotContain("PRIVATE",publicHtml);
        var reviewDir=Environment.GetEnvironmentVariable("VOXCRM_OWNER_PORTAL_REVIEW");
        if(!string.IsNullOrEmpty(reviewDir)){Directory.CreateDirectory(reviewDir);await File.WriteAllTextAsync(Path.Combine(reviewDir,"index.html"),publicHtml);}

        using(var bad=await anon.PostAsync("/OwnerTrack/"+token+"/Request",new FormUrlEncodedContent(new Dictionary<string,string>{{"patientId",p.ToString()},{"kind","OwnerContact"}})))Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        var fields=new Dictionary<string,string>{{"patientId",p.ToString()},{"kind","OwnerContact"},{"__RequestVerificationToken",Field(publicHtml,"__RequestVerificationToken")}};
        using(var good=await anon.PostAsync("/OwnerTrack/"+token+"/Request",new FormUrlEncodedContent(fields)))Assert.Equal(HttpStatusCode.Redirect,good.StatusCode);
        using(var repeat=await anon.PostAsync("/OwnerTrack/"+token+"/Request",new FormUrlEncodedContent(fields)))Assert.Equal(HttpStatusCode.Redirect,repeat.StatusCode);
        Assert.Contains("Aranma talebiniz",WebUtility.HtmlDecode(await anon.GetStringAsync("/OwnerTrack/"+token)));
        await using var verify=database.CreateDbContext();Assert.Single(await verify.ClinicTasks.ToListAsync());Assert.DoesNotContain(token,string.Join(" ",await verify.SystemAuditLogs.Select(x=>x.Message+" "+x.Path+" "+x.MetadataJson).ToListAsync()));
        using var hidden=await anon.GetAsync("/OwnerFollowUp");Assert.Equal(HttpStatusCode.Redirect,hidden.StatusCode);
    }
}
