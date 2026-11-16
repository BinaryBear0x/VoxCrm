using System.ComponentModel.DataAnnotations;
using VoxCrm.Domain.Entities;
namespace VoxCrm.Application.Finance;
public static class FinancePaymentMethods
{
    public const string Cash = "Nakit", CreditCard = "Kredi Kartı", Transfer = "Havale/EFT";
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string> { Cash, CreditCard, Transfer };
}
public sealed class FinanceAccountInput
{
    public Guid SubmissionId { get; set; } = Guid.NewGuid();
    public long Revision { get; set; }
    public string CustomerMode { get; set; } = "registered";
    public Guid? PetOwnerId { get; set; }
    public Guid? PatientId { get; set; }
    public string? CustomerName { get; set; }
    [Required(ErrorMessage = "Açıklama zorunludur."), StringLength(1000)]
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly ChargeDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly DueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}
public sealed class FinanceEntryInput
{
    public Guid AccountId { get; set; }
    public Guid? Id { get; set; }
    public long Revision { get; set; }
    public string Kind { get; set; } = "charge";
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public Guid? InstallmentId { get; set; }
    public string PaymentMethod { get; set; } = FinancePaymentMethods.Cash;
    public string? Notes { get; set; }
}
public sealed record FinanceActor(Guid Id, string Name);
public sealed record FinanceOwnerListItem(Guid Id, string DisplayName);
public sealed record FinancePatientListItem(Guid Id, Guid OwnerId, string DisplayName);
public sealed record FinanceChoices(IReadOnlyList<FinanceOwnerListItem> Owners, IReadOnlyList<FinancePatientListItem> Patients);
public sealed record FinanceSummary(Guid Id, string CustomerName, string Description, DateOnly DueDate, decimal Total, decimal Paid, decimal Remaining, decimal Overdue, bool IsActive, Guid? PatientId);
public sealed record FinanceIndexModel(IReadOnlyList<FinanceSummary> Accounts, decimal TotalOutstanding, decimal TotalCollected, decimal TotalOverdue, string? Search, string? Status, bool ShowDeleted);
public sealed record FinanceDetailsModel(FinanceAccount Account, string? PatientName)
{
    public decimal Total => Account.Charges.Where(x => x.IsActive).Sum(x => x.Amount);
    public decimal Paid => Account.Receipts.Where(x => x.IsActive).Sum(x => x.Amount);
    public decimal Remaining => Total - Paid;
    public decimal PaidFor(Guid id) => Account.Receipts.Where(x => x.IsActive && x.FinanceInstallmentId == id).Sum(x => x.Amount);
    public decimal Unplanned => Remaining - Account.Installments.Where(x => x.IsActive).Sum(x => x.Amount - PaidFor(x.ID));
}
public enum FinanceError { NotFound, Validation, Conflict }
public sealed class FinanceException(FinanceError error, string message) : Exception(message)
{
    public FinanceError Error { get; } = error;
}
