using VoxCrm.Application.Workday;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.Dashboard;

public sealed record WorkspaceAppointment(Appointment Appointment, string OwnerName, string? Phone);
public sealed record WorkspaceCallback(ClinicTask Task, string PatientName, string OwnerName, string? Phone);
public sealed record ClinicWorkspace(string ClinicName, DateOnly Today, WorkdayPage Workday, decimal DailyReceipts, int DailyExaminations,
    string? Note, long NoteRevision, IReadOnlyList<WorkspaceAppointment> Appointments, IReadOnlyList<WorkspaceCallback> Callbacks, int Patients, int Owners, decimal Outstanding);
public interface IClinicWorkspaceService
{
    Task<ClinicWorkspace> GetAsync(DateOnly? date, CancellationToken ct = default);
    Task<string> GetClinicNameAsync(CancellationToken ct = default);
    Task SaveNoteAsync(string? note, long revision, Guid actor, CancellationToken ct = default);
}
