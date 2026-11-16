using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxCrm.Application.Clinics;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Clinics;
using VoxCrm.Infrastructure.Data;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ClinicAccessIntegrationTests(PostgresDatabaseFixture database)
{
    private const string DealerPassword = "DealerTest123!";
    private const string OldPassword = "ClinicTest123!";
    private const string NewPassword = "TemporaryTest456!";
    private ServiceProvider Services()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddDataProtection();
        services.AddDbContext<VoxCrmDbContext>(o => o.ConfigureWarnings(w => w.Log(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)).UseNpgsql(database.ConnectionString));
        services.AddIdentityCore<ApplicationUser>(o => { o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 8; })
            .AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<VoxCrmDbContext>().AddDefaultTokenProviders();
        services.AddScoped<IClinicAccessService, ClinicAccessService>();
        services.AddScoped<IClinicManagementRepository, ClinicManagementRepository>();
        return services.BuildServiceProvider();
    }
    private async Task<(Clinic Clinic, ApplicationUser Actor, ApplicationUser User)> Seed(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<VoxCrmDbContext>(); await TestData.ClearWhatsAppDataAsync(db);
        var (clinic, _) = await TestData.CreateClinicWithOwnerAsync(db, "Access " + Guid.NewGuid().ToString("N"));
        var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "Dealer", "Clinic", "SystemAdmin" }) if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new(role))).Succeeded);
        var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var actor = new ApplicationUser { UserName = "dealer@example.test", Email = "dealer@example.test", DealerId = clinic.DealerId, LockoutEnabled = true };
        var user = new ApplicationUser { UserName = "clinic@example.test", Email = "clinic@example.test", ClinicId = clinic.ID, FirstName = "Ada", LastName = "Vet", LockoutEnabled = true };
        Assert.True((await manager.CreateAsync(actor, DealerPassword)).Succeeded); Assert.True((await manager.AddToRoleAsync(actor, "Dealer")).Succeeded);
        Assert.True((await manager.CreateAsync(user, OldPassword)).Succeeded); Assert.True((await manager.AddToRoleAsync(user, "Clinic")).Succeeded);
        return (clinic, actor, user);
    }
    private static ClinicAccessCommand Command(Clinic clinic, ApplicationUser actor, ApplicationUser user, ClinicAccessOperation op) =>
        new(clinic.DealerId, clinic.ID, user.Id, actor.Id, op, DealerPassword, "Kullanıcının destek talebi", NewPassword: NewPassword);

    [Fact]
    public async Task Ownership_and_roles_are_checked_for_every_operation()
    {
        await using var services = Services(); await using var scope = services.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var (clinic, actor, user) = await Seed(sp); var service = sp.GetRequiredService<IClinicAccessService>(); var db = sp.GetRequiredService<VoxCrmDbContext>();
        var (other, _) = await TestData.CreateClinicWithOwnerAsync(db, "Other " + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAsync<ClinicAccessException>(() => service.GetAsync(clinic.DealerId, other.ID, actor.Id));
        foreach (var operation in Enum.GetValues<ClinicAccessOperation>())
        {
            var command = Command(clinic, actor, user, operation) with { ClinicId = other.ID };
            var error = await Assert.ThrowsAsync<ClinicAccessException>(() => service.ExecuteAsync(command)); Assert.True(error.NotFound);
        }
        await Assert.ThrowsAsync<ClinicAccessException>(() => service.GetAsync(clinic.DealerId, clinic.ID, user.Id));
        var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        user = (await manager.FindByIdAsync(user.Id.ToString()))!; Assert.True((await manager.AddToRoleAsync(user, "SystemAdmin")).Succeeded);
        Assert.Empty((await service.GetAsync(clinic.DealerId, clinic.ID, actor.Id)).Users);
        Assert.True((await Assert.ThrowsAsync<ClinicAccessException>(() => service.ExecuteAsync(Command(clinic, actor, user, ClinicAccessOperation.TemporaryPassword)))).NotFound);
        Assert.True(await manager.CheckPasswordAsync(user, OldPassword));
        Assert.Empty(await db.SystemAuditLogs.Where(x => x.Category == "ClinicAccess").ToListAsync());
    }

    [Fact]
    public async Task Temporary_password_forces_change_revokes_tokens_and_does_not_log_passwords()
    {
        await using var services = Services(); await using var scope = services.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var (clinic, actor, user) = await Seed(sp); var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var oldStamp = user.SecurityStamp; var reset = await manager.GeneratePasswordResetTokenAsync(user);
        var s = sp.GetRequiredService<IClinicAccessService>();
        await s.ExecuteAsync(Command(clinic, actor, user, ClinicAccessOperation.TemporaryPassword));
        user = (await manager.FindByIdAsync(user.Id.ToString()))!;
        Assert.True(user.MustChangePassword); Assert.NotEqual(oldStamp, user.SecurityStamp);
        Assert.True(await manager.CheckPasswordAsync(user, NewPassword)); Assert.False(await manager.CheckPasswordAsync(user, OldPassword));
        Assert.False(await manager.VerifyUserTokenAsync(user, manager.Options.Tokens.PasswordResetTokenProvider, UserManager<ApplicationUser>.ResetPasswordTokenPurpose, reset));
        var log = await sp.GetRequiredService<VoxCrmDbContext>().SystemAuditLogs.SingleAsync(x => x.Category == "ClinicAccess");
        Assert.Equal(actor.Id, log.ActorUserId); Assert.Equal(user.Id.ToString(), log.EntityId); Assert.Equal(clinic.ID, log.ClinicId);
        Assert.DoesNotContain(NewPassword, System.Text.Json.JsonSerializer.Serialize(log)); Assert.DoesNotContain(DealerPassword, System.Text.Json.JsonSerializer.Serialize(log));
    }

    [Fact]
    public async Task Suspended_user_stays_suspended_after_clinic_reactivation_and_can_be_enabled_explicitly()
    {
        await using var services = Services(); await using var scope = services.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var (clinic, actor, user) = await Seed(sp); var s = sp.GetRequiredService<IClinicAccessService>();
        await s.ExecuteAsync(Command(clinic, actor, user, ClinicAccessOperation.Suspend));
        var repo = sp.GetRequiredService<IClinicManagementRepository>();
        Assert.True((await repo.ChangeLifecycleAsync(new(clinic.DealerId, clinic.ID, false), default)).Succeeded);
        await Assert.ThrowsAsync<ClinicAccessException>(() => s.ExecuteAsync(Command(clinic, actor, user, ClinicAccessOperation.Enable)));
        Assert.True((await repo.ChangeLifecycleAsync(new(clinic.DealerId, clinic.ID, true), default)).Succeeded);
        var manager = sp.GetRequiredService<UserManager<ApplicationUser>>(); user = (await manager.FindByIdAsync(user.Id.ToString()))!;
        Assert.True(user.LoginDisabled); Assert.True(await manager.IsLockedOutAsync(user));
        await Assert.ThrowsAsync<ClinicAccessException>(() => s.ExecuteAsync(Command(clinic, actor, user, ClinicAccessOperation.Unlock)));
        await s.ExecuteAsync(Command(clinic, actor, user, ClinicAccessOperation.Enable)); user = (await manager.FindByIdAsync(user.Id.ToString()))!;
        Assert.False(user.LoginDisabled); Assert.False(await manager.IsLockedOutAsync(user));
    }

    [Fact]
    public async Task Mfa_reset_invalidates_authenticator_and_recovery_codes_and_revoke_preserves_password()
    {
        await using var services = Services(); await using var scope = services.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var (clinic, actor, user) = await Seed(sp); var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await manager.ResetAuthenticatorKeyAsync(user)).Succeeded); Assert.True((await manager.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        var oldKey = await manager.GetAuthenticatorKeyAsync(user); var codes = (await manager.GenerateNewTwoFactorRecoveryCodesAsync(user, 2))!.ToArray();
        var s = sp.GetRequiredService<IClinicAccessService>(); await s.ExecuteAsync(Command(clinic, actor, user, ClinicAccessOperation.ResetMfa));
        user = (await manager.FindByIdAsync(user.Id.ToString()))!;
        Assert.False(user.TwoFactorEnabled); Assert.NotEqual(oldKey, await manager.GetAuthenticatorKeyAsync(user));
        Assert.Equal(0, await manager.CountRecoveryCodesAsync(user)); Assert.False((await manager.RedeemTwoFactorRecoveryCodeAsync(user, codes[0])).Succeeded);
        var stamp = user.SecurityStamp; await s.ExecuteAsync(Command(clinic, actor, user, ClinicAccessOperation.RevokeSessions));
        Assert.NotEqual(stamp, user.SecurityStamp); Assert.True(await manager.CheckPasswordAsync(user, OldPassword));
    }

    [Fact]
    public async Task Creating_and_renewing_activation_is_single_use_and_duplicate_creation_rolls_back()
    {
        await using var services = Services(); await using var scope = services.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var (clinic, actor, user) = await Seed(sp); var s = sp.GetRequiredService<IClinicAccessService>();
        var command = new ClinicAccessCreate(clinic.DealerId, clinic.ID, actor.Id, DealerPassword, "Yeni çalışan", "Deniz", "Vet", "new@example.test");
        var created = await s.CreateAsync(command); var manager = sp.GetRequiredService<UserManager<ApplicationUser>>(); var added = (await manager.FindByIdAsync(created.UserId.ToString()))!;
        Assert.Equal(new[] { "Clinic" }, await manager.GetRolesAsync(added)); Assert.False(await manager.HasPasswordAsync(added));
        var renewed = await s.ExecuteAsync(Command(clinic, actor, added, ClinicAccessOperation.RenewActivation));
        var repo = sp.GetRequiredService<IClinicManagementRepository>();
        Assert.False((await repo.GetActivationStatusAsync(added.Id, created.ActivationToken!, default)).IsValid);
        Assert.True((await repo.ActivateAsync(added.Id, renewed.ActivationToken!, NewPassword, default)).Succeeded);
        Assert.False((await repo.GetActivationStatusAsync(added.Id, renewed.ActivationToken!, default)).IsValid);
        await Assert.ThrowsAsync<ClinicAccessException>(() => s.CreateAsync(command));
        Assert.Equal(2, await sp.GetRequiredService<VoxCrmDbContext>().Users.CountAsync(x => x.ClinicId == clinic.ID));
        Assert.Equal(2, await sp.GetRequiredService<VoxCrmDbContext>().SystemAuditLogs.CountAsync(x => x.Category == "ClinicAccess"));
    }

    [Fact]
    public async Task Profile_changes_validate_uniqueness_and_confirmation_before_writing()
    {
        await using var services = Services(); await using var scope = services.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var (clinic, actor, user) = await Seed(sp); var s = sp.GetRequiredService<IClinicAccessService>();
        var cmd = Command(clinic, actor, user, ClinicAccessOperation.UpdateProfile) with { FirstName = "Yeni", LastName = "Ad", Email = "changed@example.test" };
        await Assert.ThrowsAsync<ClinicAccessException>(() => s.ExecuteAsync(cmd with { DealerPassword = "wrong" }));
        await Assert.ThrowsAsync<ClinicAccessException>(() => s.ExecuteAsync(cmd with { Reason = " " }));
        await Assert.ThrowsAsync<ClinicAccessException>(() => s.ExecuteAsync(cmd with { Email = actor.Email }));
        var manager = sp.GetRequiredService<UserManager<ApplicationUser>>(); user = (await manager.FindByIdAsync(user.Id.ToString()))!; Assert.Equal("Ada", user.FirstName);
        await s.ExecuteAsync(cmd); user = (await manager.FindByIdAsync(user.Id.ToString()))!;
        Assert.Equal("changed@example.test", user.Email); Assert.Equal("CHANGED@EXAMPLE.TEST", user.NormalizedUserName); Assert.Equal("Yeni", user.FirstName);
        Assert.True(await manager.CheckPasswordAsync(user, OldPassword)); Assert.Single(await manager.GetRolesAsync(user));
        Assert.Single(await sp.GetRequiredService<VoxCrmDbContext>().SystemAuditLogs.Where(x => x.Category == "ClinicAccess").ToListAsync());
    }

    [Fact]
    public async Task Real_dealer_forms_enforce_antiforgery_ownership_revoke_cookies_and_force_clinic_password_change()
    {
        await using var services = Services(); await using var scope = services.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var (clinic, actor, user) = await Seed(sp); var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await manager.ResetAuthenticatorKeyAsync(actor)).Succeeded); Assert.True((await manager.SetTwoFactorEnabledAsync(actor, true)).Succeeded);
        var recovery = (await manager.GenerateNewTwoFactorRecoveryCodesAsync(actor, 1))!.Single();
        var (other, _) = await TestData.CreateClinicWithOwnerAsync(sp.GetRequiredService<VoxCrmDbContext>(), "Other " + Guid.NewGuid().ToString("N"));
        await using var factory = new WebApplicationFactoryForTests(database.ConnectionString);
        using var dealerClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var clinicClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        static string Token(string page) => System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        static async Task<System.Net.Http.HttpResponseMessage> Post(HttpClient client, string url, string html, Dictionary<string, string> form)
        {
            form["__RequestVerificationToken"] = Token(html); return await client.PostAsync(url, new FormUrlEncodedContent(form));
        }
        var dealerLogin = await dealerClient.GetStringAsync("/Auth/Login");
        using (var login = await Post(dealerClient, "/Auth/Login", dealerLogin, new() { ["email"] = actor.Email!, ["password"] = DealerPassword })) Assert.Contains("LoginWith2fa", login.Headers.Location!.ToString());
        var recoveryPage = await dealerClient.GetStringAsync("/Auth/LoginWithRecoveryCode");
        using (var login = await Post(dealerClient, "/Auth/LoginWithRecoveryCode", recoveryPage, new() { ["RecoveryCode"] = recovery })) Assert.Equal(System.Net.HttpStatusCode.Redirect, login.StatusCode);
        var list = await dealerClient.GetStringAsync($"/Dealer/Users/{clinic.ID}"); Assert.Contains(user.Email!, list);
        using (var forbidden = await dealerClient.GetAsync($"/Dealer/Users/{other.ID}")) Assert.Equal(System.Net.HttpStatusCode.NotFound, forbidden.StatusCode);
        var manageUrl = $"/Dealer/ManageUser/{clinic.ID}?userId={user.Id}";
        using (var noToken = await dealerClient.PostAsync(manageUrl, new FormUrlEncodedContent(new Dictionary<string,string> { ["Operation"]="Suspend" }))) Assert.Equal(System.Net.HttpStatusCode.BadRequest, noToken.StatusCode);
        var clinicLogin = await clinicClient.GetStringAsync("/Auth/Login");
        using (var login = await Post(clinicClient, "/Auth/Login", clinicLogin, new() { ["email"] = user.Email!, ["password"] = OldPassword })) Assert.Equal(System.Net.HttpStatusCode.Redirect, login.StatusCode);
        using (var clinicPage = await clinicClient.GetAsync("/Finance")) Assert.Equal(System.Net.HttpStatusCode.OK, clinicPage.StatusCode);
        var securityPage = await dealerClient.GetStringAsync($"/Dealer/UserAccess/{clinic.ID}?userId={user.Id}"); Assert.Contains("TemporaryPassword", securityPage);
        using (var changed = await Post(dealerClient, manageUrl, securityPage, new() { ["Operation"]="TemporaryPassword", ["NewPassword"]=NewPassword, ["DealerPassword"]=DealerPassword, ["Reason"]="HTTP parola destek talebi" })) Assert.Equal(System.Net.HttpStatusCode.Redirect, changed.StatusCode);
        using (var stale = await clinicClient.GetAsync("/Finance")) Assert.Contains("/Auth/Login", stale.Headers.Location!.ToString());
        clinicLogin = await clinicClient.GetStringAsync("/Auth/Login");
        using (var login = await Post(clinicClient, "/Auth/Login", clinicLogin, new() { ["email"] = user.Email!, ["password"] = NewPassword })) Assert.Equal("/Auth/ChangePassword", login.Headers.Location!.ToString());
        using (var bypass = await clinicClient.GetAsync("/Finance")) Assert.Equal("/Auth/ChangePassword", bypass.Headers.Location!.ToString());
        using (var profile = await dealerClient.GetAsync($"/Dealer/UserAccess/{other.ID}?userId={user.Id}")) Assert.Equal(System.Net.HttpStatusCode.NotFound, profile.StatusCode);
    }
}
