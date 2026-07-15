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
public sealed class ClinicLifecycleIntegrationTests
{
    private readonly PostgresDatabaseFixture _database;

    public ClinicLifecycleIntegrationTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Dealer_cannot_read_update_or_change_another_dealers_clinic()
    {
        var (dealerA, _, clinicB) = await SeedDealerClinicsAsync();
        await using var services = CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IClinicManagementRepository>();

        var found = await repository.FindOwnedAsync(dealerA.ID, clinicB.ID, CancellationToken.None);
        var update = await repository.UpdateAsync(
            new ClinicUpdate(
                dealerA.ID,
                clinicB.ID,
                "Compromised",
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                null,
                false,
                new TimeOnly(9, 0),
                new TimeOnly(18, 0),
                "Europe/Istanbul"),
            CancellationToken.None);
        var lifecycle = await repository.ChangeLifecycleAsync(
            new ClinicLifecycleChange(dealerA.ID, clinicB.ID, false),
            CancellationToken.None);

        Assert.Null(found);
        Assert.False(update.Succeeded);
        Assert.False(lifecycle.Succeeded);

        await using var verification = _database.CreateDbContext();
        var unchanged = await verification.Clinics.SingleAsync(clinic => clinic.ID == clinicB.ID);
        Assert.NotEqual("Compromised", unchanged.Name);
        Assert.True(unchanged.IsActive);
    }

    [Fact]
    public async Task Activation_token_is_single_use()
    {
        var dealer = await SeedDealerAsync("Activation Dealer");
        await using var services = CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IClinicManagementRepository>();
        var provisioning = CreateProvisioning(dealer, "Activation Clinic", "activation@example.test");

        var created = await repository.CreateAsync(provisioning, CancellationToken.None);
        Assert.True(created.Succeeded);
        Assert.NotNull(created.Provisioned);

        var provisioned = created.Provisioned!;
        var statusBefore = await repository.GetActivationStatusAsync(
            provisioned.UserId,
            provisioned.ActivationToken,
            CancellationToken.None);
        var firstUse = await repository.ActivateAsync(
            provisioned.UserId,
            provisioned.ActivationToken,
            "Clinic123!",
            CancellationToken.None);
        var statusAfter = await repository.GetActivationStatusAsync(
            provisioned.UserId,
            provisioned.ActivationToken,
            CancellationToken.None);
        var secondUse = await repository.ActivateAsync(
            provisioned.UserId,
            provisioned.ActivationToken,
            "Other123!",
            CancellationToken.None);

        Assert.True(statusBefore.IsValid);
        Assert.True(firstUse.Succeeded);
        Assert.False(statusAfter.IsValid);
        Assert.False(secondUse.Succeeded);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(provisioned.UserId.ToString());
        Assert.NotNull(user);
        Assert.True(user!.EmailConfirmed);
        Assert.True(await userManager.CheckPasswordAsync(user, "Clinic123!"));
        Assert.False(await userManager.CheckPasswordAsync(user, "Other123!"));
    }

    [Fact]
    public async Task Deactivate_locks_clinic_user_and_reactivate_unlocks_user()
    {
        var dealer = await SeedDealerAsync("Lifecycle Dealer");
        await using var services = CreateIdentityServices();
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IClinicManagementRepository>();
        var created = await repository.CreateAsync(
            CreateProvisioning(dealer, "Lifecycle Clinic", "lifecycle@example.test"),
            CancellationToken.None);
        Assert.True(created.Succeeded);
        Assert.NotNull(created.Provisioned);

        var provisioned = created.Provisioned!;
        var activated = await repository.ActivateAsync(
            provisioned.UserId,
            provisioned.ActivationToken,
            "Clinic123!",
            CancellationToken.None);
        Assert.True(activated.Succeeded);

        var deactivated = await repository.ChangeLifecycleAsync(
            new ClinicLifecycleChange(dealer.ID, provisioned.ClinicId, false),
            CancellationToken.None);
        Assert.True(deactivated.Succeeded);
        Assert.False(await repository.IsUserScopeActiveAsync(
            provisioned.ClinicId,
            null,
            CancellationToken.None));

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var lockedUser = await userManager.FindByIdAsync(provisioned.UserId.ToString());
        Assert.NotNull(lockedUser);
        Assert.True(lockedUser!.LockoutEnabled);
        Assert.Equal(DateTimeOffset.MaxValue, lockedUser.LockoutEnd);

        var reactivated = await repository.ChangeLifecycleAsync(
            new ClinicLifecycleChange(dealer.ID, provisioned.ClinicId, true),
            CancellationToken.None);
        Assert.True(reactivated.Succeeded);
        Assert.True(await repository.IsUserScopeActiveAsync(
            provisioned.ClinicId,
            null,
            CancellationToken.None));

        var unlockedUser = await userManager.FindByIdAsync(provisioned.UserId.ToString());
        Assert.NotNull(unlockedUser);
        Assert.Null(unlockedUser!.LockoutEnd);
        Assert.Equal(0, unlockedUser.AccessFailedCount);
    }

    private async Task<(Dealer DealerA, Clinic ClinicA, Clinic ClinicB)> SeedDealerClinicsAsync()
    {
        await using var db = _database.CreateDbContext();
        await TestData.ClearWhatsAppDataAsync(db);

        var dealerA = CreateDealer("Dealer A");
        var dealerB = CreateDealer("Dealer B");
        var clinicA = CreateClinic(dealerA, "Dealer A Clinic");
        var clinicB = CreateClinic(dealerB, "Dealer B Clinic");
        db.AddRange(dealerA, dealerB, clinicA, clinicB);
        await db.SaveChangesAsync();
        return (dealerA, clinicA, clinicB);
    }

    private async Task<Dealer> SeedDealerAsync(string name)
    {
        await using var db = _database.CreateDbContext();
        await TestData.ClearWhatsAppDataAsync(db);
        var dealer = CreateDealer(name);
        db.Dealers.Add(dealer);
        await db.SaveChangesAsync();
        return dealer;
    }

    private ServiceProvider CreateIdentityServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<VoxCrmDbContext>(options =>
            options.UseNpgsql(
                _database.ConnectionString,
                builder => builder.MigrationsAssembly("VoxCrm.Infrastructure")));
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<VoxCrmDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<IClinicManagementRepository, ClinicManagementRepository>();
        return services.BuildServiceProvider();
    }

    private static ClinicProvisioning CreateProvisioning(Dealer dealer, string name, string email)
    {
        var clinic = new Clinic
        {
            Name = name,
            Slug = Slug(name),
            DealerId = dealer.ID,
            IsActive = true,
        };
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = "Clinic",
            LastName = "Admin",
            ClinicId = clinic.ID,
            LockoutEnabled = true,
        };
        return new ClinicProvisioning(clinic, user);
    }

    private static Dealer CreateDealer(string name) =>
        new()
        {
            CompanyName = name,
            ContactEmail = $"{Slug(name)}@example.test",
            ContactPhone = "+905550000000",
            IsActive = true,
        };

    private static Clinic CreateClinic(Dealer dealer, string name) =>
        new()
        {
            Name = name,
            Slug = Slug(name),
            DealerId = dealer.ID,
            Dealer = dealer,
            IsActive = true,
        };

    private static string Slug(string value) =>
        value.ToLowerInvariant().Replace(' ', '-');
}
