namespace VoxCrm.Application.Dashboard;

public sealed record ClinicDashboard(
    int TotalPetOwners,
    int TotalPatients,
    int TotalAppointments,
    decimal TotalOutstandingDebt,
    int PendingWhatsAppMessages);

public interface IDashboardService
{
    Task<ClinicDashboard> GetClinicDashboardAsync(CancellationToken cancellationToken = default);
}
