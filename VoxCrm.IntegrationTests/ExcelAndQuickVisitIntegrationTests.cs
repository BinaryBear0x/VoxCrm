extern alias VoxCrmWeb;
using System.Net;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using VoxCrm.Application.Audit;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Finance;
using VoxCrm.Infrastructure.Vaccinations;
using VoxCrm.IntegrationTests.Infrastructure;
using VoxCrmWeb::VoxCrm.Web.Models;
using VoxCrmWeb::VoxCrm.Web.Services;
namespace VoxCrm.IntegrationTests;
[Collection(DatabaseCollection.Name)]
public sealed class ExcelAndQuickVisitIntegrationTests(PostgresDatabaseFixture database)
{
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Dashboard_timestamp_display_accepts_all_kinds_without_changing_the_instant(DateTimeKind kind)
    {
        var utc = new DateTime(2026, 10, 31, 9, 15, 0, DateTimeKind.Utc);
        var value = kind == DateTimeKind.Local ? utc.ToLocalTime() : DateTime.SpecifyKind(utc, kind);
        var zone = TimeZoneInfo.CreateCustomTimeZone("TestClinic", TimeSpan.FromHours(3), "Test", "Test");
        Assert.Equal(new DateTime(2026, 10, 31, 12, 15, 0), ClinicDateDisplay.Convert(value, zone));
    }
    private sealed record Tenant(Guid Id):ITenantService {public Guid GetClinicId()=>Id;public bool IsSystemContext=>false;}
    private sealed class Audit:IAuditLogger {public Task LogAsync(AuditLogEntry entry,CancellationToken ct=default)=>Task.CompletedTask;}
    private async Task<(Guid Clinic,Guid Owner,Guid Patient,Guid User,Guid Type)> Seed()
    {
        await using var db=database.CreateDbContext();await TestData.ClearWhatsAppDataAsync(db);var(c,o)=await TestData.CreateClinicWithOwnerAsync(db,"Excel Visit "+Guid.NewGuid().ToString("N"));c.AutomaticVaccinationRemindersEnabled=false;c.IsWhatsAppEnabled=false;
        var p=new Patient{ClinicID=c.ID,Name="Boncuk",Species="Kedi"};var t=new VaccineType{ClinicID=c.ID,Name="İç parazit",ValidityDays=60,ReminderDaysBefore=1};
        var user=new ApplicationUser{Id=Guid.NewGuid(),ClinicId=c.ID,UserName="transfer@security.test",Email="transfer@security.test",NormalizedUserName="TRANSFER@SECURITY.TEST",NormalizedEmail="TRANSFER@SECURITY.TEST",SecurityStamp=Guid.NewGuid().ToString(),ConcurrencyStamp=Guid.NewGuid().ToString()};user.PasswordHash=new PasswordHasher<ApplicationUser>().HashPassword(user,"LocalSecurity123!");
        var role=await db.Roles.SingleOrDefaultAsync(x=>x.Name=="Clinic");if(role==null){role=new IdentityRole<Guid>("Clinic"){Id=Guid.NewGuid(),NormalizedName="CLINIC"};db.Add(role);}db.AddRange(p,t,user,new IdentityUserRole<Guid>{UserId=user.Id,RoleId=role.Id},new PatientOwner{ClinicID=c.ID,PatientId=p.ID,PetOwnerId=o.ID});await db.SaveChangesAsync();return(c.ID,o.ID,p.ID,user.Id,t.ID);
    }
    private static MemoryStream File(params string[][] rows)
    {
        using var wb=new XLWorkbook();var s=wb.AddWorksheet("Data");for(var c=0;c<ExcelTransferService.Headers.Length;c++)s.Cell(1,c+1).Value=ExcelTransferService.Headers[c];for(var r=0;r<rows.Length;r++)for(var c=0;c<rows[r].Length;c++)s.Cell(r+2,c+1).Value=rows[r][c];var stream=new MemoryStream();wb.SaveAs(stream);stream.Position=0;return stream;
    }
    private static readonly int[] Map=[0,1,2,3,4,5,6,7,8];
    private QuickVisitService Visit(VoxCrmDbContext db,Guid clinic,IMemoryCache cache)=>new(db,new Tenant(clinic),cache,new VaccinationService(db,new Tenant(clinic),new Audit()),new FinanceService(db,new Tenant(clinic)));
    private static QuickVisitModel Input(Guid patient,Guid owner,Guid type)=>new(){PatientId=patient,OwnerId=owner,Notes="İç parazit uygulandı.",Weight="4,2",Amount="1500",Paid="1000",PaymentMethod="Nakit",VaccineIds=[type],HomeSteps="Hekimin belirttiği adımları uygulayın."};
    [Fact] public async Task Dealer_module_switches_enforce_ownership_revision_and_preserve_records()
    {
        var(c,_,_,u,_)=await Seed();await using var db=database.CreateDbContext();var tenant=new Tenant(c);var modules=new ClinicModules(db,tenant);var clinic=await db.Clinics.SingleAsync(x=>x.ID==c);var dealer=clinic.DealerId;
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>modules.SaveAsync(Guid.NewGuid(),u,c,0,false,false,false,default));
        await modules.SaveAsync(dealer,u,c,0,false,false,false,default);Assert.Equal(0,(await db.Clinics.AsNoTracking().SingleAsync(x=>x.ID==c)).EnabledModules);Assert.Single(await db.Patients.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>modules.SaveAsync(dealer,u,c,0,true,true,false,default));await modules.SaveAsync(dealer,u,c,1,true,true,false,default);Assert.Equal(3,(await db.Clinics.AsNoTracking().SingleAsync(x=>x.ID==c)).EnabledModules);
        var fresh=new Clinic{DealerId=dealer,Name="Base only",Slug=Guid.NewGuid().ToString("N"),EnabledModules=0};db.Clinics.Add(fresh);await db.SaveChangesAsync();Assert.Equal(0,(await db.Clinics.AsNoTracking().SingleAsync(x=>x.ID==fresh.ID)).EnabledModules);
    }
    [Fact] public async Task Closed_modules_disappear_and_reject_direct_http_while_base_remains_available()
    {
        var(c,o,p,u,t)=await Seed();await using var factory=new WebApplicationFactoryForTests(database.ConnectionString);using var client=factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        string Csrf(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var page=await client.GetStringAsync("/Auth/Login");using var login=await client.PostAsync("/Auth/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"email","transfer@security.test"},{"password","LocalSecurity123!"},{"__RequestVerificationToken",Csrf(page)}}));Assert.Equal(HttpStatusCode.Redirect,login.StatusCode);Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/QuickVisit")).StatusCode);
        await using(var db=database.CreateDbContext()){var row=await db.Clinics.SingleAsync(x=>x.ID==c);row.EnabledModules=0;row.IsWhatsAppEnabled=false;await db.SaveChangesAsync();}
        foreach(var url in new[]{"/QuickVisit","/OwnerFollowUp","/WhatsApp","/Workday"})Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(url)).StatusCode);
        foreach(var url in new[]{"/Patient","/PetOwner","/Appointment","/Muayene","/Finance","/ExcelTransfer","/Vaccination","/VaccineType","/ServiceItem"})Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(url)).StatusCode);
        var html=await client.GetStringAsync("/Patient");Assert.DoesNotContain("href=\"/QuickVisit\"",html);Assert.DoesNotContain("href=\"/OwnerFollowUp\"",html);Assert.DoesNotContain("href=\"/WhatsApp\"",html);
        using var changed=await client.PostAsync("/Dealer/Modules/"+c,new FormUrlEncodedContent(new Dictionary<string,string>{{"quickVisit","true"},{"__RequestVerificationToken",Csrf(html)}}));Assert.NotEqual(HttpStatusCode.OK,changed.StatusCode);await using var verify=database.CreateDbContext();Assert.Equal(0,(await verify.Clinics.SingleAsync(x=>x.ID==c)).EnabledModules);
    }
    [Fact] public async Task Secondary_owner_primary_switch_and_restore_preserve_single_primary()
    {
        var(c,o,p,u,_)=await Seed();await using var db=database.CreateDbContext();var service=new VoxCrm.Infrastructure.Patients.PatientService(db,new Tenant(c),new Audit());
        var other=new PetOwner{ClinicID=c,FirstName="Second",Phone="+905552224455"};db.Add(other);await db.SaveChangesAsync();
        Assert.True((await service.AddOwnerAsync(p,other.ID)).Succeeded);Assert.Equal(1,await db.PatientOwners.CountAsync(x=>x.PatientId==p&&x.IsPrimaryOwner&&x.IsActive));Assert.False((await db.PatientOwners.SingleAsync(x=>x.PetOwnerId==other.ID)).IsPrimaryOwner);
        Assert.False((await service.RemoveOwnerAsync(p,o,u)).Succeeded);Assert.True((await service.SetPrimaryOwnerAsync(p,other.ID,u)).Succeeded);Assert.True((await service.RemoveOwnerAsync(p,o,u)).Succeeded);Assert.True((await service.AddOwnerAsync(p,o)).Succeeded);
        Assert.Equal(other.ID,(await db.PatientOwners.SingleAsync(x=>x.PatientId==p&&x.IsPrimaryOwner&&x.IsActive)).PetOwnerId);Assert.Equal(2,await db.PatientOwners.CountAsync(x=>x.PatientId==p&&x.IsActive));
    }
    [Fact] public async Task Vaccination_duplicate_rules_apply_to_create_edit_and_restore()
    {
        var(c,_,p,u,t)=await Seed();await using var db=database.CreateDbContext();var service=new VaccinationService(db,new Tenant(c),new Audit());
        VaccinationRecord Record(DateTime date)=>new(){ClinicID=c,PatientId=p,VaccineTypeId=t,AdministeredDate=DateTime.SpecifyKind(date,DateTimeKind.Utc)};
        var first=Record(DateTime.UtcNow.Date.AddDays(-2));var second=Record(DateTime.UtcNow.Date.AddDays(-4));Assert.True((await service.CreateAsync(first)).Succeeded);Assert.True((await service.CreateAsync(second)).Succeeded);
        Assert.False((await service.CreateAsync(Record(first.AdministeredDate))).Succeeded);
        var edit=Record(first.AdministeredDate);edit.ID=second.ID;Assert.False((await service.UpdateAsync(edit)).Succeeded);Assert.Equal(1,await db.VaccinationRecords.CountAsync(x=>x.PatientId==p&&x.IsActive&&x.AdministeredDate.Date==first.AdministeredDate.Date));
        Assert.True((await service.ArchiveAsync(first.ID,u)).Succeeded);Assert.True((await service.CreateAsync(Record(first.AdministeredDate))).Succeeded);Assert.False((await service.RestoreAsync(first.ID,u)).Succeeded);Assert.False((await db.VaccinationRecords.SingleAsync(x=>x.ID==first.ID)).IsActive);
    }
    [Fact] public async Task Concurrent_single_and_batch_vaccination_share_duplicate_lock()
    {
        var(c,_,p,u,t)=await Seed();var date=DateTime.UtcNow.Date.AddDays(-1);
        async Task<bool> Single(){await using var db=database.CreateDbContext();return(await new VaccinationService(db,new Tenant(c),new Audit()).CreateAsync(new(){ClinicID=c,PatientId=p,VaccineTypeId=t,AdministeredDate=date})).Succeeded;}
        async Task<bool> Batch(){await using var db=database.CreateDbContext();return(await new VaccinationService(db,new Tenant(c),new Audit()).CreateBatchAsync(new(p,date,Guid.NewGuid(),[new(t,null)]),u)).Succeeded;}
        var outcomes=await Task.WhenAll(Single(),Batch());Assert.Equal(1,outcomes.Count(x=>x));await using var verify=database.CreateDbContext();Assert.Equal(1,await verify.VaccinationRecords.CountAsync(x=>x.PatientId==p&&x.IsActive));
    }
    [Theory]
    [InlineData("2026-10-31T20:59:59Z",2026,10,31)]
    [InlineData("2026-10-31T21:00:00Z",2026,11,1)]
    public void Clinic_day_rolls_at_local_midnight_and_finance_uses_that_day(string instant,int year,int month,int day)
    {
        var today=ClinicTime.Today("Europe/Istanbul",DateTimeOffset.Parse(instant));Assert.Equal(new DateOnly(year,month,day),today);
        var account=new FinanceAccount{DueDate=new DateOnly(2026,10,31)};account.Charges.Add(new FinanceCharge{Amount=100});var summary=FinanceService.Summarize(account,today);Assert.Equal(today.Day==1?100:0,summary.Overdue);
    }
    [Fact] public async Task Stale_dealer_edit_cannot_reenable_module_and_missing_revision_cannot_change_it()
    {
        var(c,_,_,u,_)=await Seed();Guid dealer;await using(var db=database.CreateDbContext()){var clinic=await db.Clinics.SingleAsync(x=>x.ID==c);clinic.IsWhatsAppEnabled=true;dealer=clinic.DealerId;await db.SaveChangesAsync();}
        await using(var db=database.CreateDbContext())await new ClinicModules(db,new Tenant(c)).SaveAsync(dealer,u,c,0,false,false,false,default);
        async Task<bool> Update(long? revision){await using var db=database.CreateDbContext();var repository=new VoxCrm.Infrastructure.Clinics.ClinicManagementRepository(db,null!,null!);return(await repository.UpdateAsync(new VoxCrm.Application.Clinics.ClinicUpdate(dealer,c,"Clinic","","","",true,null,false,new TimeOnly(9,0),new TimeOnly(19,0),"Europe/Istanbul",revision),default)).Succeeded;}
        Assert.False(await Update(0));Assert.False(await Update(null));await using(var db=database.CreateDbContext())Assert.False((await db.Clinics.SingleAsync(x=>x.ID==c)).IsWhatsAppEnabled);Assert.True(await Update(1));
    }
    [Fact] public async Task Migration_backfills_legacy_import_manifests_and_preserves_undo()
    {
        var(c,_,_,u,_)=await Seed();using var cache=new MemoryCache(new MemoryCacheOptions());Guid token;
        await using(var db=database.CreateDbContext())
        {
            var service=new ExcelTransferService(db,new Tenant(c),cache);using var file=File(["Legacy","Owner","05553214567","LegacyPet","Kedi","","","",""]);token=await service.UploadAsync(file,u,default);await service.MapAsync(u,token,Map,default);await service.ConfirmAsync(u,token,[2],default);
        }
        await using(var migrationDb=database.CreateDbContext())
        {
            var migrator=migrationDb.GetService<IMigrator>();
            try{await migrator.MigrateAsync("20261031102702_AddClinicModuleSwitches");}
            finally{await migrator.MigrateAsync();}
        }
        await using var check=database.CreateDbContext();var batch=await check.ExcelImportBatches.SingleAsync();Assert.Equal(token,batch.SubmissionId);Assert.Equal(1,batch.PatientCount);Assert.Equal(u,batch.ActorUserId);
        await check.SystemAuditLogs.ExecuteDeleteAsync();await new ExcelTransferService(check,new Tenant(c),cache).UndoAsync(u,token,default);Assert.False((await check.Patients.SingleAsync(x=>x.Name=="LegacyPet")).IsActive);
    }
    [Fact] public async Task Excel_undo_and_history_survive_audit_retention()
    {
        var(c,_,_,u,_)=await Seed();using var cache=new MemoryCache(new MemoryCacheOptions());await using var db=database.CreateDbContext();var service=new ExcelTransferService(db,new Tenant(c),cache);
        using var file=File(["New","Owner","05553214567","Imported","Kedi","","","",""]);var token=await service.UploadAsync(file,u,default);await service.MapAsync(u,token,Map,default);await service.ConfirmAsync(u,token,[2],default);
        await db.SystemAuditLogs.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,DateTime.UtcNow.AddDays(-400)));await new VoxCrm.Infrastructure.Jobs.DataRetentionJob(db,new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()).ExecuteAsync();
        Assert.Empty(await db.SystemAuditLogs.ToListAsync());Assert.Single(await db.ExcelImportBatches.ToListAsync());Assert.Single(await service.HistoryAsync(default));Assert.Equal(0,await service.ConfirmAsync(u,token,[2],default));await service.UndoAsync(u,token,default);Assert.False((await db.Patients.SingleAsync(x=>x.Name=="Imported")).IsActive);Assert.NotNull((await db.ExcelImportBatches.SingleAsync()).UndoneAt);
    }
    [Fact] public async Task Excel_preview_selective_atomic_import_idempotency_and_safe_undo()
    {
        var(c,_,_,u,_)=await Seed();using var cache=new MemoryCache(new MemoryCacheOptions());await using var db=database.CreateDbContext();var service=new ExcelTransferService(db,new Tenant(c),cache);
        using var file=File(["Burak","Test","05553214567","Bitter","Kedi","Tekir","2020-10-01","00012345","=HYPERLINK(\"bad\")"],["Burak","Test","05553214567","Luna","Kedi","","","00012346",""]);
        var token=await service.UploadAsync(file,u,default);var stage=await service.MapAsync(u,token,Map,default);Assert.All(stage.Checks!,x=>Assert.False(x.Blocked));Assert.Equal(1,await db.Patients.CountAsync());
        Assert.Equal(2,await service.ConfirmAsync(u,token,[2,3],default));Assert.Equal(0,await service.ConfirmAsync(u,token,[2,3],default));Assert.Equal(3,await db.Patients.CountAsync());Assert.Equal(2,await db.PetOwners.CountAsync());Assert.False((await db.PetOwners.SingleAsync(x=>x.FirstName=="Burak")).WhatsAppConsent);Assert.Empty(await db.WhatsAppNotifications.ToListAsync());
        var bytes=await service.ExportAsync(false,default);using(var exported=new XLWorkbook(new MemoryStream(bytes))){var sheet=exported.Worksheet(1);var row=sheet.RowsUsed().Single(x=>x.Cell(4).GetString()=="Bitter");Assert.Equal("00012345",row.Cell(8).GetString());Assert.StartsWith("=HYPERLINK",row.Cell(9).GetString());Assert.False(row.Cell(9).HasFormula);Assert.Equal(4,sheet.RowsUsed().Count());}
        await service.UndoAsync(u,token,default);Assert.Equal(1,await db.Patients.CountAsync(x=>x.IsActive));Assert.Equal(1,await db.PetOwners.CountAsync(x=>x.IsActive));await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UndoAsync(u,token,default));
    }
    [Fact] public async Task Excel_conflicts_and_tenant_actor_bound_staging_reject_unsafe_rows()
    {
        var(c,o,_,u,_)=await Seed();using var cache=new MemoryCache(new MemoryCacheOptions());await using var db=database.CreateDbContext();var service=new ExcelTransferService(db,new Tenant(c),cache);
        using var file=File(["Ayse","Test","05551112233","Boncuk","Kedi","","","",""],["Burak","Other","05551112233","Mavi","Kedi","","","",""]);
        var token=await service.UploadAsync(file,u,default);var stage=await service.MapAsync(u,token,Map,default);Assert.All(stage.Checks!,x=>Assert.True(x.Blocked));await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ConfirmAsync(u,token,[2],default));
        Assert.Throws<InvalidOperationException>(()=>service.Get(Guid.NewGuid(),token));Assert.Throws<InvalidOperationException>(()=>new ExcelTransferService(db,new Tenant(Guid.NewGuid()),cache).Get(u,token));
        using var existing=File(["Ayse","Test","05551112233","NewPet","Kedi","","","",""]);var key=await service.UploadAsync(existing,u,default);Assert.Equal(o,(await service.MapAsync(u,key,Map,default)).Checks!.Single().OwnerId);await service.ConfirmAsync(u,key,[2],default);Assert.Equal(1,await db.PetOwners.CountAsync());
        var imported=await db.Patients.SingleAsync(x=>x.Name=="NewPet");imported.Notes="Sonradan düzenlendi";await db.SaveChangesAsync();await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UndoAsync(u,key,default));Assert.True(imported.IsActive);
    }
    [Fact] public async Task Excel_formulas_and_ambiguous_dates_rejected_and_new_work_blocks_undo()
    {
        var(c,_,_,u,_)=await Seed();using var cache=new MemoryCache(new MemoryCacheOptions());await using var db=database.CreateDbContext();var service=new ExcelTransferService(db,new Tenant(c),cache);
        using var file=File(["A","B","05553214567","New","Kedi","","03/04/05","",""]);var token=await service.UploadAsync(file,u,default);Assert.True((await service.MapAsync(u,token,Map,default)).Checks!.Single().Blocked);
        using var invalid=File(["A","B","05553214567","New","Kedi","","","",""]);using(var wb=new XLWorkbook(invalid)){wb.Worksheet(1).Cell(2,9).FormulaA1="1+1";using var data=new MemoryStream();wb.SaveAs(data);data.Position=0;await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UploadAsync(data,u,default));}
        using var good=File(["A","B","05553214567","New","Kedi","","","",""]);var key=await service.UploadAsync(good,u,default);await service.MapAsync(u,key,Map,default);await service.ConfirmAsync(u,key,[2],default);var p=await db.Patients.SingleAsync(x=>x.Name=="New");db.Muayeneler.Add(new Muayene{ClinicID=c,PatientId=p.ID,Subjective="İşlem"});await db.SaveChangesAsync();await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UndoAsync(u,key,default));Assert.True(p.IsActive);
    }
    [Fact] public async Task Quick_visit_preview_does_not_write_and_commit_saves_everything_once()
    {
        var(c,o,p,u,t)=await Seed();using var cache=new MemoryCache(new MemoryCacheOptions());await using var db=database.CreateDbContext();var service=Visit(db,c,cache);var input=Input(p,o,t);
        var preview=await service.PreviewAsync(input,u,default);Assert.Equal(4.2m,preview.Weight);Assert.Empty(await db.Muayeneler.ToListAsync());Assert.Empty(await db.FinanceAccounts.ToListAsync());Assert.Empty(await db.VaccinationRecords.ToListAsync());
        var id=await service.CommitAsync(u,"Vet",input.SubmissionId,default);Assert.Equal(id,await service.CommitAsync(u,"Vet",input.SubmissionId,default));Assert.Single(await db.Muayeneler.ToListAsync());Assert.Single(await db.VaccinationRecords.ToListAsync());Assert.Equal(1500,(await db.FinanceCharges.SingleAsync()).Amount);Assert.Equal(1000,(await db.FinanceReceipts.SingleAsync()).Amount);Assert.Equal("Draft",(await db.CareCards.SingleAsync()).State);Assert.Empty(await db.WhatsAppNotifications.ToListAsync());Assert.NotEmpty(await db.FinanceChanges.ToListAsync());
    }
    [Fact] public async Task Quick_visit_failure_rolls_back_exam_vaccines_finance_and_card()
    {
        var(c,o,p,u,t)=await Seed();using var cache=new MemoryCache(new MemoryCacheOptions());await using var db=database.CreateDbContext();var service=Visit(db,c,cache);var m=Input(p,o,t);await service.PreviewAsync(m,u,default);
        // Existing same-day application forces the real batch service to reject after the exam was staged.
        db.VaccinationRecords.Add(new VaccinationRecord{ClinicID=c,PatientId=p,VaccineTypeId=t,AdministeredDate=DateTime.SpecifyKind(DateTime.Today,DateTimeKind.Utc),NextDueDate=DateTime.SpecifyKind(DateTime.Today.AddDays(60),DateTimeKind.Utc)});await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.CommitAsync(u,"Vet",m.SubmissionId,default));await using var verify=database.CreateDbContext();Assert.Empty(await verify.Muayeneler.ToListAsync());Assert.Empty(await verify.FinanceAccounts.ToListAsync());Assert.Empty(await verify.CareCards.ToListAsync());Assert.Single(await verify.VaccinationRecords.ToListAsync());
    }
    [Fact] public async Task Quick_visit_foreign_references_invalid_money_stale_recipient_and_other_actor_denied()
    {
        var(c,o,p,u,t)=await Seed();using var cache=new MemoryCache(new MemoryCacheOptions());await using var db=database.CreateDbContext();var service=Visit(db,c,cache);var m=Input(p,o,t);m.Paid="1600";await Assert.ThrowsAsync<InvalidOperationException>(()=>service.PreviewAsync(m,u,default));m.Paid="1000";m.OwnerId=Guid.NewGuid();await Assert.ThrowsAsync<InvalidOperationException>(()=>service.PreviewAsync(m,u,default));m.OwnerId=o;m.VaccineIds=[Guid.NewGuid()];await Assert.ThrowsAsync<InvalidOperationException>(()=>service.PreviewAsync(m,u,default));m.VaccineIds=[t];await service.PreviewAsync(m,u,default);Assert.Throws<InvalidOperationException>(()=>service.Get(Guid.NewGuid(),m.SubmissionId));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Visit(db,Guid.NewGuid(),cache).PreviewAsync(m,u,default));var owner=await db.PetOwners.SingleAsync(x=>x.ID==o);owner.Phone="05559998877";await db.SaveChangesAsync();await Assert.ThrowsAsync<InvalidOperationException>(()=>service.CommitAsync(u,"Vet",m.SubmissionId,default));Assert.Empty(await db.Muayeneler.ToListAsync());
    }
    [Theory][InlineData("4 kilo 200 gram. Toplam 1500 lira. 1000 lira nakit ödendi.","4.2","1500","1000")][InlineData("4,2 kg. Toplam 1.500,50 TL. 500 lira ödendi.","4.2","1500.50","500")][InlineData("Dört kilo. Ücret daha sonra belirlenecek.",null,null,null)]
    public void Numeric_parser_is_explicit_and_does_not_invent_unknown_values(string text,string? weight,string? amount,string? paid){var result=QuickVisitService.Parse(text);Assert.Equal(weight,result.Weight);Assert.Equal(amount,result.Amount);Assert.Equal(paid,result.Paid);}
    [Fact] public async Task Real_forms_render_enforce_csrf_and_upload_map_preview_export_flow()
    {
        var(c,o,p,u,t)=await Seed();await using var factory=new WebApplicationFactoryForTests(database.ConnectionString);using var client=factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        string Csrf(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var login=await client.GetStringAsync("/Auth/Login");using var signIn=await client.PostAsync("/Auth/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"email","transfer@security.test"},{"password","LocalSecurity123!"},{"__RequestVerificationToken",Csrf(login)}}));Assert.Equal(HttpStatusCode.Redirect,signIn.StatusCode);
        var index=await client.GetStringAsync("/ExcelTransfer");Assert.Contains("Excel",index);Assert.Contains("no-store",(await client.GetAsync("/ExcelTransfer/Export")).Headers.CacheControl!.ToString());var voice=await client.GetStringAsync("/QuickVisit");Assert.Contains("quick-visit.js",voice);
        foreach(var path in new[]{"/ExcelTransfer/Confirm","/ExcelTransfer/Undo","/QuickVisit/Commit","/QuickVisit/Parse"})Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(path,new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        using var file=File(["New","Person","05553214567","Fresh","Kedi","","","",""]);using var form=new MultipartFormDataContent();form.Add(new StringContent(Csrf(index)),"__RequestVerificationToken");form.Add(new StreamContent(file),"file","test.xlsx");using var uploaded=await client.PostAsync("/ExcelTransfer/Upload",form);Assert.Equal(HttpStatusCode.Redirect,uploaded.StatusCode);var mapping=await client.GetStringAsync(uploaded.Headers.Location);Assert.Contains("columns",mapping);var token=Regex.Match(mapping,"name=\"token\" value=\"([^\"]+)").Groups[1].Value;Assert.NotEmpty(token);
        var fields=new List<KeyValuePair<string,string>>{new("token",token),new("__RequestVerificationToken",Csrf(mapping))};fields.AddRange(Map.Select(x=>new KeyValuePair<string,string>("columns",x.ToString())));using var preview=await client.PostAsync("/ExcelTransfer/Preview",new FormUrlEncodedContent(fields));Assert.Equal(HttpStatusCode.OK,preview.StatusCode);Assert.Contains("Fresh",await preview.Content.ReadAsStringAsync());
        var previewHtml=await preview.Content.ReadAsStringAsync();using var confirmed=await client.PostAsync("/ExcelTransfer/Confirm",new FormUrlEncodedContent(new Dictionary<string,string>{{"token",token},{"selected","2"},{"__RequestVerificationToken",Csrf(previewHtml)}}));Assert.Equal(HttpStatusCode.Redirect,confirmed.StatusCode);
        var visitFields=new Dictionary<string,string>{{"PatientId",p.ToString()},{"OwnerId",o.ToString()},{"SubmissionId",Guid.NewGuid().ToString()},{"VisitDate",DateTime.Today.ToString("yyyy-MM-dd")},{"DueDate",DateTime.Today.ToString("yyyy-MM-dd")},{"Notes","HTTP kullanıcı denemesi"},{"Weight","4"},{"Amount","1500"},{"Paid","1000"},{"PaymentMethod","Nakit"},{"VaccineIds",t.ToString()},{"__RequestVerificationToken",Csrf(voice)}};
        using var visitPreview=await client.PostAsync("/QuickVisit/Preview",new FormUrlEncodedContent(visitFields));Assert.Equal(HttpStatusCode.OK,visitPreview.StatusCode);var visitHtml=await visitPreview.Content.ReadAsStringAsync();Assert.Contains("Son kontrol",visitHtml);var visitToken=Regex.Match(visitHtml,"name=\"token\" value=\"([^\"]+)").Groups[1].Value;Assert.NotEmpty(visitToken);
        using var commit=await client.PostAsync("/QuickVisit/Commit",new FormUrlEncodedContent(new Dictionary<string,string>{{"token",visitToken},{"confirmed","true"},{"__RequestVerificationToken",Csrf(visitHtml)}}));Assert.Equal(HttpStatusCode.Redirect,commit.StatusCode);Assert.Contains("/Muayene/Details/",commit.Headers.Location!.ToString());await using var verify=database.CreateDbContext();Assert.Single(await verify.Muayeneler.ToListAsync());Assert.Equal(1000,(await verify.FinanceReceipts.SingleAsync()).Amount);Assert.Empty(await verify.WhatsAppNotifications.ToListAsync());

    }
}
