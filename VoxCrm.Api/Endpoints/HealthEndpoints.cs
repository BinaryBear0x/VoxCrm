namespace VoxCrm.Api.Endpoints;

using Microsoft.EntityFrameworkCore;
using VoxCrm.Infrastructure.Data;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (VoxCrmDbContext db, CancellationToken cancellationToken) =>
            await db.Database.CanConnectAsync(cancellationToken)
                ? Results.Ok(new { status = "ok", service = "voxcrm-api" })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        return app;
    }
}
