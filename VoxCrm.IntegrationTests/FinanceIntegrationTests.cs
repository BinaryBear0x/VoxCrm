using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Finance;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.IntegrationTests.Infrastructure;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Finance;
namespace VoxCrm.IntegrationTests;
[Collection(DatabaseCollection.Name)]
public sealed class FinanceIntegrationTests(PostgresDatabaseFixture database)
{
    private static readonly FinanceActor Actor = new(Guid.NewGuid(), "Test kullanıcısı");
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private async Task<Guid> Seed()
    {
        await using var db = database.CreateDbContext();
        await TestData.ClearWhatsAppDataAsync(db);
        var seed = await TestData.CreateClinicWithOwnerAsync(db, "Finance " + Guid.NewGuid().ToString("N"));
        return seed.Clinic.ID;
    }
    private VoxCrmDbContext Context(Guid clinic) => new(new DbContextOptionsBuilder<VoxCrmDbContext>().ConfigureWarnings(w => w.Log(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)).UseNpgsql(database.ConnectionString).Options, new Tenant(clinic));
    private static FinanceAccountInput Input(decimal amount = 100) => new() { CustomerMode = "manual", CustomerName = "Manuel Müşteri", Description = "Muayene", Amount = amount, DueDate = Today.AddDays(30), ChargeDate = Today };
    private static FinanceEntryInput Entry(Guid id, long revision, string kind, decimal amount, Guid? entryId = null) => new() { AccountId = id, Revision = revision, Kind = kind, Amount = amount, Id = entryId, Description = "Kalem", Date = Today, PaymentMethod = FinancePaymentMethods.Cash };
    [Fact]
    public async Task Manual_account_charge_receipt_edit_delete_and_history_are_consistent()
    {
        var clinic = await Seed(); await using var db = Context(clinic); var s = new FinanceService(db, new Tenant(clinic));
        var id = await s.CreateAsync(Input(), Actor);
        await s.SaveEntryAsync(Entry(id, 1, "charge", 50), Actor);
        var r = Entry(id, 2, "receipt", 60); r.Date = Today.AddDays(-2); await s.SaveEntryAsync(r, Actor);
        var d = await s.GetDetailsAsync(id); Assert.Equal(90, d.Remaining); Assert.Equal(Today.AddDays(-2), d.Account.Receipts.Single().PaidDate);
        var receipt = d.Account.Receipts.Single(); await s.SaveEntryAsync(Entry(id, 3, "receipt", 40, receipt.ID), Actor);
        await s.DeleteEntryAsync(id, "receipt", receipt.ID, 4, Actor);
        d = await s.GetDetailsAsync(id); Assert.Equal(150, d.Remaining); Assert.False(d.Account.Receipts.Single().IsActive);
        Assert.Contains(d.Account.Changes, x => x.Action == "Tahsilat güncellendi" && x.BeforeJson!.Contains("60") && x.AfterJson!.Contains("40"));
        Assert.All(d.Account.Changes, x => Assert.Equal(Actor.Id, x.ActorUserId));
        Assert.Empty(await db.WhatsAppNotifications.ToListAsync());
    }
    [Fact]
    public async Task Installments_round_exactly_and_deleting_paid_installment_keeps_receipt()
    {
        var clinic = await Seed(); await using var db = Context(clinic); var s = new FinanceService(db, new Tenant(clinic));
        var id = await s.CreateAsync(Input(), Actor); await s.GenerateInstallmentsAsync(id, 3, Today, 1, Actor);
        var d = await s.GetDetailsAsync(id); var i = d.Account.Installments.OrderBy(x => x.DueDate).First();
        Assert.Equal(100, d.Account.Installments.Sum(x => x.Amount)); Assert.Equal(0, d.Unplanned);
        var r = Entry(id, 2, "receipt", 10); r.InstallmentId = i.ID; await s.SaveEntryAsync(r, Actor);
        await s.DeleteEntryAsync(id, "installment", i.ID, 3, Actor);
        d = await s.GetDetailsAsync(id); Assert.Equal(90, d.Remaining); Assert.Null(d.Account.Receipts.Single().FinanceInstallmentId); Assert.True(d.Account.Receipts.Single().IsActive);
        Assert.Contains(d.Account.Changes, x => x.Action == "Tahsilat taksit bağlantısı kaldırıldı");
    }
    [Fact]
    public async Task Invalid_amounts_and_dates_do_not_write_audit_or_messages()
    {
        var clinic = await Seed(); await using var db = Context(clinic); var s = new FinanceService(db, new Tenant(clinic)); var id = await s.CreateAsync(Input(), Actor);
        foreach (var amount in new[] { 0m, -1m, 0.001m, 101m }) await Assert.ThrowsAsync<FinanceException>(() => s.SaveEntryAsync(Entry(id, 1, "receipt", amount), Actor));
        var future = Entry(id, 1, "receipt", 10); future.Date = Today.AddDays(1); await Assert.ThrowsAsync<FinanceException>(() => s.SaveEntryAsync(future, Actor));
        var d = await s.GetDetailsAsync(id); Assert.Empty(d.Account.Receipts); Assert.Equal(2, d.Account.Changes.Count); Assert.Equal(1, d.Account.Revision); Assert.Empty(await db.WhatsAppNotifications.ToListAsync());
    }
    [Fact]
    public async Task Concurrent_receipts_and_stale_pages_cannot_overcollect_or_duplicate()
    {
        var clinic = await Seed(); Guid id;
        await using(var db = Context(clinic)) id = await new FinanceService(db, new Tenant(clinic)).CreateAsync(Input(), Actor);
        async Task<bool> Pay() { await using var db = Context(clinic); try { await new FinanceService(db, new Tenant(clinic)).SaveEntryAsync(Entry(id, 1, "receipt", 80), Actor); return true; } catch(FinanceException) { return false; } }
        var result = await Task.WhenAll(Pay(), Pay()); Assert.Single(result, x => x);
        await using var verify = Context(clinic); var d = await new FinanceService(verify, new Tenant(clinic)).GetDetailsAsync(id); Assert.Equal(20, d.Remaining); Assert.Single(d.Account.Receipts);
    }
    [Fact]
    public async Task Duplicate_creation_is_idempotent_and_deleted_account_history_survives()
    {
        var clinic = await Seed(); await using var db = Context(clinic); var s = new FinanceService(db, new Tenant(clinic)); var input = Input(); var id = await s.CreateAsync(input, Actor);
        Assert.Equal(id, await s.CreateAsync(input, Actor)); await s.DeleteAsync(id, 1, Actor);
        Assert.Empty((await s.GetIndexAsync(null, null, false)).Accounts); Assert.Single((await s.GetIndexAsync(null, null, true)).Accounts);
        var d = await s.GetDetailsAsync(id); Assert.False(d.Account.IsActive); Assert.Contains(d.Account.Changes, x => x.Action == "Kayıt silindi");
    }
    [Fact]
    public async Task Tenant_cannot_read_or_write_other_clinics_account_or_child()
    {
        var clinic = await Seed(); Guid other; await using(var db = database.CreateDbContext()) { var seed = await TestData.CreateClinicWithOwnerAsync(db, "Other " + Guid.NewGuid().ToString("N")); other = seed.Clinic.ID; }
        await using var a = Context(clinic); var s = new FinanceService(a, new Tenant(clinic)); var id = await s.CreateAsync(Input(), Actor);
        await using var b = Context(other); var foreign = new FinanceService(b, new Tenant(other));
        await Assert.ThrowsAsync<FinanceException>(() => foreign.GetDetailsAsync(id)); await Assert.ThrowsAsync<FinanceException>(() => foreign.SaveEntryAsync(Entry(id, 1, "charge", 20), Actor));
        Assert.Empty((await foreign.GetIndexAsync(null, null, true)).Accounts);
    }
    [Fact]
    public async Task Plans_and_charge_reductions_cannot_exceed_balance_and_rollback_is_atomic()
    {
        var clinic = await Seed(); await using var db = Context(clinic); var s = new FinanceService(db, new Tenant(clinic)); var id = await s.CreateAsync(Input(), Actor);
        await s.GenerateInstallmentsAsync(id, 2, Today, 1, Actor);
        await Assert.ThrowsAsync<FinanceException>(() => s.SaveEntryAsync(Entry(id, 2, "receipt", 1), Actor));
        var d = await s.GetDetailsAsync(id); var charge = d.Account.Charges.Single();
        await Assert.ThrowsAsync<FinanceException>(() => s.SaveEntryAsync(Entry(id, 2, "charge", 50, charge.ID), Actor));
        d = await s.GetDetailsAsync(id); Assert.Equal(100, d.Total); Assert.Equal(2, d.Account.Revision); Assert.Empty(d.Account.Receipts);
    }
    [Fact]
    public async Task Account_metadata_charge_and_installment_can_be_updated_and_deleted()
    {
        var clinic = await Seed(); await using var db = Context(clinic); var s = new FinanceService(db, new Tenant(clinic)); var id = await s.CreateAsync(Input(), Actor);
        var input = Input(); input.Revision = 1; input.CustomerName = "Yeni isim"; await s.UpdateAsync(id, input, Actor);
        await s.SaveEntryAsync(Entry(id, 2, "installment", 30), Actor); var d = await s.GetDetailsAsync(id); var i = d.Account.Installments.Single();
        await s.SaveEntryAsync(Entry(id, 3, "installment", 40, i.ID), Actor); await s.DeleteEntryAsync(id, "installment", i.ID, 4, Actor);
        d = await s.GetDetailsAsync(id); await s.DeleteEntryAsync(id, "charge", d.Account.Charges.Single().ID, 5, Actor);
        d = await s.GetDetailsAsync(id); Assert.Equal("Yeni isim", d.Account.CustomerName); Assert.Equal(0, d.Total); Assert.Equal(0, d.Remaining);
    }
    [Fact]
    public async Task Registered_patient_must_belong_to_selected_owner_and_clinic()
    {
        var clinic = await Seed(); await using var db = Context(clinic); var s = new FinanceService(db, new Tenant(clinic)); var owner = await db.PetOwners.SingleAsync();
        var input = Input(); input.CustomerMode = "registered"; input.PetOwnerId = owner.ID;
        var id = await s.CreateAsync(input, Actor); Assert.Equal(owner.ID, (await s.GetDetailsAsync(id)).Account.PetOwnerId);
        input.SubmissionId = Guid.NewGuid(); input.PatientId = Guid.NewGuid(); await Assert.ThrowsAsync<FinanceException>(() => s.CreateAsync(input, Actor));
        input.CustomerMode = "manual"; input.SubmissionId = Guid.NewGuid(); input.CustomerName = "Serbest isim";
        var manual = await s.CreateAsync(input, Actor); var d = await s.GetDetailsAsync(manual); Assert.Null(d.Account.PetOwnerId); Assert.Null(d.Account.PatientId);
    }
    [Fact]
    public async Task Browser_forms_render_and_post_with_antiforgery_and_keep_correct_dashboard_balance()
    {
        var clinic = await Seed();
        await using var factory = new WebApplicationFactoryForTests(database.ConnectionString);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "finance-browser@example.test", Email = "finance-browser@example.test", EmailConfirmed = true, ClinicId = clinic, FirstName = "Test", LastName = "Kullanıcı" };
            Assert.True((await manager.CreateAsync(user, "Finance-Test-482!" )).Succeeded);
            Assert.True((await manager.AddToRoleAsync(user, "Clinic")).Succeeded);
        }
        async Task<string> Page(string url)
        {
            var response = await client.GetAsync(url); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadAsStringAsync();
        }
        static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task<HttpResponseMessage> Post(string url, string page, Dictionary<string,string> data)
        {
            data["__RequestVerificationToken"] = Token(page); return await client.PostAsync(url, new FormUrlEncodedContent(data));
        }
        var login = await Page("/Auth/Login");
        using(var response = await Post("/Auth/Login", login, new() { ["email"] = "finance-browser@example.test", ["password"] = "Finance-Test-482!" })) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var create = await Page("/Finance/Create"); Assert.Contains("Manuel isim", create);
        using var created = await Post("/Finance/Create", create, new() { ["CustomerMode"]="manual", ["CustomerName"]="Browser test", ["Description"]="Muayene", ["Amount"]="100.00", ["SubmissionId"]=Guid.NewGuid().ToString(), ["ChargeDate"]=Today.ToString("yyyy-MM-dd"), ["DueDate"]=Today.AddDays(10).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode); var location = created.Headers.Location!.ToString();
        var details = await Page(location); Assert.Contains("Browser test", details); Assert.DoesNotContain("Ters Kaydet", details);
        var accountId = Guid.Parse(location.Split('/').Last());
        foreach(var kind in new[] { "charge", "installment", "receipt" }) await Page($"/Finance/Entry?accountId={accountId}&kind={kind}");
        await Page($"/Finance/Edit/{accountId}");
        var entry = await Page($"/Finance/Entry?accountId={accountId}&kind=receipt");
        using(var paid = await Post("/Finance/Entry", entry, new() { ["AccountId"]=accountId.ToString(), ["Kind"]="receipt", ["Revision"]="1", ["Amount"]="40.00", ["Date"]=Today.ToString("yyyy-MM-dd"), ["PaymentMethod"]=FinancePaymentMethods.Cash })) Assert.Equal(HttpStatusCode.Redirect, paid.StatusCode);
        await Page(location); await Page("/Finance?status=open"); await Page("/");
        await using var verify = Context(clinic); var d = await new FinanceService(verify, new Tenant(clinic)).GetDetailsAsync(accountId);
        Assert.Equal(60, d.Remaining); Assert.Single(d.Account.Receipts);
        var dashboard = await new VoxCrm.Infrastructure.Dashboard.DashboardService(verify).GetClinicDashboardAsync(); Assert.Equal(60, dashboard.TotalOutstandingDebt);
        using(var rejected = await client.PostAsync("/Finance/Delete", new FormUrlEncodedContent(new Dictionary<string,string> { ["id"] = accountId.ToString(), ["revision"] = "2" }))) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(await verify.WhatsAppNotifications.ToListAsync());
    }
    [Fact]
    public async Task Protected_finance_fields_and_change_history_roundtrip_without_exposing_plaintext_at_rest()
    {
        var clinic = await Seed();
        var keyFile = Path.Combine(Path.GetTempPath(), "finance-pii-" + Guid.NewGuid().ToString("N") + ".key");
        await File.WriteAllTextAsync(keyFile, Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        try
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["PiiEncryption:KeyFile"] = keyFile }).Build();
            var protector = new VoxCrm.Infrastructure.Security.AesGcmPiiProtector(config);
            var options = new DbContextOptionsBuilder<VoxCrmDbContext>().ConfigureWarnings(w => w.Log(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)).UseNpgsql(database.ConnectionString).AddInterceptors(new VoxCrm.Infrastructure.Security.PiiEncryptionInterceptor(protector)).Options;
            Guid id;
            await using(var db = new VoxCrmDbContext(options, new Tenant(clinic)))
            {
                var service = new FinanceService(db, new Tenant(clinic)); id = await service.CreateAsync(Input(), Actor);
                var r = Entry(id, 1, "receipt", 10); r.Notes = "Özel ödeme notu"; await service.SaveEntryAsync(r, Actor);
            }
            await using(var db = Context(clinic))
            {
                var account = await db.FinanceAccounts.SingleAsync(); Assert.StartsWith("enc:v1:", account.CustomerName);
                Assert.All(await db.FinanceChanges.ToListAsync(), x => Assert.StartsWith("enc:v1:", x.AfterJson!));
                Assert.StartsWith("enc:v1:", (await db.FinanceReceipts.SingleAsync()).Notes!);
            }
            await using(var db = new VoxCrmDbContext(options, new Tenant(clinic)))
            {
                var d = await new FinanceService(db, new Tenant(clinic)).GetDetailsAsync(id);
                Assert.Equal("Manuel Müşteri", d.Account.CustomerName); Assert.Equal("Özel ödeme notu", d.Account.Receipts.Single().Notes);
                Assert.Contains(d.Account.Changes, x => x.AfterJson!.Contains("Amount")); Assert.Equal(90, d.Remaining);
            }
        }
        finally { File.Delete(keyFile); }
    }
    private sealed class Tenant(Guid id) : ITenantService { public Guid GetClinicId() => id; public bool IsSystemContext => false; }
}
