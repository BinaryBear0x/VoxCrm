using Microsoft.Extensions.DependencyInjection;
using VoxCrm.Application.Clinics;
using VoxCrm.Application.WhatsApp;

namespace VoxCrm.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddVoxCrmApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClinicSendWindowCalculator, ClinicSendWindowCalculator>();
        services.AddScoped<IWhatsAppNotificationService, WhatsAppNotificationService>();
        return services;
    }

    public static IServiceCollection AddVoxCrmWebApplication(this IServiceCollection services)
    {
        services.AddScoped<IClinicManagementService, ClinicManagementService>();
        return services;
    }
}
