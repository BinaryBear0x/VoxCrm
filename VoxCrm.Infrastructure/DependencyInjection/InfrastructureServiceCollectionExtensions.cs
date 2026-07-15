using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VoxCrm.Application.Audit;
using VoxCrm.Application.Clinics;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Common;
using VoxCrm.Infrastructure.Audit;
using VoxCrm.Infrastructure.Clinics;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Patients;
using VoxCrm.Infrastructure.PetOwners;
using VoxCrm.Infrastructure.Vaccinations;
using VoxCrm.Infrastructure.WhatsApp;
using VoxCrm.Application.Patients;
using VoxCrm.Application.PetOwners;
using VoxCrm.Application.Vaccinations;
using VoxCrm.Application.Finance;
using VoxCrm.Application.Appointments;
using VoxCrm.Infrastructure.Appointments;
using VoxCrm.Infrastructure.Finance;
using VoxCrm.Application.Dashboard;
using VoxCrm.Application.Examinations;
using VoxCrm.Application.ServiceItems;
using VoxCrm.Infrastructure.Dashboard;
using VoxCrm.Infrastructure.Examinations;
using VoxCrm.Infrastructure.ServiceItems;
using VoxCrm.Application.DealerOperations;
using VoxCrm.Infrastructure.DealerOperations;
using VoxCrm.Infrastructure.Security;

namespace VoxCrm.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddVoxCrmInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");

        services.TryAddSingleton<IPiiProtector, AesGcmPiiProtector>();
        services.TryAddSingleton<PiiEncryptionInterceptor>();
        services.AddDbContext<VoxCrmDbContext>((provider, options) =>
            options.UseNpgsql(connectionString, builder => builder.MigrationsAssembly("VoxCrm.Infrastructure"))
                .AddInterceptors(provider.GetRequiredService<PiiEncryptionInterceptor>()));

        services.TryAddScoped<ITenantService, SystemTenantService>();
        services.TryAddScoped<IAuditLogger, DbAuditLogger>();
        services.TryAddScoped<IWhatsAppNotificationRepository, WhatsAppNotificationRepository>();
        return services;
    }

    public static IServiceCollection AddVoxCrmWebInfrastructureServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IPiiProtector, AesGcmPiiProtector>();
        services.TryAddSingleton<PiiEncryptionInterceptor>();
        services.TryAddScoped<IAuditLogger, DbAuditLogger>();
        services.TryAddScoped<IWhatsAppNotificationRepository, WhatsAppNotificationRepository>();
        services.TryAddScoped<IClinicManagementRepository, ClinicManagementRepository>();
        services.TryAddScoped<IPatientService, PatientService>();
        services.TryAddScoped<IPetOwnerService, PetOwnerService>();
        services.TryAddScoped<IVaccinationService, VaccinationService>();
        services.TryAddScoped<IVaccineTypeService, VaccineTypeService>();
        services.TryAddScoped<IFinanceService, FinanceService>();
        services.TryAddScoped<IAppointmentService, AppointmentService>();
        services.TryAddScoped<IDashboardService, DashboardService>();
        services.TryAddScoped<IExaminationService, ExaminationService>();
        services.TryAddScoped<IServiceItemService, ServiceItemService>();
        services.TryAddScoped<IDealerLogService, DealerLogService>();
        return services;
    }
}
