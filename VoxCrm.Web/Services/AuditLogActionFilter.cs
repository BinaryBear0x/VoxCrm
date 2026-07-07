using Microsoft.AspNetCore.Mvc.Filters;
using System.Diagnostics;
using System.Security.Claims;
using VoxCrm.Application.Audit;

namespace VoxCrm.Web.Services;

public class AuditLogActionFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> IgnoredPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/WhatsApp/QrStatus"
    };

    private readonly IAuditLogger _auditLogger;

    public AuditLogActionFilter(IAuditLogger auditLogger)
    {
        _auditLogger = auditLogger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();
        ActionExecutedContext executed;
        try
        {
            executed = await next();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await WriteLogAsync(context, ex, StatusCodes.Status500InternalServerError, stopwatch.ElapsedMilliseconds);
            throw;
        }

        stopwatch.Stop();
        var httpContext = context.HttpContext;
        if (IgnoredPaths.Contains(httpContext.Request.Path))
            return;

        var isMeaningfulGet = httpContext.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && context.RouteData.Values["controller"]?.ToString() is "Dealer" or "WhatsApp";
        var isMutation = httpContext.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE";
        if (!isMeaningfulGet && !isMutation && executed.Exception == null)
            return;

        await WriteLogAsync(context, executed.Exception, httpContext.Response.StatusCode, stopwatch.ElapsedMilliseconds);
    }

    private async Task WriteLogAsync(
        ActionExecutingContext context,
        Exception? exception,
        int statusCode,
        long durationMs)
    {
        var httpContext = context.HttpContext;
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "Unknown";
        var action = context.RouteData.Values["action"]?.ToString() ?? "Unknown";
        var category = exception == null
            ? statusCode >= 400 ? AuditLogCategories.Validation : AuditLogCategories.Operation
            : AuditLogCategories.Exception;
        var level = exception != null
            ? AuditLogLevels.Error
            : statusCode is >= 400 and < 500 ? AuditLogLevels.Warning : AuditLogLevels.Info;
        var outcome = exception != null || statusCode >= 500
            ? AuditLogOutcomes.Failed
            : statusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
                ? AuditLogOutcomes.Denied
                : statusCode >= 400 ? AuditLogOutcomes.Failed : AuditLogOutcomes.Success;

        var log = new AuditLogEntry
        {
            Level = level,
            Source = AuditLogSources.Web,
            Category = category,
            Outcome = outcome,
            Action = $"{controller}.{action}",
            Message = exception == null
                ? $"{httpContext.Request.Method} {httpContext.Request.Path} işlemi tamamlandı."
                : exception.Message,
            EntityType = controller,
            EntityId = context.RouteData.Values["id"]?.ToString(),
            DealerId = TryGetGuid(httpContext.User.FindFirst("DealerId")?.Value),
            ClinicId = TryGetGuid(httpContext.User.FindFirst("ClinicId")?.Value),
            ActorUserId = TryGetGuid(httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value),
            ActorUserName = httpContext.User.Identity?.Name,
            ActorRole = httpContext.User.IsInRole("Dealer")
                ? "Dealer"
                : httpContext.User.IsInRole("Clinic") ? "Clinic" : null,
            HttpMethod = httpContext.Request.Method,
            Path = httpContext.Request.Path,
            StatusCode = statusCode,
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = httpContext.Request.Headers["User-Agent"].ToString(),
            ExceptionType = exception?.GetType().Name,
            TraceId = httpContext.TraceIdentifier,
            CorrelationId = httpContext.TraceIdentifier,
            DurationMs = durationMs,
            RequestId = httpContext.Request.Headers["X-Request-ID"].ToString(),
            Metadata = new Dictionary<string, object?>
            {
                ["route"] = $"{controller}.{action}",
                ["query"] = httpContext.Request.QueryString.Value,
                ["formFieldCount"] = httpContext.Request.HasFormContentType ? httpContext.Request.Form.Count : 0
            }
        };

        await _auditLogger.LogAsync(log);
    }

    private static Guid? TryGetGuid(string? value)
    {
        return Guid.TryParse(value, out var result) ? result : null;
    }
}
