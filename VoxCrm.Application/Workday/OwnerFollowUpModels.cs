using System.ComponentModel.DataAnnotations;
using VoxCrm.Domain.Entities;
namespace VoxCrm.Application.Workday;
public sealed class FollowUpInput
{
    public Guid OwnerId { get; set; }
    public Guid IssueId { get; set; } = Guid.NewGuid();
    public long Revision { get; set; }
    public List<Guid> PatientIds { get; set; } = new();
    [Range(1,30)] public int Days { get; set; } = 14;
    public bool ShowVaccinations { get; set; } = true;
    public bool ShowHistory { get; set; } = true;
    public bool ShowAppointments { get; set; } = true;
    public bool ShowCare { get; set; }
    [StringLength(2000,ErrorMessage="Paylaşılacak not en fazla 2000 karakter olabilir.")] public string? PublicNote { get; set; }
    public bool ConfirmRecipient { get; set; }
}
public sealed record FollowUpManager(IReadOnlyList<PetOwner> Owners, PetOwner? Owner, IReadOnlyList<Patient> Patients, OwnerFollowUpShare? Share, IReadOnlyList<OwnerFollowUpShare> Shares, IReadOnlyList<ClinicTask> Requests);
public sealed record FollowUpVaccine(string Name, DateTime Applied, DateTime Next, bool Current);
public sealed record FollowUpAppointment(DateTime LocalTime);
public sealed record FollowUpCare(string Today, string Steps, string Call, DateOnly? FollowUp);
public sealed record FollowUpPatient(Guid Id,string Name,string? Species,IReadOnlyList<FollowUpVaccine> Vaccines,IReadOnlyList<FollowUpAppointment> Appointments,IReadOnlyList<FollowUpCare> Care,bool ContactRequested,bool AppointmentRequested);
public sealed record PublicFollowUp(string ClinicName,string? Phone,DateTime Today,string? Note,bool ShowVaccinations,bool ShowHistory,bool ShowAppointments,bool ShowCare,IReadOnlyList<FollowUpPatient> Patients);
