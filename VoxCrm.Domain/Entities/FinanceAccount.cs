using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities;

// Finans kayıtları iletişim/WhatsApp kuyruğundan bağımsızdır.
public sealed class FinanceAccount : BaseEntity, ITenantEntity
{
    public Guid ClinicID { get; set; }
    public Guid? PetOwnerId { get; set; }
    public PetOwner? PetOwner { get; set; }
    public Guid? PatientId { get; set; }
    public Patient? Patient { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public long Revision { get; set; } = 1;
    public Guid SubmissionId { get; set; } = Guid.NewGuid();
    public ICollection<FinanceCharge> Charges { get; set; } = new List<FinanceCharge>();
    public ICollection<FinanceInstallment> Installments { get; set; } = new List<FinanceInstallment>();
    public ICollection<FinanceReceipt> Receipts { get; set; } = new List<FinanceReceipt>();
    public ICollection<FinanceChange> Changes { get; set; } = new List<FinanceChange>();
}

public sealed class FinanceCharge : BaseEntity, ITenantEntity
{
    public Guid ClinicID { get; set; }
    public Guid FinanceAccountId { get; set; }
    public FinanceAccount FinanceAccount { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly ChargeDate { get; set; }
}

public sealed class FinanceInstallment : BaseEntity, ITenantEntity
{
    public Guid ClinicID { get; set; }
    public Guid FinanceAccountId { get; set; }
    public FinanceAccount FinanceAccount { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public ICollection<FinanceReceipt> Receipts { get; set; } = new List<FinanceReceipt>();
}

public sealed class FinanceReceipt : BaseEntity, ITenantEntity
{
    public Guid ClinicID { get; set; }
    public Guid FinanceAccountId { get; set; }
    public FinanceAccount FinanceAccount { get; set; } = null!;
    public Guid? FinanceInstallmentId { get; set; }
    public FinanceInstallment? FinanceInstallment { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaidDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public sealed class FinanceChange : BaseEntity, ITenantEntity
{
    public Guid ClinicID { get; set; }
    public Guid FinanceAccountId { get; set; }
    public FinanceAccount FinanceAccount { get; set; } = null!;
    public Guid ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}
