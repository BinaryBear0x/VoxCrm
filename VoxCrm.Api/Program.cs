using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<VoxCrmDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();

var app = builder.Build();
GatewayJwt.ThrowIfUnsafeSecret(app.Configuration, app.Environment);
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapPost("/api/whatsapp/notifications/claim", async (
    HttpRequest httpRequest,
    [FromBody] WhatsAppClaimRequest request,
    VoxCrmDbContext db,
    IConfiguration configuration,
    IMemoryCache replayCache,
    CancellationToken cancellationToken) =>
{
    var auth = GatewayJwt.Authorize(httpRequest, configuration, "whatsapp.notifications.claim", replayCache);
    if (!auth.IsAuthorized) return Results.Unauthorized();

    var clinicIds = request.ClinicIds.Distinct().ToList();
    if (clinicIds.Count == 0) return Results.BadRequest("En az bir clinicId zorunlu.");

    var now = DateTime.UtcNow;
    var gatewayId = string.IsNullOrWhiteSpace(request.GatewayId) ? auth.Subject : request.GatewayId;
    var batchSize = Math.Clamp(request.BatchSize, 1, 50);
    var lockSeconds = Math.Clamp(request.LockSeconds, 30, 600);

    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

    var messages = await ClaimNotificationsAsync(db, clinicIds, now, gatewayId, batchSize, lockSeconds, cancellationToken);

    await transaction.CommitAsync(cancellationToken);

    return Results.Ok(messages);
});

app.MapPost("/api/whatsapp/notifications/{id:guid}/status", async (
    Guid id,
    HttpRequest httpRequest,
    [FromBody] WhatsAppStatusRequest request,
    VoxCrmDbContext db,
    IConfiguration configuration,
    IMemoryCache replayCache,
    CancellationToken cancellationToken) =>
{
    var auth = GatewayJwt.Authorize(httpRequest, configuration, "whatsapp.notifications.status", replayCache);
    if (!auth.IsAuthorized) return Results.Unauthorized();

    var notification = await db.WhatsAppNotifications.FindAsync(new object?[] { id }, cancellationToken);
    if (notification == null) return Results.NotFound("Bildirim bulunamadi.");

    if (!IsSupportedStatus(request.Status))
        return Results.BadRequest("Desteklenmeyen WhatsApp notification status.");

    notification.Status = request.Status;
    notification.GatewayMessageId = request.GatewayMessageId ?? notification.GatewayMessageId;
    notification.LastError = request.LastError;
    notification.LockedAt = null;
    notification.LockedBy = null;
    notification.LockExpiresAt = null;

    if (request.RetryCount.HasValue)
        notification.RetryCount = Math.Max(notification.RetryCount, request.RetryCount.Value);

    if (request.Status == WhatsAppNotificationStatuses.Sent)
    {
        notification.SentAt = DateTime.UtcNow;
        notification.NextAttemptAt = null;
        notification.LastError = null;
    }
    else if (request.Status == WhatsAppNotificationStatuses.RetryScheduled)
    {
        notification.NextAttemptAt = request.NextAttemptAt ?? DateTime.UtcNow.AddMinutes(5);
    }
    else
    {
        notification.NextAttemptAt = null;
    }

    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { notification.ID, notification.Status });
});

app.MapPost("/api/whatsapp/notifications/recover-expired-processing", async (
    HttpRequest httpRequest,
    VoxCrmDbContext db,
    IConfiguration configuration,
    IMemoryCache replayCache,
    CancellationToken cancellationToken) =>
{
    var auth = GatewayJwt.Authorize(httpRequest, configuration, "whatsapp.notifications.recover", replayCache);
    if (!auth.IsAuthorized) return Results.Unauthorized();

    var now = DateTime.UtcNow;
    var expired = await db.WhatsAppNotifications
        .IgnoreQueryFilters()
        .Where(n => n.Status == WhatsAppNotificationStatuses.Processing
                    && n.LockExpiresAt != null
                    && n.LockExpiresAt < now)
        .ToListAsync(cancellationToken);

    foreach (var notification in expired)
    {
        notification.Status = WhatsAppNotificationStatuses.RetryScheduled;
        notification.NextAttemptAt = now.AddMinutes(5);
        notification.LockedAt = null;
        notification.LockedBy = null;
        notification.LockExpiresAt = null;
        notification.LastError = "Processing lock expired before gateway reported a final delivery state.";
    }

    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { recovered = expired.Count });
});

