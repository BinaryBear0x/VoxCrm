using Microsoft.AspNetCore.Mvc.Filters;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Web.Services;

public class AuditLogActionFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> LoggedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST",
        "PUT",
        "PATCH",
        "DELETE"
    };

    private readonly IServiceScopeFactory _scopeFactory;

    public AuditLogActionFilter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();
        var httpContext = context.HttpContext;
        if (!LoggedMethods.Contains(httpContext.Request.Method))
            return;

        var controller = context.RouteData.Values["controller"]?.ToString() ?? "Unknown";
        var action = context.RouteData.Values["action"]?.ToString() ?? "Unknown";
        var exception = executed.Exception;

        var log = new SystemAuditLog
        {
            Level = exception == null ? "Info" : "Error",
            Category = exception == null ? "Operation" : "Exception",
            Action = $"{controller}.{action}",
            Message = exception == null
                ? $"{httpContext.Request.Method} {httpContext.Request.Path} işlemi tamamlandı."
                : exception.Message,
            EntityType = controller,
            EntityId = context.RouteData.Values["id"]?.ToString(),
            DealerId = TryGetGuid(httpContext.User.FindFirst("DealerId")?.Value),
            ClinicId = TryGetGuid(httpContext.User.FindFirst("ClinicId")?.Value),
            ActorUserId = TryGetGuid(httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value),
            ActorUserName = httpContext.User.Identity?.Name,
            ActorRole = httpContext.User.IsInRole("Dealer")
                ? "Dealer"
                : httpContext.User.IsInRole("Clinic") ? "Clinic" : null,
            HttpMethod = httpContext.Request.Method,
            Path = httpContext.Request.Path,
            StatusCode = httpContext.Response.StatusCode,
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = httpContext.Request.Headers["User-Agent"].ToString(),
            ExceptionType = exception?.GetType().Name,
            TraceId = httpContext.TraceIdentifier
        };

        await WriteLogAsync(log);
    }

    private async Task WriteLogAsync(SystemAuditLog log)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VoxCrmDbContext>();
            db.SystemAuditLogs.Add(log);
            await db.SaveChangesAsync();
        }
        catch
        {
            // Audit logging must never break the user-facing operation.
        }
    }

    private static Guid? TryGetGuid(string? value)
    {
        return Guid.TryParse(value, out var result) ? result : null;
    }
}
