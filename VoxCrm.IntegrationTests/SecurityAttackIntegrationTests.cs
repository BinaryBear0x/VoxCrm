extern alias VoxCrmApi;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Security;
using VoxCrm.IntegrationTests.Infrastructure;
using GatewayAuth=VoxCrmApi::VoxCrm.Api.Security.GatewayJwtAuthenticator;
namespace VoxCrm.IntegrationTests;
[Collection(DatabaseCollection.Name)]
public sealed class SecurityAttackIntegrationTests(PostgresDatabaseFixture database)
{
    private sealed record Target(Guid Clinic,Guid Owner,Guid Patient,Guid Exam,Guid Vaccine,Guid Type,Guid Appointment,Guid Account,Guid Task,Guid User,string Email);
    private static string Field(string html,string name="__RequestVerificationToken")=>WebUtility.HtmlDecode(Regex.Match(html,"name=\""+Regex.Escape(name)+"\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private async Task<(Target A,Target B)> Seed()
    {
        await using var db=database.CreateDbContext();await TestData.ClearWhatsAppDataAsync(db);
        var role=await db.Roles.SingleOrDefaultAsync(x=>x.Name=="Clinic");if(role==null){role=new IdentityRole<Guid>("Clinic"){Id=Guid.NewGuid(),NormalizedName="CLINIC"};db.Add(role);await db.SaveChangesAsync();}
        async Task<Target> Add(string tag)
        {
            var(c,o)=await TestData.CreateClinicWithOwnerAsync(db,"Security "+tag);var p=new Patient{ClinicID=c.ID,Name=tag+"-secret-patient",Species="Kedi"};var v=new VaccineType{ClinicID=c.ID,Name=tag+"-vaccine",ValidityDays=90,ReminderDaysBefore=1};var e=new Muayene{ClinicID=c.ID,PatientId=p.ID,Subjective=tag+"-private-exam"};var a=new Appointment{ClinicID=c.ID,PatientId=p.ID,ScheduledAt=DateTime.UtcNow.AddDays(2),Status="Planlandı",AppointmentType="Kontrol"};var r=new VaccinationRecord{ClinicID=c.ID,PatientId=p.ID,VaccineTypeId=v.ID,AdministeredDate=DateTime.UtcNow.Date,NextDueDate=DateTime.UtcNow.Date.AddDays(90)};var f=new FinanceAccount{ClinicID=c.ID,CustomerName=tag+"-private-account",DueDate=DateOnly.FromDateTime(DateTime.UtcNow)};var t=new ClinicTask{ClinicID=c.ID,Title=tag+"-private-task",PatientId=p.ID,SubmissionId=Guid.NewGuid(),DueDate=DateOnly.FromDateTime(DateTime.UtcNow)};
            var u=new ApplicationUser{Id=Guid.NewGuid(),ClinicId=c.ID,Email=tag+"@security.test",UserName=tag+"@security.test",NormalizedEmail=(tag+"@security.test").ToUpperInvariant(),NormalizedUserName=(tag+"@security.test").ToUpperInvariant(),SecurityStamp=Guid.NewGuid().ToString(),ConcurrencyStamp=Guid.NewGuid().ToString()};u.PasswordHash=new PasswordHasher<ApplicationUser>().HashPassword(u,"LocalSecurity123!");
            db.AddRange(p,v,e,a,r,f,t,u,new PatientOwner{ClinicID=c.ID,PatientId=p.ID,PetOwnerId=o.ID},new IdentityUserRole<Guid>{UserId=u.Id,RoleId=role.Id});await db.SaveChangesAsync();return new(c.ID,o.ID,p.ID,e.ID,r.ID,v.ID,a.ID,f.ID,t.ID,u.Id,u.Email);
        }
        return(await Add("alpha"),await Add("bravo"));
    }
    private async Task<HttpClient> Login(WebApplicationFactoryForTests factory,Target target,string? redirect=null)
    {
        var client=factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions{AllowAutoRedirect=false});var html=await client.GetStringAsync("/Auth/Login");using var response=await client.PostAsync("/Auth/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"email",target.Email},{"password","LocalSecurity123!"},{"returnUrl",redirect??"/"},{"__RequestVerificationToken",Field(html)}}));Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.DoesNotContain("evil.example",response.Headers.Location!.ToString());return client;
    }
    [Fact]
    public async Task Idor_read_matrix_cannot_cross_clinic_or_disclose_records()
    {
        var(a,b)=await Seed();await using var factory=new WebApplicationFactoryForTests(database.ConnectionString);using var client=await Login(factory,a);
        foreach(var route in new[]{"/Patient/Details/"+b.Patient,"/Patient/Edit/"+b.Patient,"/PetOwner/Details/"+b.Owner,"/PetOwner/Edit/"+b.Owner,"/Muayene/Details/"+b.Exam,"/Muayene/Edit/"+b.Exam,"/Vaccination/Edit/"+b.Vaccine,"/VaccineType/Edit/"+b.Type,"/Appointment/Edit/"+b.Appointment,"/Finance/Details/"+b.Account,"/Finance/Entry?accountId="+b.Account+"&kind=charge","/Workday/Task/"+b.Task,"/Workday/Care/"+b.Exam,"/OwnerFollowUp?ownerId="+b.Owner})
        {using var response=await client.GetAsync(route);Assert.True(response.StatusCode==HttpStatusCode.NotFound,$"{route}: {response.StatusCode}");Assert.DoesNotContain("bravo-private",await response.Content.ReadAsStringAsync());}
        foreach(var route in new[]{"/Patient","/PetOwner","/Muayene","/Finance","/Workday","/Patient/Search?q=bravo","/PetOwner/Search?q=bravo"}){var html=await client.GetStringAsync(route);Assert.DoesNotContain("bravo",html,StringComparison.OrdinalIgnoreCase);}
    }
    [Fact]
    public async Task Idor_write_matrix_and_csrf_cannot_change_foreign_data()
    {
        var(a,b)=await Seed();await using var factory=new WebApplicationFactoryForTests(database.ConnectionString);using var client=await Login(factory,a);var page=await client.GetStringAsync("/Patient");var csrf=Field(page);
        foreach(var route in new[]{"/Patient/Delete","/PetOwner/Delete","/Muayene/Delete","/Vaccination/Delete","/VaccineType/Delete","/Appointment/Delete","/Finance/Delete","/OwnerFollowUp/Revoke"})
        {var id=route.StartsWith("/Patient/")?b.Patient:route.StartsWith("/PetOwner/")?b.Owner:route.StartsWith("/Muayene/")?b.Exam:route.StartsWith("/Vaccination/")?b.Vaccine:route.StartsWith("/VaccineType/")?b.Type:route.StartsWith("/Appointment/")?b.Appointment:b.Account;using var denied=await client.PostAsync(route,new FormUrlEncodedContent(new Dictionary<string,string>{{"id",id.ToString()},{"revision","1"},{"__RequestVerificationToken",csrf}}));Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);}
        foreach(var route in new[]{"/Patient/Delete","/PetOwner/Delete","/Muayene/Delete","/Vaccination/Delete","/Finance/Delete","/OwnerFollowUp/Issue"}){using var noCsrf=await client.PostAsync(route,new FormUrlEncodedContent(new Dictionary<string,string>{{"id",a.Patient.ToString()}}));Assert.Equal(HttpStatusCode.BadRequest,noCsrf.StatusCode);}
        await using var db=database.CreateDbContext();Assert.True((await db.Patients.SingleAsync(x=>x.ID==b.Patient)).IsActive);Assert.True((await db.Muayeneler.SingleAsync(x=>x.ID==b.Exam)).IsActive);Assert.True((await db.FinanceAccounts.SingleAsync(x=>x.ID==b.Account)).IsActive);
    }
    [Fact]
    public async Task Stored_and_reflected_xss_sql_and_command_payloads_remain_inert_text()
    {
        var(a,_)=await Seed();await using var factory=new WebApplicationFactoryForTests(database.ConnectionString);using var client=await Login(factory,a,"https://evil.example/");
        var marker=Path.Combine(Path.GetTempPath(),"voxcrm-security-canary-"+Guid.NewGuid().ToString("N"));var payload="</textarea><script>window.__voxXss=1</script><img src=x onerror=alert(1)> {{7*7}}";
        var edit=await client.GetStringAsync("/Patient/Edit/"+a.Patient);
        using(var changed=await client.PostAsync("/Patient/Edit/"+a.Patient,new FormUrlEncodedContent(new Dictionary<string,string>{{"ID",a.Patient.ToString()},{"Name",payload},{"Species","Kedi"},{"Notes","$(touch "+marker+") & whoami ' OR 1=1 --"},{"ClinicID",Guid.NewGuid().ToString()},{"IsActive","false"},{"__RequestVerificationToken",Field(edit)}})))Assert.Equal(HttpStatusCode.Redirect,changed.StatusCode);
        var detail=await client.GetStringAsync("/Patient/Details/"+a.Patient);Assert.DoesNotContain("<script>window.__voxXss",detail);Assert.DoesNotContain("<img src=x",detail);Assert.Contains("&lt;script&gt;",detail);Assert.Contains("{{7*7}}",detail);Assert.False(File.Exists(marker));
        foreach(var q in new[]{"<svg onload=alert(1)>","' OR 1=1 --","../../appsettings.json","$(whoami)","${7*7}"}){using var search=await client.GetAsync("/Patient?search="+Uri.EscapeDataString(q));Assert.Equal(HttpStatusCode.OK,search.StatusCode);Assert.DoesNotContain("<svg onload",await search.Content.ReadAsStringAsync());}
        await using var db=database.CreateDbContext();var patient=await db.Patients.SingleAsync(x=>x.ID==a.Patient);Assert.Equal(a.Clinic,patient.ClinicID);Assert.True(patient.IsActive);Assert.Equal(payload,patient.Name);Assert.Equal(2,await db.Patients.CountAsync());
    }
    [Fact]
    public async Task Role_and_account_revocation_take_effect_for_existing_session()
    {
        var(a,_)=await Seed();await using var factory=new WebApplicationFactoryForTests(database.ConnectionString);using var client=await Login(factory,a);Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/Patient")).StatusCode);
        await using(var db=database.CreateDbContext()){var user=await db.Users.SingleAsync(x=>x.Id==a.User);user.LoginDisabled=true;await db.SaveChangesAsync();}
        using var disabled=await client.GetAsync("/Patient");Assert.NotEqual(HttpStatusCode.OK,disabled.StatusCode);
    }
    [Fact]
    public async Task Removed_role_rejects_existing_cookie()
    {
        var(a,_)=await Seed();await using var factory=new WebApplicationFactoryForTests(database.ConnectionString);using var client=await Login(factory,a);
        await using(var db=database.CreateDbContext()){await db.UserRoles.Where(x=>x.UserId==a.User).ExecuteDeleteAsync();}
        Assert.NotEqual(HttpStatusCode.OK,(await client.GetAsync("/Patient")).StatusCode);
    }
    [Fact]
    public async Task Sensitive_files_and_traversal_are_not_exposed_to_anonymous_clients()
    {
        await using var factory=new WebApplicationFactoryForTests(database.ConnectionString);using var client=factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        foreach(var path in new[]{"/.env","/appsettings.json","/appsettings.Development.json","/../appsettings.json","/%2e%2e/appsettings.json","/.git/config","/local-web.log","/backups/voxcrm-db.dump"}){using var response=await client.GetAsync(path);Assert.NotEqual(HttpStatusCode.OK,response.StatusCode);Assert.DoesNotContain("DefaultConnection",await response.Content.ReadAsStringAsync());}
    }
    [Fact]
    public async Task Encryption_prefix_text_survives_multiple_database_saves_and_reload()
    {
        var(a,_)=await Seed();var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".key");File.WriteAllText(path,Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        try
        {
            var protector=new AesGcmPiiProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"PiiEncryption:KeyFile",path}}).Build());
            var options=new DbContextOptionsBuilder<VoxCrm.Infrastructure.Data.VoxCrmDbContext>().UseNpgsql(database.ConnectionString).ConfigureWarnings(w=>w.Log(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)).AddInterceptors(new PiiEncryptionInterceptor(protector)).Options;
            await using(var db=new VoxCrm.Infrastructure.Data.VoxCrmDbContext(options)){var p=await db.Patients.SingleAsync(x=>x.ID==a.Patient);p.Notes="enc:v1:bad";using var cancelled=new CancellationTokenSource();cancelled.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>db.SaveChangesAsync(cancelled.Token));Assert.Equal("enc:v1:bad",p.Notes);await db.SaveChangesAsync();p.Species="Köpek";await db.SaveChangesAsync();Assert.Equal("enc:v1:bad",p.Notes);}
            await using(var raw=database.CreateDbContext())Assert.NotEqual("enc:v1:bad",(await raw.Patients.SingleAsync(x=>x.ID==a.Patient)).Notes);
            await using var reload=new VoxCrm.Infrastructure.Data.VoxCrmDbContext(options);Assert.Equal("enc:v1:bad",(await reload.Patients.SingleAsync(x=>x.ID==a.Patient)).Notes);
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public void Concurrent_signed_token_replay_authorizes_only_one_request()
    {
        using var cache=new MemoryCache(new MemoryCacheOptions());var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"WhatsAppGateway:JwtSecret",TestJwt.Secret},{"WhatsAppGateway:ApiIssuer",TestJwt.Issuer},{"WhatsAppGateway:ApiAudience",TestJwt.Audience}}).Build();var token=TestJwt.Create("whatsapp.notifications.claim");var accepted=0;
        Parallel.For(0,64,_=>{var http=new DefaultHttpContext();http.Request.Headers.Authorization="Bearer "+token;if(new GatewayAuth(config,cache).Authorize(http.Request,"whatsapp.notifications.claim").IsAuthorized)Interlocked.Increment(ref accepted);});Assert.Equal(1,accepted);
    }
    [Theory]
    [InlineData("/Dealer")][InlineData("/Dealer/Health")][InlineData("/hangfire")]
    public async Task Clinic_account_cannot_access_privileged_endpoints(string url)
    {var(a,_)=await Seed();await using var factory=new WebApplicationFactoryForTests(database.ConnectionString);using var client=await Login(factory,a);using var response=await client.GetAsync(url);Assert.NotEqual(HttpStatusCode.OK,response.StatusCode);}
    [Theory]
    [InlineData("enc:v1:bad")][InlineData("enc:v1:")][InlineData("enc:v1:AAAA")]
    public void Encryption_envelope_prefix_in_user_text_roundtrips_without_persistent_crash(string input)
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".key");File.WriteAllText(path,Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        try{var p=new AesGcmPiiProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"PiiEncryption:KeyFile",path}}).Build());var cipher=p.Protect(input);Assert.NotEqual(input,cipher);Assert.Equal(input,p.Unprotect(cipher));}finally{File.Delete(path);}
    }
    [Fact]
    public void Signed_malformed_jwt_fails_closed_instead_of_throwing()
    {
        using var cache=new MemoryCache(new MemoryCacheOptions());var auth=new GatewayAuth(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"WhatsAppGateway:JwtSecret",TestJwt.Secret},{"WhatsAppGateway:ApiIssuer",TestJwt.Issuer},{"WhatsAppGateway:ApiAudience",TestJwt.Audience}}).Build(),cache);
        foreach(var exp in new object[]{"not-a-number",long.MaxValue,new[]{1,2}}){var payload=new Dictionary<string,object?>{{"iss",TestJwt.Issuer},{"aud",TestJwt.Audience},{"scope","whatsapp.notifications.claim"},{"exp",exp},{"jti",Guid.NewGuid().ToString()}};var context=new DefaultHttpContext();context.Request.Headers.Authorization="Bearer "+TestJwt.CreateRaw(payload);Assert.False(auth.Authorize(context.Request,"whatsapp.notifications.claim").IsAuthorized);}
    }
}
