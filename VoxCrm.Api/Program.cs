using VoxCrm.Api.DependencyInjection;
using VoxCrm.Api.Endpoints;
using VoxCrm.Api.Security;
using VoxCrm.Infrastructure.Configuration;

var builder = WebApplication.CreateBuilder(args);
var repoRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, ".."));

builder.Configuration
    .AddVoxCrmEnvFile(repoRoot)
    .AddEnvironmentVariables();

builder.Services.AddVoxCrmApi(builder.Configuration);
builder.Services.AddScoped<GatewayJwtAuthenticator>();

var app = builder.Build();

GatewayJwtSecretGuard.ThrowIfUnsafeSecret(app.Configuration, app.Environment);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapWhatsAppEndpoints();
app.MapAuditLogEndpoints();

app.Run();

public partial class Program;