app.MapPost("/api/whatsapp/inbound", async (
    HttpRequest httpRequest,
    [FromBody] WhatsAppInboundRequest request,
    VoxCrmDbContext db,
    IConfiguration configuration,
    IMemoryCache replayCache,
    CancellationToken cancellationToken) =>
{
    var auth = GatewayJwt.Authorize(httpRequest, configuration, "whatsapp.inbound.write", replayCache);
    if (!auth.IsAuthorized) return Results.Unauthorized();

    var message = new WhatsAppInboundMessage
    {
        ClinicID = request.ClinicId,
        FromPhone = request.FromPhone,
        Message = request.Message,
        ReceivedAt = request.ReceivedAt ?? DateTime.UtcNow,
        GatewaySessionId = request.GatewaySessionId
    };

    db.WhatsAppInboundMessages.Add(message);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { message.ID });
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "voxcrm-api" }));

app.Run();

static bool IsSupportedStatus(string status)
{
    return status is WhatsAppNotificationStatuses.Sent
        or WhatsAppNotificationStatuses.Failed
        or WhatsAppNotificationStatuses.RetryScheduled
        or WhatsAppNotificationStatuses.NeedsReview
        or WhatsAppNotificationStatuses.Cancelled;
}

static async Task<IReadOnlyList<WhatsAppClaimedNotification>> ClaimNotificationsAsync(
    VoxCrmDbContext db,
    IReadOnlyList<Guid> clinicIds,
    DateTime now,
    string gatewayId,
    int batchSize,
    int lockSeconds,
    CancellationToken cancellationToken)
{
    var connection = db.Database.GetDbConnection();
    if (connection.State != ConnectionState.Open)
        await connection.OpenAsync(cancellationToken);

    await using var command = connection.CreateCommand();
    command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
    command.CommandText = """
UPDATE "WhatsAppNotifications" AS n
SET "Status" = @processing_status,
    "LockedAt" = @locked_at,
    "LockedBy" = @locked_by,
    "LockExpiresAt" = @lock_expires_at,
    "LastError" = NULL
WHERE n."ID" IN (
    SELECT n2."ID"
    FROM "WhatsAppNotifications" AS n2
    INNER JOIN "Clinics" AS c ON c."ID" = n2."ClinicID"
    WHERE n2."ClinicID" = ANY(@clinic_ids)
      AND n2."NotificationType" = ANY(@notification_types)
      AND n2."Status" = ANY(@claimable_statuses)
      AND (n2."NextAttemptAt" IS NULL OR n2."NextAttemptAt" <= @now)
      AND c."IsWhatsAppEnabled" = TRUE
      AND c."IsActive" = TRUE
    ORDER BY COALESCE(n2."NextAttemptAt", n2."CreatedAt"), n2."CreatedAt"
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
)
RETURNING n."ID", n."ClinicID", n."PetOwnerId", n."PhoneNumber", n."MessageContent", n."NotificationType", n."RetryCount";
""";
    AddParameter(command, "processing_status", WhatsAppNotificationStatuses.Processing);
    AddParameter(command, "locked_at", now);
    AddParameter(command, "locked_by", gatewayId);
    AddParameter(command, "lock_expires_at", now.AddSeconds(lockSeconds));
    AddParameter(command, "clinic_ids", clinicIds.ToArray());
    AddParameter(command, "notification_types", GetClaimableNotificationTypes());
    AddParameter(command, "claimable_statuses", WhatsAppNotificationStatuses.Claimable.ToArray());
    AddParameter(command, "now", now);
    AddParameter(command, "batch_size", batchSize);

    var claimed = new List<WhatsAppClaimedNotification>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        claimed.Add(new WhatsAppClaimedNotification(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6)));
    }
    return claimed;
}

