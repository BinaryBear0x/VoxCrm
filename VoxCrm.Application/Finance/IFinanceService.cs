namespace VoxCrm.Application.Finance;
public interface IFinanceService
{
    Task<DateOnly> GetTodayAsync(CancellationToken ct = default);
    Task<FinanceIndexModel> GetIndexAsync(string? search, string? status, bool showDeleted, CancellationToken ct = default);
    Task<FinanceChoices> GetChoicesAsync(CancellationToken ct = default);
    Task<FinanceDetailsModel> GetDetailsAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateAsync(FinanceAccountInput input, FinanceActor actor, CancellationToken ct = default);
    Task UpdateAsync(Guid id, FinanceAccountInput input, FinanceActor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid id, long revision, FinanceActor actor, CancellationToken ct = default);
    Task SaveEntryAsync(FinanceEntryInput input, FinanceActor actor, CancellationToken ct = default);
    Task DeleteEntryAsync(Guid accountId, string kind, Guid id, long revision, FinanceActor actor, CancellationToken ct = default);
    Task GenerateInstallmentsAsync(Guid accountId, int count, DateOnly firstDate, long revision, FinanceActor actor, CancellationToken ct = default);
}
