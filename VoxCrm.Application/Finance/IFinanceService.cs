namespace VoxCrm.Application.Finance;

public interface IFinanceService
{
    Task<FinanceIndexModel> GetIndexAsync(bool? collected, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinanceOwnerListItem>> GetOwnersAsync(CancellationToken cancellationToken = default);
    Task<Guid> CreateDebtAsync(CreateDebtRequest request, CancellationToken cancellationToken = default);
    Task<Guid> AddPaymentAsync(AddPaymentRequest request, CancellationToken cancellationToken = default);
    Task<Guid> ReversePaymentAsync(ReversePaymentRequest request, CancellationToken cancellationToken = default);
    Task CancelDebtAsync(CancelDebtRequest request, CancellationToken cancellationToken = default);
}
