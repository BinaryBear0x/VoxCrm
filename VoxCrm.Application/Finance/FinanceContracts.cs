using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.Finance;

public static class FinancePaymentMethods
{
    public const string Cash = "Nakit";
    public const string CreditCard = "Kredi Karti";
    public const string Transfer = "Havale/EFT";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Cash,
            CreditCard,
            Transfer,
        };
}

public sealed record CreateDebtRequest(
    Guid PetOwnerId,
    string Description,
    decimal Amount,
    DateTime DueDate);

public sealed record AddPaymentRequest(
    Guid DebtId,
    decimal Amount,
    string PaymentMethod,
    Guid ActorUserId);

public sealed record ReversePaymentRequest(
    Guid PaymentId,
    string Reason,
    Guid ActorUserId);

public sealed record CancelDebtRequest(
    Guid DebtId,
    string Reason,
    Guid ActorUserId);

public sealed record FinanceOwnerListItem(
    Guid Id,
    string DisplayName,
    string Phone);

public sealed record PaymentLedgerItem(
    Guid Id,
    PaymentEntryType EntryType,
    decimal Amount,
    DateTime PaymentDate,
    string PaymentMethod,
    string? Reason,
    Guid? ReversesPaymentId,
    bool IsReversed);

public sealed record DebtListItem(
    Guid Id,
    string OwnerName,
    string Description,
    decimal Amount,
    decimal PaidAmount,
    decimal RemainingAmount,
    DateTime DueDate,
    bool IsCollected,
    DateTime? CollectedAt,
    string? PaymentMethod,
    DateTime? CancelledAt,
    string? CancellationReason,
    IReadOnlyList<PaymentLedgerItem> Ledger);

public sealed record FinanceIndexModel(
    IReadOnlyList<DebtListItem> Debts,
    decimal TotalOutstanding,
    decimal TotalCollected,
    bool? CollectedFilter);

public enum FinanceError
{
    NotFound,
    Validation,
    Conflict,
}

public sealed class FinanceException : Exception
{
    public FinanceException(FinanceError error, string message) : base(message)
    {
        Error = error;
    }

    public FinanceError Error { get; }
}
