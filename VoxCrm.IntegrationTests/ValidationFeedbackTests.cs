using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using VoxCrm.Domain.Entities;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ValidationFeedbackTests(PostgresDatabaseFixture database)
{
    private const string Password = "FeedbackTest123!";

    private static string Field(string html, string name) =>
        WebUtility.HtmlDecode(Regex.Match(html, "name=\"" + name + "\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private async Task<(WebApplicationFactoryForTests Factory, HttpClient Client, Clinic Clinic)> SignInAsync(string name)
    {
        Clinic clinic;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, _) = await TestData.CreateClinicWithOwnerAsync(db, name);
        }
        var factory = new WebApplicationFactoryForTests(database.ConnectionString);
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"feedback-{Guid.NewGuid():N}@example.test";
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, ClinicId = clinic.ID, LockoutEnabled = true };
            Assert.True((await manager.CreateAsync(user, Password)).Succeeded);
            Assert.True((await manager.AddToRoleAsync(user, "Clinic")).Succeeded);
        }
        var loginPage = await client.GetStringAsync("/Auth/Login");
        using var login = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Field(loginPage, "__RequestVerificationToken"),
            ["email"] = email,
            ["password"] = Password,
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return (factory, client, clinic);
    }

    private static async Task<string> PostOwnerAsync(HttpClient client, string first, string last, string phone)
    {
        var page = await client.GetStringAsync("/PetOwner/Create");
        using var response = await client.PostAsync("/PetOwner/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Field(page, "__RequestVerificationToken"),
            ["SubmissionId"] = Guid.NewGuid().ToString(),
            ["FirstName"] = first,
            ["LastName"] = last,
            ["Phone"] = phone,
        }));
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task A_rejected_save_tells_the_user_why_and_returns_what_they_typed()
    {
        var (factory, client, _) = await SignInAsync("Feedback Clinic");
        await using var webFactory = factory;
        using var webClient = client;

        var first = await PostOwnerAsync(client, "Ilk", "Musteri", "0500 111 22 33");
        Assert.DoesNotContain("attempted-values", first); // the first save succeeded (redirect), no form feedback needed

        var duplicate = await PostOwnerAsync(client, "Ikinci", "Musteri", "0500 111 22 33");

        // The reason is shown as an error toast and the submitted values are handed back to the page.
        Assert.Contains("zaten kay", WebUtility.HtmlDecode(duplicate));
        Assert.Contains("text-bg-danger", duplicate);
        var block = Regex.Match(duplicate, "<script type=\"application/json\" id=\"attempted-values\"[^>]*>(.*?)</script>", RegexOptions.Singleline);
        Assert.True(block.Success, "attempted values block missing");
        var json = System.Text.Json.JsonDocument.Parse(block.Groups[1].Value).RootElement;
        Assert.Equal("Ikinci", json.GetProperty("FirstName").GetString());
        Assert.Equal("0500 111 22 33", json.GetProperty("Phone").GetString());
    }

    [Fact]
    public async Task A_failed_sign_in_never_echoes_the_password_back_into_the_page()
    {
        await using var factory = new WebApplicationFactoryForTests(database.ConnectionString);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var loginPage = await client.GetStringAsync("/Auth/Login");

        using var response = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Field(loginPage, "__RequestVerificationToken"),
            ["email"] = "nobody@example.test",
            ["password"] = "SuperSecretWrong123!",
        }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("SuperSecretWrong123!", html);
    }
}
