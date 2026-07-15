using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Security.Cryptography;
using System.Text.Json;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.IntegrationTests.Infrastructure;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Security;
using VoxCrm.Infrastructure.Jobs;

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
    public async Task Tenant_query_filters_cover_every_tenant_entity_and_fail_closed_without_clinic()
    {
        await using var systemContext = _database.CreateDbContext();
        var tenantEntities = systemContext.Model.GetEntityTypes()
            .Where(entityType => typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            .ToList();

        Assert.NotEmpty(tenantEntities);
        Assert.All(tenantEntities, entityType => Assert.NotEmpty(entityType.GetDeclaredQueryFilters()));

        await using var emptyTenantContext = CreateTenantContext(Guid.Empty);
        Assert.Equal(0, await emptyTenantContext.PetOwners.CountAsync());
        Assert.Equal(0, await emptyTenantContext.VaccineTypes.CountAsync());
        Assert.Equal(0, await emptyTenantContext.VaccinationRecords.CountAsync());
    }

    [Fact]
    public async Task Vaccination_data_and_mutations_are_scoped_to_current_clinic()
    {
        Guid clinicA;
        Guid clinicB;
        Guid clinicBVaccineType;
        Guid clinicBRecord;

        await using (var db = _database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            var first = await TestData.CreateClinicWithOwnerAsync(db, "Vaccination Clinic A");
            var second = await TestData.CreateClinicWithOwnerAsync(db, "Vaccination Clinic B");
            clinicA = first.Clinic.ID;
            clinicB = second.Clinic.ID;

            var patientA = new Patient { ClinicID = clinicA, Name = "A Patient" };
            var patientB = new Patient { ClinicID = clinicB, Name = "B Patient" };
            var vaccineA = new VaccineType { ClinicID = clinicA, Name = "A Vaccine", ValidityDays = 365 };
            var vaccineB = new VaccineType { ClinicID = clinicB, Name = "B Vaccine", ValidityDays = 365 };
            var recordA = new VaccinationRecord
            {
                ClinicID = clinicA,
                Patient = patientA,
                VaccineType = vaccineA,
                AdministeredDate = DateTime.UtcNow,
                NextDueDate = DateTime.UtcNow.AddDays(365),
            };
            var recordB = new VaccinationRecord
            {
                ClinicID = clinicB,
                Patient = patientB,
                VaccineType = vaccineB,
                AdministeredDate = DateTime.UtcNow,
                NextDueDate = DateTime.UtcNow.AddDays(365),
            };
            db.AddRange(recordA, recordB);
            await db.SaveChangesAsync();
            clinicBVaccineType = vaccineB.ID;
            clinicBRecord = recordB.ID;
        }

        await using var clinicAContext = CreateTenantContext(clinicA);
        Assert.All(await clinicAContext.VaccineTypes.ToListAsync(), item => Assert.Equal(clinicA, item.ClinicID));
        Assert.All(await clinicAContext.VaccinationRecords.ToListAsync(), item => Assert.Equal(clinicA, item.ClinicID));
        Assert.Null(await clinicAContext.VaccineTypes.FindAsync(clinicBVaccineType));
        Assert.Null(await clinicAContext.VaccinationRecords.FindAsync(clinicBRecord));

        var foreignRecord = await clinicAContext.VaccinationRecords
            .IgnoreQueryFilters()
            .SingleAsync(record => record.ID == clinicBRecord);
        foreignRecord.NextDueDate = foreignRecord.NextDueDate.AddDays(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => clinicAContext.SaveChangesAsync());
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
        var csp = string.Join(" ", response.Headers.GetValues("Content-Security-Policy"));
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("script-src 'self' 'nonce-", csp, StringComparison.Ordinal);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(" onclick=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" onchange=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" onsubmit=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DENY", response.Headers.GetValues("X-Frame-Options"));
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Contains("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy"));
        Assert.Contains("camera=()", string.Join(",", response.Headers.GetValues("Permissions-Policy")));
    }

    [Fact]
    public async Task Login_post_without_antiforgery_token_is_rejected()
    {
        await using var factory = new WebApplicationFactoryForTests(_database.ConnectionString);
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "attacker@example.test",
            ["password"] = "Wrong!Password1",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authentication_endpoint_is_rate_limited_per_client()
    {
        await using var factory = new WebApplicationFactoryForTests(_database.ConnectionString);
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        HttpResponseMessage? lastResponse = null;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            lastResponse?.Dispose();
            lastResponse = await client.GetAsync("/Auth/Login");
        }

        using (lastResponse)
            Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse!.StatusCode);
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

    [Fact]
    public async Task Sensitive_owner_fields_are_ciphertext_at_rest_and_search_indexes_are_tenant_bound()
    {
        var keyFile = Path.Combine(Path.GetTempPath(), $"voxcrm-pii-{Guid.NewGuid():N}.key");
        await File.WriteAllTextAsync(keyFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PiiEncryption:KeyFile"] = keyFile,
            }).Build();
            var protector = new AesGcmPiiProtector(configuration);
            var interceptor = new PiiEncryptionInterceptor(protector);
            var options = new DbContextOptionsBuilder<VoxCrmDbContext>()
                .UseNpgsql(_database.ConnectionString, builder => builder.MigrationsAssembly("VoxCrm.Infrastructure"))
                .AddInterceptors(interceptor)
                .Options;

            Guid ownerId;
            Guid clinicId;
            await using (var seed = _database.CreateDbContext())
            {
                var data = await TestData.CreateClinicWithOwnerAsync(seed, $"Encrypted clinic {Guid.NewGuid():N}");
                clinicId = data.Clinic.ID;
            }
            await using (var encryptedContext = new VoxCrmDbContext(options))
            {
                var owner = new PetOwner
                {
                    ClinicID = clinicId,
                    FirstName = "Az Bilgili",
                    Phone = "+90 555 123 45 67",
                    Email = "limited@example.test",
                    Address = "Gizli adres",
                    Notes = "Sahipsiz hayvanı getiren kişi",
                };
                encryptedContext.PetOwners.Add(owner);
                await encryptedContext.SaveChangesAsync();
                ownerId = owner.ID;
                Assert.Equal("+90 555 123 45 67", owner.Phone);
            }

            await using var connection = new NpgsqlConnection(_database.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT \"Phone\", \"Email\", \"Address\", \"Notes\", \"NormalizedPhone\", \"EmailLookupHash\" FROM \"PetOwners\" WHERE \"ID\" = @id";
            command.Parameters.AddWithValue("id", ownerId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.StartsWith("enc:v1:", reader.GetString(0));
            Assert.StartsWith("enc:v1:", reader.GetString(1));
            Assert.StartsWith("enc:v1:", reader.GetString(2));
            Assert.StartsWith("enc:v1:", reader.GetString(3));
            Assert.Equal(protector.BlindIndex(clinicId, "905551234567"), reader.GetString(4));
            Assert.Equal(protector.BlindIndex(clinicId, "limited@example.test"), reader.GetString(5));
            Assert.NotEqual(protector.BlindIndex(Guid.NewGuid(), "905551234567"), reader.GetString(4));
        }
        finally
        {
            File.Delete(keyFile);
        }
    }

    [Fact]
    public async Task Retention_removes_expired_message_content_and_audit_but_keeps_recent_content()
    {
        var marker = Guid.NewGuid().ToString("N");
        Guid oldInboundId;
        Guid recentInboundId;
        Guid oldNotificationId;
        await using (var db = _database.CreateDbContext())
        {
            var data = await TestData.CreateClinicWithOwnerAsync(db, $"Retention {marker}");
            var oldInbound = new WhatsAppInboundMessage
            {
                ClinicID = data.Clinic.ID, FromPhone = "+905550000001", Message = "old secret",
                ReceivedAt = DateTime.UtcNow.AddDays(-31), GatewaySessionId = marker,
                ProviderMessageId = $"old-{marker}"
            };
            var recentInbound = new WhatsAppInboundMessage
            {
                ClinicID = data.Clinic.ID, FromPhone = "+905550000002", Message = "recent secret",
                ReceivedAt = DateTime.UtcNow.AddDays(-1), GatewaySessionId = marker,
                ProviderMessageId = $"recent-{marker}"
            };
            var oldNotification = new WhatsAppNotification
            {
                ClinicID = data.Clinic.ID, PetOwnerId = data.Owner.ID, PhoneNumber = "+905550000003",
                MessageContent = "expired outbound", CreatedAt = DateTime.UtcNow.AddDays(-31)
            };
            db.AddRange(oldInbound, recentInbound, oldNotification,
                new SystemAuditLog { Action = $"old-{marker}", Message = "old audit", CreatedAt = DateTime.UtcNow.AddDays(-366) },
                new SystemAuditLog { Action = $"recent-{marker}", Message = "recent audit", CreatedAt = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
            oldInboundId = oldInbound.ID;
            recentInboundId = recentInbound.ID;
            oldNotificationId = oldNotification.ID;
        }

        await using (var jobContext = _database.CreateDbContext())
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataRetention:WhatsAppMessageDays"] = "30",
                ["DataRetention:AuditDays"] = "365"
            }).Build();
            await new DataRetentionJob(jobContext, configuration).ExecuteAsync();
        }

        await using var verify = _database.CreateDbContext();
        Assert.False(await verify.WhatsAppInboundMessages.IgnoreQueryFilters().AnyAsync(x => x.ID == oldInboundId));
        Assert.True(await verify.WhatsAppInboundMessages.IgnoreQueryFilters().AnyAsync(x => x.ID == recentInboundId));
        var redacted = await verify.WhatsAppNotifications.IgnoreQueryFilters().SingleAsync(x => x.ID == oldNotificationId);
        Assert.Equal("[silindi]", redacted.PhoneNumber);
        Assert.DoesNotContain("expired outbound", redacted.MessageContent);
        Assert.False(await verify.SystemAuditLogs.AnyAsync(x => x.Action == $"old-{marker}"));
        Assert.True(await verify.SystemAuditLogs.AnyAsync(x => x.Action == $"recent-{marker}"));
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
        public bool IsSystemContext => false;
    }
}
