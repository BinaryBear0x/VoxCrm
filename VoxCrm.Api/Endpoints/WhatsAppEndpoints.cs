using VoxCrm.Api.Security;
using VoxCrm.Application.Audit;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Api.Endpoints;

public static class WhatsAppEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppEndpoints(this IEndpointRouteBuilder app)
    {
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
