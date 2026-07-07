using VoxCrm.Application.DependencyInjection;
using VoxCrm.Infrastructure.DependencyInjection;

namespace VoxCrm.Api.DependencyInjection;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddVoxCrmApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenApi();
        services.AddMemoryCache();
        services.AddVoxCrmApplication();
        services.AddVoxCrmInfrastructure(configuration);
        return services;
    }
}
