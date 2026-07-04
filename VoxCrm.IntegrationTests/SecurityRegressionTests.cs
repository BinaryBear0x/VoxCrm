using System.Net;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.IntegrationTests.Infrastructure;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class SecurityRegressionTests
{
    private readonly PostgresDatabaseFixture _database;

    public SecurityRegressionTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Tenant_query_filters_keep_whatsapp_data_scoped_to_current_clinic()
    {
        Guid clinicA;
        Guid clinicB;
        await using (var db = _database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            var first = await TestData.CreateClinicWithOwnerAsync(db, "Clinic A");
            var second = await TestData.CreateClinicWithOwnerAsync(db, "Clinic B");
            clinicA = first.Clinic.ID;
            clinicB = second.Clinic.ID;
            await TestData.AddNotificationsAsync(db, clinicA, first.Owner.ID, count: 2);
            await TestData.AddNotificationsAsync(db, clinicB, second.Owner.ID, count: 3);
            db.WhatsAppTemplates.AddRange(
                new WhatsAppTemplate
                {
                    ClinicID = clinicA,
                    NotificationType = WhatsAppNotificationTypes.VaccinationReminder,
                    Body = "A template",
                },
                new WhatsAppTemplate
                {
                    ClinicID = clinicB,
                    NotificationType = WhatsAppNotificationTypes.VaccinationReminder,
                    Body = "B template",
                });
            await db.SaveChangesAsync();
        }

        await using var clinicAContext = CreateTenantContext(clinicA);
        var visibleNotifications = await clinicAContext.WhatsAppNotifications.AsNoTracking().ToListAsync();
        var visibleTemplates = await clinicAContext.WhatsAppTemplates.AsNoTracking().ToListAsync();

        Assert.Equal(2, visibleNotifications.Count);
        Assert.All(visibleNotifications, notification => Assert.Equal(clinicA, notification.ClinicID));
        Assert.Single(visibleTemplates);
        Assert.Equal(clinicA, visibleTemplates.Single().ClinicID);

        var totalNotifications = await clinicAContext.WhatsAppNotifications.IgnoreQueryFilters().CountAsync();
        Assert.Equal(5, totalNotifications);
    }

    [Fact]
    public async Task Web_login_response_contains_security_headers()
    {
        await using var factory = new WebApplicationFactoryForTests(_database.ConnectionString);
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/Auth/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.Contains("DENY", response.Headers.GetValues("X-Frame-Options"));
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Contains("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy"));
    }

    [Fact]
    public void Appointment_calendar_payload_is_json_encoded_not_raw_script()
    {
        const string payload = "</script><script>alert('xss')</script>";
        var events = new[]
        {
            new
            {
                title = $"Karabas - {payload}",
                start = "2026-07-02T09:00:00",
                end = "2026-07-02T09:30:00",
                url = "/Appointment/Edit/00000000-0000-0000-0000-000000000001",
                backgroundColor = "#0d6efd",
                borderColor = "#0d6efd",
            },
        };

        var json = JsonSerializer.Serialize(events);

        Assert.DoesNotContain("</script><script>", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\u003C/script\\u003E", json);
    }

    private VoxCrmDbContext CreateTenantContext(Guid clinicId)
    {
        var options = new DbContextOptionsBuilder<VoxCrmDbContext>()
            .UseNpgsql(_database.ConnectionString, builder => builder.MigrationsAssembly("VoxCrm.Infrastructure"))
            .Options;

        return new VoxCrmDbContext(options, new FixedTenantService(clinicId));
    }

    private sealed class FixedTenantService : ITenantService
    {
        private readonly Guid _clinicId;

        public FixedTenantService(Guid clinicId)
        {
            _clinicId = clinicId;
        }

        public Guid GetClinicId() => _clinicId;
    }
}
