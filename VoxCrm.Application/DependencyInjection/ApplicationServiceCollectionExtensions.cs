using Microsoft.Extensions.DependencyInjection;
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
}