static void AddParameter(DbCommand command, string name, object value)
{
    var parameter = command.CreateParameter();
    parameter.ParameterName = name;
    parameter.Value = value;
    command.Parameters.Add(parameter);
}

static string[] GetClaimableNotificationTypes() =>
[
    WhatsAppNotificationTypes.VaccinationReminder,
    WhatsAppNotificationTypes.ManualMessage
];

public record WhatsAppClaimRequest(
    IReadOnlyList<Guid> ClinicIds,
    int BatchSize = 10,
    string GatewayId = "voxcrm-whatsapp-gateway",
    int LockSeconds = 180);

public record WhatsAppClaimedNotification(
    Guid NotificationId,
    Guid ClinicId,
    Guid PetOwnerId,
    string PhoneNumber,
    string MessageContent,
    string NotificationType,
    int RetryCount);

public record WhatsAppStatusRequest(
    string Status,
    string? GatewayMessageId,
    string? LastError,
    int? RetryCount,
    DateTime? NextAttemptAt);

public record WhatsAppInboundRequest(
    Guid ClinicId,
    string FromPhone,
    string Message,
    DateTime? ReceivedAt,
    string GatewaySessionId);

public sealed record GatewayAuthResult(bool IsAuthorized, string Subject);

public static class GatewayJwt
{
    private const string DevOnlySecret = "dev-only-change-this-very-long-whatsapp-gateway-secret";

    public static void ThrowIfUnsafeSecret(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var secret = configuration["WhatsAppGateway:JwtSecret"];
        if (!environment.IsDevelopment() && (string.IsNullOrWhiteSpace(secret) || secret == DevOnlySecret))
            throw new InvalidOperationException("WhatsAppGateway:JwtSecret must be configured with a non-development value.");
    }

    public static GatewayAuthResult Authorize(
        HttpRequest request,
        IConfiguration configuration,
        string requiredScope,
        IMemoryCache replayCache)
    {
        var authHeader = request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return new GatewayAuthResult(false, string.Empty);

        var token = authHeader["Bearer ".Length..].Trim();
        var parts = token.Split('.');
        if (parts.Length != 3) return new GatewayAuthResult(false, string.Empty);

        var secret = configuration["WhatsAppGateway:JwtSecret"];
        if (string.IsNullOrWhiteSpace(secret)) return new GatewayAuthResult(false, string.Empty);

        var expectedSignature = Sign($"{parts[0]}.{parts[1]}", secret);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSignature),
                Encoding.UTF8.GetBytes(parts[2])))
            return new GatewayAuthResult(false, string.Empty);

        using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        var root = payload.RootElement;

        if (!TryGetString(root, "iss", out var issuer) || issuer != configuration["WhatsAppGateway:Issuer"])
            return new GatewayAuthResult(false, string.Empty);

        if (!TryGetString(root, "aud", out var audience) || audience != configuration["WhatsAppGateway:Audience"])
            return new GatewayAuthResult(false, string.Empty);

        if (!root.TryGetProperty("exp", out var expElement)
            || DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64()) <= DateTimeOffset.UtcNow)
            return new GatewayAuthResult(false, string.Empty);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64());

        if (!TryGetString(root, "scope", out var scopes)
            || !scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(requiredScope))
            return new GatewayAuthResult(false, string.Empty);

        if (!TryGetString(root, "jti", out var jti))
            return new GatewayAuthResult(false, string.Empty);

        var replayKey = $"whatsapp-jti:{jti}";
        if (replayCache.TryGetValue(replayKey, out _))
            return new GatewayAuthResult(false, string.Empty);
        replayCache.Set(replayKey, true, expiresAt);

        TryGetString(root, "sub", out var subject);
        return new GatewayAuthResult(true, subject);
    }

    private static string Sign(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element)) return false;
        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

public partial class Program;
