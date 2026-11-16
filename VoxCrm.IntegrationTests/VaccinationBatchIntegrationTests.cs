using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VoxCrm.Application.Vaccinations;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Audit;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Vaccinations;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class VaccinationBatchIntegrationTests(PostgresDatabaseFixture database)
{
    private sealed record Tenant(Guid Id) : ITenantService { public Guid GetClinicId()=>Id; public bool IsSystemContext=>false; }
    private static DateTime Today => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(3).Date,DateTimeKind.Utc);
    private async Task<(Guid Clinic,Guid Patient,Guid Actor,Guid[] Types)> Seed()
    {
        await using var db=database.CreateDbContext();await TestData.ClearWhatsAppDataAsync(db);
        var(c,o)=await TestData.CreateClinicWithOwnerAsync(db,"Batch "+Guid.NewGuid().ToString("N"));
        var p=new Patient{ClinicID=c.ID,Name="Boncuk",Species="Kedi"};var u=new ApplicationUser{Id=Guid.NewGuid(),ClinicId=c.ID,Email="batch@example.test",UserName="batch@example.test",NormalizedEmail="BATCH@EXAMPLE.TEST",NormalizedUserName="BATCH@EXAMPLE.TEST",SecurityStamp=Guid.NewGuid().ToString()};
        db.AddRange(p,u,new PatientOwner{ClinicID=c.ID,PatientId=p.ID,PetOwnerId=o.ID});
        var types=new[]{new VaccineType{ClinicID=c.ID,Name="İç parazit",ValidityDays=90,ReminderDaysBefore=1},new VaccineType{ClinicID=c.ID,Name="Dış parazit",ValidityDays=30,ReminderDaysBefore=1},new VaccineType{ClinicID=c.ID,Name="Karma",ValidityDays=365,ReminderDaysBefore=1}};db.AddRange(types);
        var role=await db.Roles.SingleOrDefaultAsync(x=>x.Name=="Clinic");if(role==null){role=new IdentityRole<Guid>("Clinic"){Id=Guid.NewGuid(),NormalizedName="CLINIC"};db.Add(role);}
        db.UserRoles.Add(new IdentityUserRole<Guid>{UserId=u.Id,RoleId=role.Id});await db.SaveChangesAsync();return(c.ID,p.ID,u.Id,types.Select(x=>x.ID).ToArray());
    }
    private VaccinationService Service(VoxCrmDbContext db,Guid clinic,IVaccinationReminderScheduler? scheduler=null)=>new(db,new Tenant(clinic),new DbAuditLogger(db),NullLogger<VaccinationService>.Instance,scheduler??new VaccinationReminderScheduler(db,new ClinicSendWindowCalculator()));
    private static VaccinationBatchInput Input(Guid patient,Guid[] types)=>new(patient,Today,Guid.NewGuid(),types.Select((x,i)=>new VaccinationBatchItem(x,"SERİ-"+i)).ToList());

    [Fact]
    public async Task Three_applications_have_independent_codes_dates_and_audit_without_duplicate_replay()
    {
        var(c,p,u,types)=await Seed();await using var db=database.CreateDbContext();var s=Service(db,c);var input=Input(p,types);
        var result=await s.CreateBatchAsync(input,u);Assert.True(result.Succeeded,result.Error);Assert.Equal(3,result.Records.Count);
        var replay=await s.CreateBatchAsync(input,u);Assert.True(replay.Succeeded);Assert.Equal(3,await db.VaccinationRecords.CountAsync());
        Assert.Equal(new[]{30,90,365},result.Records.Select(x=>(x.NextDueDate-x.AdministeredDate).Days).OrderBy(x=>x).ToArray());
        Assert.Equal(3,result.Records.Select(x=>x.VaccineCode).Distinct().Count());Assert.Equal(3,await db.SystemAuditLogs.CountAsync(x=>x.Action=="Vaccination.BatchCreated"));
        Assert.Empty(await db.WhatsAppNotifications.ToListAsync());
        Assert.False((await s.CreateBatchAsync(input with {Items=new[]{new VaccinationBatchItem(types[0],"FARKLI")}},u)).Succeeded);
        Assert.False((await s.CreateBatchAsync(input with {SubmissionId=Guid.NewGuid()},u)).Succeeded);Assert.Equal(3,await db.VaccinationRecords.CountAsync());
    }
    [Fact]
    public async Task Invalid_or_foreign_selection_never_partially_saves()
    {
        var(c,p,u,types)=await Seed();await using var db=database.CreateDbContext();var s=Service(db,c);var input=Input(p,types);
        foreach(var bad in new[]{input with{Items=Array.Empty<VaccinationBatchItem>()},input with{Items=new[]{new VaccinationBatchItem(types[0],null),new VaccinationBatchItem(Guid.NewGuid(),null)}},input with{Items=new[]{new VaccinationBatchItem(types[0],null),new VaccinationBatchItem(types[0],null)}},input with{AdministeredDate=Today.AddDays(1)},input with{Items=new[]{new VaccinationBatchItem(types[0],new string('x',201))}}}) Assert.False((await s.CreateBatchAsync(bad,u)).Succeeded);
        Assert.Empty(await db.VaccinationRecords.ToListAsync());Assert.Empty(await db.SystemAuditLogs.ToListAsync());
    }
    [Fact]
    public async Task Concurrent_double_submit_creates_only_three_rows()
    {
        var(c,p,u,types)=await Seed();var input=Input(p,types);await using var a=database.CreateDbContext();await using var b=database.CreateDbContext();
        var result=await Task.WhenAll(Service(a,c).CreateBatchAsync(input,u),Service(b,c).CreateBatchAsync(input,u));Assert.All(result,x=>Assert.True(x.Succeeded,x.Error));
        await using var db=database.CreateDbContext();Assert.Equal(3,await db.VaccinationRecords.CountAsync());
    }
    private sealed class FailingScheduler:IVaccinationReminderScheduler
    {
        public Task<int> ReconcileAsync(Guid? id=null,DateTime? now=null,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("Injected scheduler failure");
        public Task<VaccinationReminderActionResult> RetryFailedAsync(Guid id,DateTime? now=null,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<VaccinationReminderDispatchResult> StartImmediateAsync(Guid id,DateTime? now=null,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task CompleteImmediateAsync(Guid id,string? gatewayMessageId,DateTime? now=null,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task FailImmediateAsync(Guid id,string error,CancellationToken ct=default)=>throw new NotSupportedException();
    }
    [Fact]
    public async Task Failure_after_save_rolls_back_all_applications_and_audit()
    {
        var(c,p,u,types)=await Seed();await using var db=database.CreateDbContext();var result=await Service(db,c,new FailingScheduler()).CreateBatchAsync(Input(p,types),u);Assert.False(result.Succeeded);
        await using var verify=database.CreateDbContext();Assert.Empty(await verify.VaccinationRecords.ToListAsync());Assert.Empty(await verify.SystemAuditLogs.ToListAsync());
    }
    [Fact]
    public async Task Renewal_preserves_history_cancels_unsent_old_reminder_and_archive_reopens_expectation()
    {
        var(c,p,u,types)=await Seed();await using var db=database.CreateDbContext();var owner=await db.PetOwners.SingleAsync();
        var old=new VaccinationRecord{ClinicID=c,PatientId=p,VaccineTypeId=types[0],AdministeredDate=Today.AddDays(-90),NextDueDate=Today};db.Add(old);
        var notification=new WhatsAppNotification{ClinicID=c,PetOwnerId=owner.ID,VaccinationRecordId=old.ID,PhoneNumber=owner.Phone!,MessageContent="Eski hatırlatma",Status="Pending"};db.Add(notification);await db.SaveChangesAsync();
        var s=Service(db,c);var result=await s.CreateBatchAsync(Input(p,new[]{types[0]}),u);Assert.True(result.Succeeded,result.Error);
        Assert.Equal("Cancelled",notification.Status);var list=await s.ListAsync();Assert.Equal(Today,Assert.Single(list,x=>x.ID==old.ID).RenewedOn);
        Assert.Equal(2,list.Count);Assert.False((await new VaccinationReminderScheduler(db,new ClinicSendWindowCalculator()).StartImmediateAsync(old.ID)).Succeeded);
        await s.ArchiveAsync(result.Records.Single().ID,u);Assert.Null((await s.ListAsync()).Single().RenewedOn);
    }
    [Fact]
    public async Task Http_form_accepts_multiple_rows_and_preserves_validation_feedback()
    {
        var(c,p,u,types)=await Seed();await using var factory=new WebApplicationFactoryForTests(database.ConnectionString);
        using(var scope=factory.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var user=(await users.FindByIdAsync(u.ToString()))!;Assert.True((await users.AddPasswordAsync(user,"BatchTest123!")).Succeeded);}
        using var client=factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        static string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\""+Regex.Escape(name)+"\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var login=await client.GetStringAsync("/Auth/Login");using(var logged=await client.PostAsync("/Auth/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"email","batch@example.test"},{"password","BatchTest123!"},{"__RequestVerificationToken",Field(login,"__RequestVerificationToken")}})))Assert.Equal(HttpStatusCode.Redirect,logged.StatusCode);
        var page=await client.GetStringAsync("/Vaccination/Create");Assert.Contains("vaccination-batch.js",page);var submission=Field(page,"SubmissionId");
        var fields=new Dictionary<string,string>{{"PatientId",p.ToString()},{"AdministeredDate",Today.ToString("yyyy-MM-dd")},{"SubmissionId",submission},{"__RequestVerificationToken",Field(page,"__RequestVerificationToken")}};
        for(var i=0;i<3;i++){fields[$"Rows[{i}].VaccineTypeId"]=types[i].ToString();fields[$"Rows[{i}].Selected"]="true";fields[$"Rows[{i}].VaccineCode"]="KOD-"+i;}
        using(var saved=await client.PostAsync("/Vaccination/Create",new FormUrlEncodedContent(fields))){Assert.True(saved.StatusCode==HttpStatusCode.Redirect,await saved.Content.ReadAsStringAsync());}
        using(var repeat=await client.PostAsync("/Vaccination/Create",new FormUrlEncodedContent(fields)))Assert.Equal(HttpStatusCode.Redirect,repeat.StatusCode);
        await using var verify=database.CreateDbContext();Assert.Equal(3,await verify.VaccinationRecords.CountAsync());
        // Force local-kind values as returned by the application's legacy timestamp reader.
        var record=await verify.VaccinationRecords.FirstAsync();
        record.AdministeredDate=DateTime.SpecifyKind(new DateTime(2026,10,1),DateTimeKind.Local);
        record.NextDueDate=DateTime.SpecifyKind(new DateTime(2027,10,1),DateTimeKind.Local);
        await verify.SaveChangesAsync();
        await using(var beforeDb=database.CreateDbContext())
        {
            var before=await beforeDb.VaccinationRecords.AsNoTracking().SingleAsync(x=>x.ID==record.ID);
            var list=await client.GetStringAsync("/Vaccination");
            using(var archived=await client.PostAsync("/Vaccination/Delete",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",record.ID.ToString()},{"__RequestVerificationToken",Field(list,"__RequestVerificationToken")}}))) Assert.Equal(HttpStatusCode.Redirect,archived.StatusCode);
            await using(var afterDb=database.CreateDbContext())
            {var after=await afterDb.VaccinationRecords.IgnoreQueryFilters().AsNoTracking().SingleAsync(x=>x.ID==record.ID);Assert.False(after.IsActive);Assert.Equal(before.AdministeredDate,after.AdministeredDate);Assert.Equal(before.NextDueDate,after.NextDueDate);Assert.Equal(before.CreatedAt,after.CreatedAt);}
            var archivePage=await client.GetStringAsync("/Vaccination?includeArchived=true");
            using(var restored=await client.PostAsync("/Vaccination/Restore",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",record.ID.ToString()},{"__RequestVerificationToken",Field(archivePage,"__RequestVerificationToken")}}))) Assert.Equal(HttpStatusCode.Redirect,restored.StatusCode);
            await using var restoredDb=database.CreateDbContext();var active=await restoredDb.VaccinationRecords.AsNoTracking().SingleAsync(x=>x.ID==record.ID);Assert.True(active.IsActive);Assert.Equal(before.AdministeredDate,active.AdministeredDate);Assert.Equal(before.NextDueDate,active.NextDueDate);
        }
        var noSelection=await client.GetStringAsync("/Vaccination/Create");using var invalid=await client.PostAsync("/Vaccination/Create",new FormUrlEncodedContent(new Dictionary<string,string>{{"PatientId",p.ToString()},{"AdministeredDate",Today.ToString("yyyy-MM-dd")},{"SubmissionId",Field(noSelection,"SubmissionId")},{"__RequestVerificationToken",Field(noSelection,"__RequestVerificationToken")}}));
        Assert.Equal(HttpStatusCode.OK,invalid.StatusCode);Assert.Contains("uygulama seçin",WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));
    }
}
