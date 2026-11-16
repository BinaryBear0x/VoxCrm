using VoxCrm.Domain.Common;
namespace VoxCrm.Domain.Entities;
public sealed class OwnerFollowUpShare : BaseEntity, ITenantEntity
{
    public Guid ClinicID { get; set; }
    public Guid PetOwnerId { get; set; }
    public string PatientIdsJson { get; set; } = "[]";
    public string RecipientSnapshot { get; set; } = "";
    public string? TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool ShowVaccinations { get; set; }
    public bool ShowHistory { get; set; }
    public bool ShowAppointments { get; set; }
    public bool ShowCare { get; set; }
    public string? PublicNote { get; set; }
    public Guid? NoteApprovedBy { get; set; }
    public Guid LastIssueId { get; set; }
    public long Revision { get; set; }
}
