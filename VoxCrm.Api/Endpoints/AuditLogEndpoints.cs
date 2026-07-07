using VoxCrm.Api.Security;
using VoxCrm.Application.Audit;
using VoxCrm.Application.WhatsApp;

namespace VoxCrm.Api.Endpoints;

public static class AuditLogEndpoints
{
    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/system/audit-logs", async (
            HttpRequest request,
            ExternalAuditLogRequest payload,
            GatewayJwtAuthenticator authenticator,
            IAuditLogger auditLogger,
            CancellationToken cancellationToken) =>
        {
            var auth = authenticator.Authorize(request, "system.audit.write");
            if (!auth.IsAuthorized)
                return Results.Unauthorized();

            await auditLogger.LogAsync(new AuditLogEntry
            {
                Level = payload.Level,
                Source = payload.Source,
                Category = payload.Category,
                Outcome = payload.Outcome,
                Action = payload.Action,
                Message = payload.Message,
                DealerId = payload.DealerId,
                ClinicId = payload.ClinicId,
                EntityType = payload.EntityType,
                EntityId = payload.EntityId,
                ErrorCode = payload.ErrorCode,
                CorrelationId = payload.CorrelationId,
                TraceId = request.HttpContext.TraceIdentifier,
                Metadata = payload.Metadata
            }, cancellationToken);

            return Results.Ok(new { ok = true });
        });

        return app;
    }
}
