using VoxCrm.Api.Security;
using VoxCrm.Application.Audit;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.WhatsApp;

namespace VoxCrm.Api.Endpoints;

public static class WhatsAppEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/whatsapp/notifications/{id:guid}/validate", async (
            Guid id, HttpRequest request, DeliveryValidation input, GatewayJwtAuthenticator authenticator,
            VoxCrmDbContext db, WhatsAppRecipientGuard guard, CancellationToken ct) =>
        {
            if (!authenticator.Authorize(request, "whatsapp.notifications.status").IsAuthorized) return Results.Unauthorized();
            var n = await db.WhatsAppNotifications.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ID == id && x.ClinicID == input.ClinicId, ct);
            if (n == null) return Results.NotFound();
            if (n.Status is not (WhatsAppNotificationStatuses.Processing or WhatsAppNotificationStatuses.DirectSending)
                || n.LockExpiresAt == null || n.LockExpiresAt <= DateTime.UtcNow)
            {
                // Nothing was sent. VoxCRM keeps ownership of the state (lock recovery requeues it),
                // so the gateway must not mark it NeedsReview on its own.
                return Results.Ok(new { allowed = false, retryable = true, error = "Gönderim kilidi geçersiz veya mesaj artık gönderilebilir durumda değil." });
            }
            var error = await guard.CheckAsync(n, ct);
            if (WhatsAppRecipientGuard.Digits(input.Phone) != WhatsAppRecipientGuard.Digits(n.PhoneNumber) || input.Message != n.MessageContent)
                error = "Gönderim içeriği önizlemeden sonra değişti. Gönderim durduruldu.";
            if (error != null)
            {
                n.Status = WhatsAppNotificationStatuses.NeedsReview; n.LastError = error; n.NextAttemptAt = null;
                n.LockedAt = null; n.LockedBy = null; n.LockExpiresAt = null;
                await db.SaveChangesAsync(ct);
            }
            else
            {
                // The send is about to happen: keep the lock alive long enough that lock recovery
                // cannot requeue (and double-send) the message while the worker is delivering it.
                var extended = DateTime.UtcNow.AddMinutes(5);
                if (n.LockExpiresAt < extended)
                {
                    n.LockExpiresAt = extended;
                    await db.SaveChangesAsync(ct);
                }
            }
            return Results.Ok(new { allowed = error == null, retryable = false, error });
        });
        app.MapPost("/api/whatsapp/notifications/claim", async (
            HttpRequest httpRequest,
            WhatsAppClaimRequest request,
            GatewayJwtAuthenticator authenticator,
            IWhatsAppNotificationService service,
            IAuditLogger auditLogger,
            CancellationToken cancellationToken) =>
        {
            var auth = authenticator.Authorize(httpRequest, "whatsapp.notifications.claim");
            if (!auth.IsAuthorized)
            {
                await LogDeniedAsync(auditLogger, httpRequest, "WhatsAppNotifications.Claim", "whatsapp.notifications.claim", cancellationToken);
                return Results.Unauthorized();
            }

            var clinicIds = request.ClinicIds.Distinct().ToList();
            if (clinicIds.Count == 0)
                return Results.BadRequest("En az bir clinicId zorunlu.");

            var gatewayId = string.IsNullOrWhiteSpace(request.GatewayId) ? auth.Subject : request.GatewayId;
            var messages = await service.ClaimAsync(
                request with { ClinicIds = clinicIds },
                gatewayId,
                DateTime.UtcNow,
                cancellationToken);

            return Results.Ok(messages);
        });

        app.MapPost("/api/whatsapp/notifications/{id:guid}/status", async (
            Guid id,
            HttpRequest httpRequest,
            WhatsAppStatusRequest request,
            GatewayJwtAuthenticator authenticator,
            IWhatsAppNotificationService service,
            IAuditLogger auditLogger,
            CancellationToken cancellationToken) =>
        {
            var auth = authenticator.Authorize(httpRequest, "whatsapp.notifications.status");
            if (!auth.IsAuthorized)
            {
                await LogDeniedAsync(auditLogger, httpRequest, "WhatsAppNotifications.ReportStatus", "whatsapp.notifications.status", cancellationToken);
                return Results.Unauthorized();
            }

            if (!IsSupportedStatus(request.Status))
                return Results.BadRequest("Desteklenmeyen WhatsApp notification status.");

            var updated = await service.ReportStatusAsync(id, request, DateTime.UtcNow, cancellationToken);
            return updated ? Results.Ok(new { ID = id, request.Status }) : Results.NotFound("Bildirim bulunamadi.");
        });

        app.MapPost("/api/whatsapp/notifications/recover-expired-processing", async (
            HttpRequest httpRequest,
            GatewayJwtAuthenticator authenticator,
            IWhatsAppNotificationService service,
            IAuditLogger auditLogger,
            CancellationToken cancellationToken) =>
        {
            var auth = authenticator.Authorize(httpRequest, "whatsapp.notifications.recover");
            if (!auth.IsAuthorized)
            {
                await LogDeniedAsync(auditLogger, httpRequest, "WhatsAppNotifications.RecoverExpiredProcessing", "whatsapp.notifications.recover", cancellationToken);
                return Results.Unauthorized();
            }

            var recovered = await service.RecoverExpiredProcessingAsync(DateTime.UtcNow, cancellationToken);
            return Results.Ok(new { recovered });
        });

        app.MapPost("/api/whatsapp/inbound", async (
            HttpRequest httpRequest,
            WhatsAppInboundRequest request,
            GatewayJwtAuthenticator authenticator,
            IWhatsAppNotificationService service,
            IAuditLogger auditLogger,
            CancellationToken cancellationToken) =>
        {
            var auth = authenticator.Authorize(httpRequest, "whatsapp.inbound.write");
            if (!auth.IsAuthorized)
            {
                await LogDeniedAsync(auditLogger, httpRequest, "WhatsAppInbound.Write", "whatsapp.inbound.write", cancellationToken);
                return Results.Unauthorized();
            }

            // An empty provider id would make every later message look like a duplicate of the
            // first one, silently dropping them; oversized values would fail in the database.
            if (request is null
                || request.ClinicId == Guid.Empty
                || string.IsNullOrWhiteSpace(request.ProviderMessageId) || request.ProviderMessageId.Length > 256
                || string.IsNullOrWhiteSpace(request.FromPhone) || request.FromPhone.Length > 64
                || string.IsNullOrEmpty(request.Message) || request.Message.Length > 4096
                || string.IsNullOrWhiteSpace(request.GatewaySessionId) || request.GatewaySessionId.Length > 128)
                return Results.BadRequest("Geçersiz inbound mesaj verisi.");

            var messageId = await service.WriteInboundAsync(request, DateTime.UtcNow, cancellationToken);
            return Results.Ok(new { ID = messageId });
        });

        return app;
    }

    private static bool IsSupportedStatus(string status)
    {
        return status is WhatsAppNotificationStatuses.Sent
            or WhatsAppNotificationStatuses.Failed
            or WhatsAppNotificationStatuses.RetryScheduled
            or WhatsAppNotificationStatuses.NeedsReview
            or WhatsAppNotificationStatuses.Cancelled;
    }

    private static Task LogDeniedAsync(
        IAuditLogger auditLogger,
        HttpRequest request,
        string action,
        string requiredScope,
        CancellationToken cancellationToken)
    {
        return auditLogger.LogAsync(new AuditLogEntry
        {
            Level = AuditLogLevels.Warning,
            Source = AuditLogSources.Api,
            Category = AuditLogCategories.Security,
            Outcome = AuditLogOutcomes.Denied,
            Action = action,
            Message = "Gateway JWT dogrulamasi basarisiz oldu.",
            HttpMethod = request.Method,
            Path = request.Path,
            StatusCode = StatusCodes.Status401Unauthorized,
            IpAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = request.Headers.UserAgent.ToString(),
            TraceId = request.HttpContext.TraceIdentifier,
            Metadata = new Dictionary<string, object?> { ["requiredScope"] = requiredScope }
        }, cancellationToken);
    }
}
public sealed record DeliveryValidation(Guid ClinicId, string Phone, string Message);
