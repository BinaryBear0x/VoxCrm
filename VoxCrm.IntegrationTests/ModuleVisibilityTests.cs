extern alias VoxCrmWeb;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxCrm.Domain.Entities;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ModuleVisibilityTests(PostgresDatabaseFixture database)
{
    private const string Password = "ModuleTest123!";

    private static string Field(string html, string name) =>
        WebUtility.HtmlDecode(Regex.Match(html, "name=\"" + name + "\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private async Task SetModulesAsync(Guid clinicId, int modules)
    {
        await using var db = database.CreateDbContext();
        await db.Clinics.IgnoreQueryFilters().Where(c => c.ID == clinicId).ExecuteUpdateAsync(s => s.SetProperty(c => c.EnabledModules, modules));
    }

    private sealed class FixedTenant(Guid clinicId) : VoxCrm.Domain.Common.ITenantService
    {
        public Guid GetClinicId() => clinicId;
        public bool IsSystemContext => false;
    }

    [Fact]
    public async Task The_dealer_can_switch_tasks_and_closing_on_and_off_without_touching_other_modules()
    {
        Clinic clinic;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, _) = await TestData.CreateClinicWithOwnerAsync(db, "Dealer Switch " + Guid.NewGuid().ToString("N"));
        }

        async Task<int> SaveAsync(bool quick, bool follow, bool workday)
        {
            await using var db = database.CreateDbContext();
            var modules = new VoxCrmWeb::VoxCrm.Web.Services.ClinicModules(db, new FixedTenant(clinic.ID));
            var current = await db.Clinics.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.ID == clinic.ID);
            await modules.SaveAsync(clinic.DealerId, Guid.NewGuid(), clinic.ID, current.ModulesRevision, quick, follow, current.IsWhatsAppEnabled, default, workday);
            return (await db.Clinics.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.ID == clinic.ID)).EnabledModules;
        }

        Assert.Equal(1 | 2, await SaveAsync(true, true, false));
        Assert.Equal(1 | 2 | 4, await SaveAsync(true, true, true));
        Assert.Equal(4, await SaveAsync(false, false, true));
        Assert.Equal(0, await SaveAsync(false, false, false));
    }

    [Fact]
    public async Task Tasks_closing_assistant_and_callbacks_follow_the_dealers_module_switches()
    {
        Clinic clinic;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, _) = await TestData.CreateClinicWithOwnerAsync(db, "Modules Clinic " + Guid.NewGuid().ToString("N"));
        }

        await using var factory = new WebApplicationFactoryForTests(database.ConnectionString);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"modules-{Guid.NewGuid():N}@example.test";
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, ClinicId = clinic.ID, LockoutEnabled = true };
            Assert.True((await manager.CreateAsync(user, Password)).Succeeded);
            Assert.True((await manager.AddToRoleAsync(user, "Clinic")).Succeeded);
        }
        var loginPage = await client.GetStringAsync("/Auth/Login");
        using (var login = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Field(loginPage, "__RequestVerificationToken"),
            ["email"] = email,
            ["password"] = Password,
        })))
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        // Everything on (tasks + closing 4, follow-up 2, quick visit 1).
        await SetModulesAsync(clinic.ID, 7);
        var all = await client.GetStringAsync("/");
        Assert.Contains("Yapılacaklar", all);
        Assert.Contains("Kapanış asistanı", all);
        Assert.Contains("Geri Arama Talepleri", all);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Workday")).StatusCode);

        // Tasks/closing on, follow-up (patient contact) off: callbacks disappear, tasks stay.
        await SetModulesAsync(clinic.ID, 4);
        var tasksOnly = await client.GetStringAsync("/");
        Assert.Contains("Yapılacaklar", tasksOnly);
        Assert.Contains("Kapanış asistanı", tasksOnly);
        Assert.DoesNotContain("Geri Arama Talepleri", tasksOnly);

        // Tasks/closing off: no widget, link or page leads to them, and callbacks (which are handled as tasks) go too.
        await SetModulesAsync(clinic.ID, 2);
        var followOnly = await client.GetStringAsync("/");
        Assert.DoesNotContain("Yapılacaklar", followOnly);
        Assert.DoesNotContain("Kapanış asistanı", followOnly);
        Assert.DoesNotContain("href=\"/Workday", followOnly);
        Assert.DoesNotContain("Geri Arama Talepleri", followOnly);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Workday")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Workday/Task")).StatusCode);
    }
}
