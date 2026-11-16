extern alias VoxCrmWeb;

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.WhatsApp;
using VoxCrm.IntegrationTests.Infrastructure;
using VoxCrmWeb::VoxCrm.Web.Services;

namespace VoxCrm.IntegrationTests;

public sealed class WhatsAppGatewayClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    private static WhatsAppGatewayClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WhatsAppGateway:BaseUrl"] = "http://gateway.test",
            ["WhatsAppGateway:JwtSecret"] = TestJwt.Secret,
        }).Build();
        return new WhatsAppGatewayClient(new HttpClient(new StubHandler(respond)), configuration);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_rejected_or_failing_gateway_is_an_error_not_an_empty_answer(HttpStatusCode status)
    {
        var client = Client(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("secret internal detail 10.0.0.5") }));
        var clinicId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStatusAsync(clinicId));
        Assert.Equal(status, ex.StatusCode);
        Assert.DoesNotContain("10.0.0.5", ex.Message);

        var connect = await Assert.ThrowsAsync<HttpRequestException>(() => client.ConnectAsync(clinicId));
        Assert.DoesNotContain("10.0.0.5", connect.Message);
    }

    [Fact]
    public async Task A_healthy_gateway_still_returns_the_session_status()
    {
        var clinicId = Guid.NewGuid();
        var client = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"clinicId":"{{clinicId}}","status":"ready","connectedPhone":"905551112233"}""", System.Text.Encoding.UTF8, "application/json")
        }));

        var status = await client.GetStatusAsync(clinicId);

        Assert.Equal("ready", status!.Status);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class WhatsAppWebTests(PostgresDatabaseFixture database)
{
    private const string Password = "WebWaTest123!";

    private static string Field(string html, string name) =>
        WebUtility.HtmlDecode(Regex.Match(html, "name=\"" + name + "\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private async Task<(WebApplicationFactoryForTests Factory, HttpClient Client)> SignInAsync(Clinic clinic, string role)
    {
        var factory = new WebApplicationFactoryForTests(database.ConnectionString);
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, LockoutEnabled = true };
            if (role == "Clinic") user.ClinicId = clinic.ID; else user.DealerId = clinic.DealerId;
            Assert.True((await manager.CreateAsync(user, Password)).Succeeded);
            Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        }
        var loginPage = await client.GetStringAsync("/Auth/Login");
        using var login = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Field(loginPage, "__RequestVerificationToken"),
            ["email"] = email,
            ["password"] = Password,
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        if (role == "Dealer")
        {
            // Dealers must enroll in MFA before any page opens. The enrollment flow itself is not
            // under test here, so mark it done after the (cookie-issuing) password login. A plain
            // column update keeps the security stamp, which would otherwise invalidate the cookie.
            await using var db = database.CreateDbContext();
            Assert.Equal(1, await db.Users.Where(u => u.Email == email).ExecuteUpdateAsync(s => s.SetProperty(u => u.TwoFactorEnabled, true)));
        }
        return (factory, client);
    }

    private static string Trim(string body) => body.Length > 1500 ? body[..1500] : body;

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string antiforgerySource, Dictionary<string, string> form)
    {
        using var pageResponse = await client.GetAsync(antiforgerySource);
        Assert.True(pageResponse.StatusCode == HttpStatusCode.OK,
            $"{antiforgerySource} answered {(int)pageResponse.StatusCode} -> {pageResponse.Headers.Location} {Trim(await pageResponse.Content.ReadAsStringAsync())}");
        var page = await pageResponse.Content.ReadAsStringAsync();
        form["__RequestVerificationToken"] = Field(page, "__RequestVerificationToken");
        return await client.PostAsync(url, new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task A_clinic_cannot_switch_off_the_dealers_module_permission_and_lock_itself_out()
    {
        Clinic clinic;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, _) = await TestData.CreateClinicWithOwnerAsync(db, "Web Lockout");
        }
        var (factory, client) = await SignInAsync(clinic, "Clinic");
        await using var webFactory = factory;
        using var webClient = client;

        var page = await client.GetStringAsync("/WhatsApp");
        Assert.DoesNotContain("name=\"enabled\"", page);

        // A crafted or stale form that still sends enabled=false must not change the permission.
        using var save = await PostAsync(client, "/WhatsApp/SaveSendingSettings", "/WhatsApp", new()
        {
            ["clinicId"] = clinic.ID.ToString(),
            ["automatic"] = "true",
            ["manual"] = "true",
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        await using (var verify = database.CreateDbContext())
        {
            var stored = await verify.Clinics.AsNoTracking().SingleAsync();
            Assert.True(stored.IsWhatsAppEnabled);
            Assert.True(stored.AutomaticVaccinationRemindersEnabled);
            Assert.True(stored.ManualWhatsAppEnabled);
        }
        using var reopened = await client.GetAsync("/WhatsApp");
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
    }

    [Fact]
    public async Task A_dealer_can_still_switch_the_module_off_and_that_cancels_the_queue()
    {
        Clinic clinic;
        PetOwner owner;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Web Dealer Switch");
            db.Add(new WhatsAppNotification
            {
                ClinicID = clinic.ID, PetOwnerId = owner.ID, PhoneNumber = owner.Phone!,
                RecipientSnapshot = WhatsAppRecipientGuard.Snapshot(owner, null), MessageContent = "Merhaba",
                NotificationType = WhatsAppNotificationTypes.ManualMessage, Status = WhatsAppNotificationStatuses.Pending,
            });
            await db.SaveChangesAsync();
        }
        var (factory, client) = await SignInAsync(clinic, "Dealer");
        await using var webFactory = factory;
        using var webClient = client;

        using var save = await PostAsync(client, "/WhatsApp/SaveSendingSettings", $"/WhatsApp?clinicId={clinic.ID}", new()
        {
            ["clinicId"] = clinic.ID.ToString(),
            ["automatic"] = "true",
            ["manual"] = "true",
        });
        Assert.True(save.StatusCode == HttpStatusCode.Redirect, Trim(await save.Content.ReadAsStringAsync()));

        await using var verify = database.CreateDbContext();
        Assert.False((await verify.Clinics.AsNoTracking().SingleAsync()).IsWhatsAppEnabled);
        Assert.Equal(WhatsAppNotificationStatuses.Cancelled, (await verify.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task A_dealer_can_cancel_a_queued_message_of_its_clinic()
    {
        Clinic clinic;
        WhatsAppNotification queued;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            PetOwner owner;
            (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Web Dealer Cancel");
            queued = new WhatsAppNotification
            {
                ClinicID = clinic.ID, PetOwnerId = owner.ID, PhoneNumber = owner.Phone!,
                RecipientSnapshot = WhatsAppRecipientGuard.Snapshot(owner, null), MessageContent = "Merhaba",
                NotificationType = WhatsAppNotificationTypes.ManualMessage, Status = WhatsAppNotificationStatuses.Pending,
            };
            db.Add(queued);
            await db.SaveChangesAsync();
        }
        var (factory, client) = await SignInAsync(clinic, "Dealer");
        await using var webFactory = factory;
        using var webClient = client;

        using var cancel = await PostAsync(client, "/WhatsApp/ManageNotification", $"/WhatsApp?clinicId={clinic.ID}", new()
        {
            ["clinicId"] = clinic.ID.ToString(),
            ["id"] = queued.ID.ToString(),
            ["operation"] = "cancel",
        });

        Assert.True(cancel.StatusCode == HttpStatusCode.Redirect, Trim(await cancel.Content.ReadAsStringAsync()));
        await using var verify = database.CreateDbContext();
        Assert.Equal(WhatsAppNotificationStatuses.Cancelled, (await verify.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task An_unreachable_gateway_gives_a_clear_page_and_a_generic_json_error()
    {
        Clinic clinic;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, _) = await TestData.CreateClinicWithOwnerAsync(db, "Web Gateway Down");
        }
        var (factory, client) = await SignInAsync(clinic, "Clinic");
        await using var webFactory = factory;
        using var webClient = client;

        using var page = await client.GetAsync("/WhatsApp");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("WhatsApp servisine erişilemiyor", await page.Content.ReadAsStringAsync());

        using var qr = await client.GetAsync($"/WhatsApp/QrStatus?clinicId={clinic.ID}");
        Assert.Equal(HttpStatusCode.BadGateway, qr.StatusCode);
        var body = await qr.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\":false", body);
        Assert.DoesNotContain("127.0.0.1", body);
    }

    private async Task<(Clinic Clinic, PetOwner Owner)> SeedClinicAsync(string name)
    {
        await using var db = database.CreateDbContext();
        await TestData.ClearWhatsAppDataAsync(db);
        return await TestData.CreateClinicWithOwnerAsync(db, name);
    }

    private static WhatsAppNotification StoppedMessage(Clinic clinic, PetOwner owner, string snapshot) => new()
    {
        ClinicID = clinic.ID, PetOwnerId = owner.ID, PhoneNumber = owner.Phone!, MessageContent = "Merhaba, kontrol zamanı.",
        NotificationType = WhatsAppNotificationTypes.ManualMessage, Status = WhatsAppNotificationStatuses.NeedsReview,
        RecipientSnapshot = snapshot, LastError = "Alıcı bilgileri değişti. Gönderim durduruldu.",
    };

    [Fact]
    public async Task A_clinic_can_edit_the_reminder_template_and_typos_are_flagged()
    {
        var (clinic, _) = await SeedClinicAsync("Web Template Clinic");
        var (factory, client) = await SignInAsync(clinic, "Clinic");
        await using var webFactory = factory;
        using var webClient = client;

        var page = await client.GetStringAsync("/WhatsApp");
        Assert.Contains("name=\"body\"", page);
        Assert.Contains("Örnek (kayıtlı şablonla)", page);

        using var first = await PostAsync(client, "/WhatsApp/SaveTemplate", "/WhatsApp", new()
        {
            ["clinicId"] = clinic.ID.ToString(),
            ["body"] = "Merhaba {ownerFirstName}, {patientName} için {vaccineName} {dueDate} tarihinde. {tpyo}",
        });
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        using var second = await PostAsync(client, "/WhatsApp/SaveTemplate", "/WhatsApp", new()
        {
            ["clinicId"] = clinic.ID.ToString(),
            ["body"] = "Selam {ownerFirstName}, {patientName} için {vaccineName} zamanı {dueDate}.",
        });
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);

        await using (var verify = database.CreateDbContext())
        {
            var template = await verify.WhatsAppTemplates.IgnoreQueryFilters().AsNoTracking().SingleAsync();
            Assert.Equal(clinic.ID, template.ClinicID);
            Assert.StartsWith("Selam {ownerFirstName}", template.Body);
        }

        var reopened = await client.GetStringAsync("/WhatsApp");
        Assert.Contains("Selam {ownerFirstName}", WebUtility.HtmlDecode(reopened));
        Assert.Contains("Selam Ayşe, Pamuk için Kuduz zamanı", WebUtility.HtmlDecode(reopened));
    }

    [Fact]
    public async Task Template_input_is_validated_and_a_typo_warning_is_shown()
    {
        var (clinic, _) = await SeedClinicAsync("Web Template Validation");
        var (factory, client) = await SignInAsync(clinic, "Clinic");
        await using var webFactory = factory;
        using var webClient = client;

        using var tooLong = await PostAsync(client, "/WhatsApp/SaveTemplate", "/WhatsApp", new() { ["clinicId"] = clinic.ID.ToString(), ["body"] = new string('x', 1001) });
        using var blank = await PostAsync(client, "/WhatsApp/SaveTemplate", "/WhatsApp", new() { ["clinicId"] = clinic.ID.ToString(), ["body"] = "   " });
        await using (var verify = database.CreateDbContext())
            Assert.Empty(await verify.WhatsAppTemplates.IgnoreQueryFilters().ToListAsync());

        using var typo = await PostAsync(client, "/WhatsApp/SaveTemplate", "/WhatsApp", new() { ["clinicId"] = clinic.ID.ToString(), ["body"] = "Merhaba {ownerFirsName}" });
        Assert.Equal(HttpStatusCode.Redirect, typo.StatusCode);
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/WhatsApp"));
        Assert.Contains("tanınmayan alanlar", page);
        Assert.Contains("{ownerFirsName}", page);
    }

    [Fact]
    public async Task A_dealer_can_save_the_template_of_its_clinic()
    {
        var (clinic, _) = await SeedClinicAsync("Web Dealer Template");
        var (factory, client) = await SignInAsync(clinic, "Dealer");
        await using var webFactory = factory;
        using var webClient = client;

        using var save = await PostAsync(client, "/WhatsApp/SaveTemplate", $"/WhatsApp?clinicId={clinic.ID}", new()
        {
            ["clinicId"] = clinic.ID.ToString(),
            ["body"] = "Bayi şablonu {patientName}",
        });

        Assert.True(save.StatusCode == HttpStatusCode.Redirect, Trim(await save.Content.ReadAsStringAsync()));
        await using var verify = database.CreateDbContext();
        Assert.Equal("Bayi şablonu {patientName}", (await verify.WhatsAppTemplates.IgnoreQueryFilters().AsNoTracking().SingleAsync()).Body);
    }

    [Fact]
    public async Task A_stopped_message_can_be_reviewed_and_released_after_the_recipient_is_confirmed()
    {
        var (clinic, owner) = await SeedClinicAsync("Web Review Release");
        WhatsAppNotification stopped;
        await using (var db = database.CreateDbContext())
        {
            stopped = StoppedMessage(clinic, owner, "STALE-SNAPSHOT");
            db.Add(stopped);
            await db.SaveChangesAsync();
        }
        var (factory, client) = await SignInAsync(clinic, "Clinic");
        await using var webFactory = factory;
        using var webClient = client;

        var review = WebUtility.HtmlDecode(await client.GetStringAsync($"/WhatsApp/Preview?clinicId={clinic.ID}&id={stopped.ID}"));
        Assert.Contains("name=\"acknowledged\"", review);
        Assert.Contains("yeniden sıraya al", review);

        // Without the explicit confirmation nothing is released.
        using var unconfirmed = await PostAsync(client, "/WhatsApp/ManageNotification", $"/WhatsApp/Preview?clinicId={clinic.ID}&id={stopped.ID}", new()
        {
            ["clinicId"] = clinic.ID.ToString(), ["id"] = stopped.ID.ToString(), ["operation"] = "requeue",
        });
        Assert.Equal(HttpStatusCode.Redirect, unconfirmed.StatusCode);
        await using (var verify = database.CreateDbContext())
            Assert.Equal(WhatsAppNotificationStatuses.NeedsReview, (await verify.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync()).Status);

        using var released = await PostAsync(client, "/WhatsApp/ManageNotification", $"/WhatsApp/Preview?clinicId={clinic.ID}&id={stopped.ID}", new()
        {
            ["clinicId"] = clinic.ID.ToString(), ["id"] = stopped.ID.ToString(), ["operation"] = "requeue", ["acknowledged"] = "true",
        });
        Assert.Equal(HttpStatusCode.Redirect, released.StatusCode);

        await using var after = database.CreateDbContext();
        var stored = await after.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.Equal(WhatsAppNotificationStatuses.Pending, stored.Status);
        Assert.NotEqual("STALE-SNAPSHOT", stored.RecipientSnapshot);
        Assert.Null(stored.LastError);
        Assert.Equal(0, stored.RetryCount);
        Assert.NotNull(stored.NextAttemptAt);
        // The refreshed snapshot is exactly what the final delivery check compares with.
        Assert.Null(await new WhatsAppRecipientGuard(after).CheckAsync(stored));
    }

    [Fact]
    public async Task A_stopped_message_cannot_be_released_while_the_customer_has_no_consent()
    {
        var (clinic, owner) = await SeedClinicAsync("Web Review Blocked");
        WhatsAppNotification stopped;
        await using (var db = database.CreateDbContext())
        {
            stopped = StoppedMessage(clinic, owner, "STALE-SNAPSHOT");
            db.Add(stopped);
            await db.SaveChangesAsync();
            await db.PetOwners.IgnoreQueryFilters().Where(o => o.ID == owner.ID).ExecuteUpdateAsync(s => s.SetProperty(o => o.WhatsAppConsent, false));
        }
        var (factory, client) = await SignInAsync(clinic, "Clinic");
        await using var webFactory = factory;
        using var webClient = client;

        var review = WebUtility.HtmlDecode(await client.GetStringAsync($"/WhatsApp/Preview?clinicId={clinic.ID}&id={stopped.ID}"));
        Assert.DoesNotContain("name=\"acknowledged\"", review);
        Assert.Contains("WhatsApp izni yok", review);

        // A hand-made request is refused by the server as well.
        using var forced = await PostAsync(client, "/WhatsApp/ManageNotification", "/WhatsApp", new()
        {
            ["clinicId"] = clinic.ID.ToString(), ["id"] = stopped.ID.ToString(), ["operation"] = "requeue", ["acknowledged"] = "true",
        });
        Assert.Equal(HttpStatusCode.Redirect, forced.StatusCode);
        await using var verify = database.CreateDbContext();
        Assert.Equal(WhatsAppNotificationStatuses.NeedsReview, (await verify.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData("Clinic")]
    [InlineData("Dealer")]
    public async Task A_stopped_message_can_be_cancelled(string role)
    {
        var (clinic, owner) = await SeedClinicAsync("Web Review Cancel " + role);
        WhatsAppNotification stopped;
        await using (var db = database.CreateDbContext())
        {
            stopped = StoppedMessage(clinic, owner, "STALE-SNAPSHOT");
            db.Add(stopped);
            await db.SaveChangesAsync();
        }
        var (factory, client) = await SignInAsync(clinic, role);
        await using var webFactory = factory;
        using var webClient = client;

        using var cancel = await PostAsync(client, "/WhatsApp/ManageNotification", $"/WhatsApp?clinicId={clinic.ID}", new()
        {
            ["clinicId"] = clinic.ID.ToString(), ["id"] = stopped.ID.ToString(), ["operation"] = "cancel",
        });

        Assert.True(cancel.StatusCode == HttpStatusCode.Redirect, Trim(await cancel.Content.ReadAsStringAsync()));
        await using var verify = database.CreateDbContext();
        Assert.Equal(WhatsAppNotificationStatuses.Cancelled, (await verify.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task A_message_that_may_already_have_been_sent_is_never_released()
    {
        var (clinic, owner) = await SeedClinicAsync("Web Review Sent");
        WhatsAppNotification stopped;
        await using (var db = database.CreateDbContext())
        {
            stopped = StoppedMessage(clinic, owner, "STALE-SNAPSHOT");
            stopped.GatewayMessageId = "wa-already";
            db.Add(stopped);
            await db.SaveChangesAsync();
        }
        var (factory, client) = await SignInAsync(clinic, "Clinic");
        await using var webFactory = factory;
        using var webClient = client;

        var review = await client.GetStringAsync($"/WhatsApp/Preview?clinicId={clinic.ID}&id={stopped.ID}");
        Assert.DoesNotContain("name=\"acknowledged\"", review);

        using var forced = await PostAsync(client, "/WhatsApp/ManageNotification", "/WhatsApp", new()
        {
            ["clinicId"] = clinic.ID.ToString(), ["id"] = stopped.ID.ToString(), ["operation"] = "requeue", ["acknowledged"] = "true",
        });
        Assert.Equal(HttpStatusCode.Redirect, forced.StatusCode);
        await using var verify = database.CreateDbContext();
        Assert.Equal(WhatsAppNotificationStatuses.NeedsReview, (await verify.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync()).Status);
    }
}
