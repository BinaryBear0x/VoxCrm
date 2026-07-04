extern alias VoxCrmApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VoxCrm.Infrastructure.Data;

using ApiProgram = VoxCrmApi::Program;

namespace VoxCrm.IntegrationTests.Infrastructure;

public sealed class ApiApplicationFactory : WebApplicationFactory<ApiProgram>
{
    private readonly string _connectionString;

    public ApiApplicationFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["WhatsAppGateway:Issuer"] = TestJwt.Issuer,
                ["WhatsAppGateway:Audience"] = TestJwt.Audience,
                ["WhatsAppGateway:JwtSecret"] = TestJwt.Secret,
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<VoxCrmDbContext>>();
            services.AddDbContext<VoxCrmDbContext>(options =>
                options.UseNpgsql(_connectionString, npgsql => npgsql.MigrationsAssembly("VoxCrm.Infrastructure")));
        });
    }
}
