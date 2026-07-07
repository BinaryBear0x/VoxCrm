using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class WhatsAppApiTests
{
    private readonly PostgresDatabaseFixture _database;

    public WhatsAppApiTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Claim_uses_postgres_locks_and_does_not_return_duplicate_notifications()
    {
        await using (var db = _database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            var (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Concurrency Clinic");
            await TestData.AddNotificationsAsync(db, clinic.ID, owner.ID, count: 20);
        }

        await using var factory = new ApiApplicationFactory(_database.ConnectionString);
        var firstClient = CreateAuthorizedClient(factory, "whatsapp.notifications.claim");
        var secondClient = CreateAuthorizedClient(factory, "whatsapp.notifications.claim");

        await using var readDb = _database.CreateDbContext();
        var clinicId = await readDb.Clinics.Select(c => c.ID).SingleAsync();
        var firstRequest = new
        {
            clinicIds = new[] { clinicId },
            batchSize = 10,
            gatewayId = "gateway-a",
            lockSeconds = 180,
        };
        var secondRequest = new
        {
            clinicIds = new[] { clinicId },
            batchSize = 10,
            gatewayId = "gateway-b",
            lockSeconds = 180,
        };

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/whatsapp/notifications/claim", firstRequest),
            secondClient.PostAsJsonAsync("/api/whatsapp/notifications/claim", secondRequest));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var firstIds = await ReadNotificationIdsAsync(responses[0]);
        var secondIds = await ReadNotificationIdsAsync(responses[1]);

        var allClaimedIds = new HashSet<Guid>(firstIds);
        Assert.True(allClaimedIds.UnionWithNoDuplicates(secondIds));

        while (allClaimedIds.Count < 20)
        {
            var followUpClient = CreateAuthorizedClient(factory, "whatsapp.notifications.claim");
            var followUpResponse = await followUpClient.PostAsJsonAsync(
                "/api/whatsapp/notifications/claim",
                new
                {
                    clinicIds = new[] { clinicId },
                    batchSize = 5,
                    gatewayId = "gateway-follow-up",
                    lockSeconds = 180,
                });
            Assert.Equal(HttpStatusCode.OK, followUpResponse.StatusCode);

            var followUpIds = await ReadNotificationIdsAsync(followUpResponse);
            Assert.NotEmpty(followUpIds);
            Assert.True(allClaimedIds.UnionWithNoDuplicates(followUpIds));
        }

        Assert.Equal(20, allClaimedIds.Count);

        await using var verifyDb = _database.CreateDbContext();
        var notifications = await verifyDb.WhatsAppNotifications.AsNoTracking().ToListAsync();
        Assert.All(notifications, notification =>
        {
            Assert.Equal(WhatsAppNotificationStatuses.Processing, notification.Status);
            Assert.NotNull(notification.LockedAt);
            Assert.NotNull(notification.LockExpiresAt);
            Assert.False(string.IsNullOrWhiteSpace(notification.LockedBy));
        });
    }

    [Fact]
    public async Task RecoverExpiredProcessing_moves_only_expired_locks_to_retry_scheduled()
    {
        WhatsAppNotification expired;
        WhatsAppNotification fresh;
        await using (var db = _database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            var (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Recovery Clinic");
            expired = new WhatsAppNotification
            {
                ClinicID = clinic.ID,
                PetOwnerId = owner.ID,
                PhoneNumber = "+905551111111",
                MessageContent = "Expired lock",
                NotificationType = WhatsAppNotificationTypes.VaccinationReminder,
                Status = WhatsAppNotificationStatuses.Processing,
                LockedAt = DateTime.UtcNow.AddMinutes(-10),
                LockedBy = "gateway-old",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            };
            fresh = new WhatsAppNotification
            {
                ClinicID = clinic.ID,
                PetOwnerId = owner.ID,
                PhoneNumber = "+905552222222",
                MessageContent = "Fresh lock",
                NotificationType = WhatsAppNotificationTypes.VaccinationReminder,
                Status = WhatsAppNotificationStatuses.Processing,
                LockedAt = DateTime.UtcNow,
                LockedBy = "gateway-live",
                LockExpiresAt = DateTime.UtcNow.AddMinutes(5),
            };
            db.WhatsAppNotifications.AddRange(expired, fresh);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiApplicationFactory(_database.ConnectionString);
        var client = CreateAuthorizedClient(factory, "whatsapp.notifications.recover");
        var response = await client.PostAsync("/api/whatsapp/notifications/recover-expired-processing", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verifyDb = _database.CreateDbContext();
        var recovered = await verifyDb.WhatsAppNotifications.FindAsync(expired.ID);
        var unchanged = await verifyDb.WhatsAppNotifications.FindAsync(fresh.ID);

        Assert.NotNull(recovered);
        Assert.Equal(WhatsAppNotificationStatuses.RetryScheduled, recovered.Status);
        Assert.Null(recovered.LockedAt);
        Assert.Null(recovered.LockedBy);
        Assert.Null(recovered.LockExpiresAt);
        Assert.NotNull(recovered.NextAttemptAt);
        Assert.Contains("expired", recovered.LastError, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(unchanged);
        Assert.Equal(WhatsAppNotificationStatuses.Processing, unchanged.Status);
        Assert.Equal("gateway-live", unchanged.LockedBy);
    }

    [Fact]
    public async Task Claim_includes_manual_messages_in_gateway_queue()
    {
        Guid clinicId;
        Guid manualNotificationId;
        await using (var db = _database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            var (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Manual Message Clinic");
            clinicId = clinic.ID;

            var notification = new WhatsAppNotification
            {
                ClinicID = clinic.ID,
                PetOwnerId = owner.ID,
                PhoneNumber = "+905551112233",
                MessageContent = "Manual test message",
                NotificationType = WhatsAppNotificationTypes.ManualMessage,
                Status = WhatsAppNotificationStatuses.Pending,
                NextAttemptAt = DateTime.UtcNow,
            };
            db.WhatsAppNotifications.Add(notification);
            await db.SaveChangesAsync();
            manualNotificationId = notification.ID;
        }

        await using var factory = new ApiApplicationFactory(_database.ConnectionString);
        var client = CreateAuthorizedClient(factory, "whatsapp.notifications.claim");
        var response = await client.PostAsJsonAsync(
            "/api/whatsapp/notifications/claim",
            new
            {
                clinicIds = new[] { clinicId },
                batchSize = 10,
                gatewayId = "gateway-manual",
                lockSeconds = 180,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var claimedIds = await ReadNotificationIdsAsync(response);
        Assert.Contains(manualNotificationId, claimedIds);

        await using var verifyDb = _database.CreateDbContext();
        var claimed = await verifyDb.WhatsAppNotifications.FindAsync(manualNotificationId);
        Assert.NotNull(claimed);
        Assert.Equal(WhatsAppNotificationStatuses.Processing, claimed.Status);
        Assert.Equal("gateway-manual", claimed.LockedBy);
    }

    [Fact]
    public async Task Claim_defers_messages_outside_clinic_send_window()
    {
        Guid clinicId;
        Guid notificationId;
        await using (var db = _database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            var (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Window Clinic");
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));
            var start = TimeOnly.FromDateTime(localNow.AddHours(2));
            var end = TimeOnly.FromDateTime(localNow.AddHours(3));
            clinic.WhatsAppSendWindowEnabled = true;
            clinic.WhatsAppSendWindowStart = start;
            clinic.WhatsAppSendWindowEnd = end;
            clinic.WhatsAppTimeZoneId = "Europe/Istanbul";

            var notification = new WhatsAppNotification
            {
                ClinicID = clinic.ID,
                PetOwnerId = owner.ID,
                PhoneNumber = "+905551112233",
                MessageContent = "Window deferred message",
                NotificationType = WhatsAppNotificationTypes.ManualMessage,
                Status = WhatsAppNotificationStatuses.Pending,
                NextAttemptAt = DateTime.UtcNow.AddMinutes(-1),
            };
            db.WhatsAppNotifications.Add(notification);
            await db.SaveChangesAsync();
            clinicId = clinic.ID;
            notificationId = notification.ID;
        }

        await using var factory = new ApiApplicationFactory(_database.ConnectionString);
        var client = CreateAuthorizedClient(factory, "whatsapp.notifications.claim");
        var response = await client.PostAsJsonAsync(
            "/api/whatsapp/notifications/claim",
            new
            {
                clinicIds = new[] { clinicId },
                batchSize = 10,
                gatewayId = "gateway-window",
                lockSeconds = 180,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var claimedIds = await ReadNotificationIdsAsync(response);
        Assert.Empty(claimedIds);

        await using var verifyDb = _database.CreateDbContext();
        var deferred = await verifyDb.WhatsAppNotifications.FindAsync(notificationId);
        Assert.NotNull(deferred);
        Assert.Equal(WhatsAppNotificationStatuses.Pending, deferred.Status);
        Assert.Null(deferred.LockedBy);
        Assert.NotNull(deferred.NextAttemptAt);
        Assert.True(deferred.NextAttemptAt > DateTime.UtcNow);

        var audit = await verifyDb.SystemAuditLogs
            .AsNoTracking()
            .SingleOrDefaultAsync(l => l.Action == "WhatsAppNotifications.DeferOutsideSendWindow");
        Assert.NotNull(audit);
        Assert.Equal("Warning", audit.Level);
        Assert.Equal("Deferred", audit.Outcome);
    }

    [Fact]
    public async Task Jwt_replay_and_invalid_gateway_tokens_are_rejected()
    {
        await using (var db = _database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
        }

        await using var factory = new ApiApplicationFactory(_database.ConnectionString);
        var client = factory.CreateClient();
        var token = TestJwt.Create("whatsapp.notifications.claim", jti: "same-jti");
        var emptyClaimRequest = new { clinicIds = Array.Empty<Guid>(), batchSize = 10 };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var first = await client.PostAsJsonAsync("/api/whatsapp/notifications/claim", emptyClaimRequest);
        var replay = await client.PostAsJsonAsync("/api/whatsapp/notifications/claim", emptyClaimRequest);

        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var wrongAudience = factory.CreateClient();
        wrongAudience.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwt.Create("whatsapp.notifications.claim", audience: "wrong-audience"));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await wrongAudience.PostAsJsonAsync("/api/whatsapp/notifications/claim", emptyClaimRequest)).StatusCode);

        var expired = factory.CreateClient();
        expired.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwt.Create("whatsapp.notifications.claim", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await expired.PostAsJsonAsync("/api/whatsapp/notifications/claim", emptyClaimRequest)).StatusCode);

        var wrongSignature = factory.CreateClient();
        wrongSignature.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwt.Create("whatsapp.notifications.claim", secret: "wrong-secret"));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await wrongSignature.PostAsJsonAsync("/api/whatsapp/notifications/claim", emptyClaimRequest)).StatusCode);
    }

    private static HttpClient CreateAuthorizedClient(ApiApplicationFactory factory, string scope)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create(scope));
        return client;
    }

    private static async Task<HashSet<Guid>> ReadNotificationIdsAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("notificationId").GetGuid())
            .ToHashSet();
    }
}

public static class HashSetExtensions
{
    public static bool UnionWithNoDuplicates<T>(this HashSet<T> target, IEnumerable<T> values)
    {
        foreach (var value in values)
        {
            if (!target.Add(value))
                return false;
        }

        return true;
    }
}
