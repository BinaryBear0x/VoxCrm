using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using VoxCrm.Application.PetOwners;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Audit;
using VoxCrm.Infrastructure.PetOwners;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ArchiveAndSearchTests(PostgresDatabaseFixture database)
{
    private const string Password = "ArchiveTest123!";

    private sealed class FixedTenant(Guid clinicId) : ITenantService
    {
        public Guid GetClinicId() => clinicId;
        public bool IsSystemContext => false;
    }

    private static string Field(string html, string name) =>
        WebUtility.HtmlDecode(Regex.Match(html, "name=\"" + name + "\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    [Fact]
    public async Task Searching_a_customer_by_full_name_finds_them()
    {
        Guid clinicId;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            clinicId = (await TestData.CreateClinicWithOwnerAsync(db, "Search Clinic " + Guid.NewGuid().ToString("N"))).Clinic.ID;
        }

        await using (var db = database.CreateDbContext())
        {
            var service = new PetOwnerService(db, new FixedTenant(clinicId), new DbAuditLogger(db));
            Assert.True((await service.CreateAsync(new PetOwner { FirstName = "Zeynep", LastName = "Yilmaz", Phone = "0500 111 22 33" })).Succeeded);
        }

        await using (var db = database.CreateDbContext())
        {
            var service = new PetOwnerService(db, new FixedTenant(clinicId), new DbAuditLogger(db));
            Assert.Single(await service.ListAsync("zeynep yilmaz"));
            Assert.Single(await service.ListAsync("Zeynep"));
            Assert.Empty(await service.ListAsync("zeynep demir"));
        }
    }

    [Fact]
    public async Task An_archived_customer_can_be_listed_and_restored_from_the_web_ui()
    {
        Clinic clinic;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, _) = await TestData.CreateClinicWithOwnerAsync(db, "Archive Clinic " + Guid.NewGuid().ToString("N"));
        }
        PetOwner owner;
        await using (var db = database.CreateDbContext())
        {
            var created = await new PetOwnerService(db, new FixedTenant(clinic.ID), new DbAuditLogger(db))
                .CreateAsync(new PetOwner { FirstName = "Arsiv", LastName = "Musteri", Phone = "0500 222 33 44" });
            Assert.True(created.Succeeded);
            owner = created.Owner!;
        }

        await using var factory = new WebApplicationFactoryForTests(database.ConnectionString);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"archive-{Guid.NewGuid():N}@example.test";
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

        // Archive through the service, then it must be visible only in the archived list.
        await using (var db = database.CreateDbContext())
            Assert.True((await new PetOwnerService(db, new FixedTenant(clinic.ID), new DbAuditLogger(db)).ArchiveAsync(owner.ID, Guid.NewGuid())).Succeeded);

        Assert.DoesNotContain("Arsiv", await client.GetStringAsync("/PetOwner"));
        var archivedPage = await client.GetStringAsync("/PetOwner?archived=true");
        Assert.Contains("Arsiv", archivedPage);
        Assert.Contains("/PetOwner/Restore/" + owner.ID, archivedPage);

        using var restore = await client.PostAsync("/PetOwner/Restore/" + owner.ID, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Field(archivedPage, "__RequestVerificationToken"),
        }));
        Assert.Equal(HttpStatusCode.Redirect, restore.StatusCode);
        Assert.Contains("Arsiv", await client.GetStringAsync("/PetOwner"));
    }
}
