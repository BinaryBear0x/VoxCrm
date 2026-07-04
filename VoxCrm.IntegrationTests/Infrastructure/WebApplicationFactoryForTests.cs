extern alias VoxCrmWeb;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VoxCrm.Infrastructure.Data;

using WebProgram = VoxCrmWeb::Program;

namespace VoxCrm.IntegrationTests.Infrastructure;

public sealed class WebApplicationFactoryForTests : WebApplicationFactory<WebProgram>
{
    private readonly string _connectionString;

    public WebApplicationFactoryForTests(string connectionString)
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
                ["WhatsAppGateway:BaseUrl"] = "http://127.0.0.1:8088",
                ["WhatsAppGateway:Issuer"] = "voxcrm-web",
                ["WhatsAppGateway:Audience"] = "voxcrm-whatsapp-gateway",
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
