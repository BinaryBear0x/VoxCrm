using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VoxCrm.Application.Audit;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Infrastructure.Audit;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.WhatsApp;

namespace VoxCrm.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddVoxCrmInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");

        services.AddDbContext<VoxCrmDbContext>(options =>
            options.UseNpgsql(connectionString, builder => builder.MigrationsAssembly("VoxCrm.Infrastructure")));

        services.AddScoped<IAuditLogger, DbAuditLogger>();
        services.AddScoped<IWhatsAppNotificationRepository, WhatsAppNotificationRepository>();
        return services;
    }
}
