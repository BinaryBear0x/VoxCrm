using VoxCrm.Api.DependencyInjection;
using VoxCrm.Api.Endpoints;
using VoxCrm.Api.Security;
using VoxCrm.Infrastructure.Configuration;

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        using var response = await healthClient.GetAsync("http://127.0.0.1:8080/api/health");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        Environment.ExitCode = 1;
    }
    return;
}

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
