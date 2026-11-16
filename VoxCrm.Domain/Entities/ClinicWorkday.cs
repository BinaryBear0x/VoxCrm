using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities;

public sealed class ClinicTask : BaseEntity, ITenantEntity
{
    public Guid ClinicID { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public DateOnly DueDate { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public Guid? CareCardId { get; set; }
    public string Kind { get; set; } = "Manual";
    public string Status { get; set; } = "Open";
    public bool Important { get; set; }
    public long Revision { get; set; } = 1;
    public Guid SubmissionId { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class CareCard : BaseEntity, ITenantEntity
{
    public Guid ClinicID { get; set; }
    public Guid ExaminationId { get; set; }
    public Guid PetOwnerId { get; set; }
    public string TodayActions { get; set; } = "";
    public string HomeSteps { get; set; } = "";
    public string CallInstructions { get; set; } = "";
    public DateOnly? FollowUpDate { get; set; }
    public string State { get; set; } = "Draft";
    public long Revision { get; set; } = 1;
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RecipientSnapshot { get; set; }
    public string? TokenHash { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? OwnerResponse { get; set; }
    public DateTime? RespondedAt { get; set; }
}

public sealed class ClosingAcknowledgement : BaseEntity, ITenantEntity
{
    public Guid ClinicID { get; set; }
    public DateOnly Date { get; set; }
    public string IssueKey { get; set; } = "";
    public string Note { get; set; } = "";
    public Guid ActorId { get; set; }
}
