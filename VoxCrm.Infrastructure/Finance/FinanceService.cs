using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Finance;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.Finance;

public sealed class FinanceService : IFinanceService
{
    private readonly VoxCrmDbContext _context;
    private readonly ITenantService _tenant;

    public FinanceService(VoxCrmDbContext context, ITenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<FinanceIndexModel> GetIndexAsync(bool? collected, CancellationToken cancellationToken = default)
    {
        var debts = await TenantDebts()
            .Include(debt => debt.PetOwner)
            .Include(debt => debt.Payments)
            .AsNoTracking()
            .OrderByDescending(debt => debt.DueDate)
            .ToListAsync(cancellationToken);
        var allItems = debts.Select(ToListItem).ToList();
        var items = collected.HasValue
            ? allItems.Where(item => item.IsCollected == collected.Value).ToList()
            : allItems;
        var totalOutstanding = allItems.Where(item => item.CancelledAt == null).Sum(item => item.RemainingAmount);
        var totalCollected = allItems.Sum(item => item.PaidAmount);

        return new FinanceIndexModel(items, totalOutstanding, totalCollected, collected);
    }

    public async Task<IReadOnlyList<FinanceOwnerListItem>> GetOwnersAsync(CancellationToken cancellationToken = default) =>
        await _context.PetOwners
            .IgnoreQueryFilters()
            .Where(owner => owner.ClinicID == ClinicId && owner.IsActive)
            .OrderBy(owner => owner.FirstName)
            .ThenBy(owner => owner.LastName)
            .Select(owner => new FinanceOwnerListItem(
                owner.ID,
                ((owner.FirstName ?? string.Empty) + " " + (owner.LastName ?? string.Empty)).Trim(),
                owner.Phone ?? string.Empty))
            .ToListAsync(cancellationToken);

    public async Task<Guid> CreateDebtAsync(CreateDebtRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePositiveAmount(request.Amount, "Borç tutarı sıfırdan büyük olmalıdır.");
        if (string.IsNullOrWhiteSpace(request.Description))
            throw Validation("Borç açıklaması zorunludur.");

        var ownerExists = await _context.PetOwners
            .IgnoreQueryFilters()
            .AnyAsync(owner => owner.ID == request.PetOwnerId && owner.ClinicID == ClinicId && owner.IsActive, cancellationToken);
        if (!ownerExists)
            throw Validation("Geçerli ve aktif bir müşteri seçin.");

        var debt = new Debt
        {
            ClinicID = ClinicId,
            PetOwnerId = request.PetOwnerId,
            Description = request.Description.Trim(),
            Amount = request.Amount,
            DueDate = request.DueDate,
        };
        _context.Borçlar.Add(debt);
        await _context.SaveChangesAsync(cancellationToken);
        return debt.ID;
    }

    public async Task<Guid> AddPaymentAsync(AddPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePositiveAmount(request.Amount, "Tahsilat tutarı sıfırdan büyük olmalıdır.");
        if (!FinancePaymentMethods.Allowed.Contains(request.PaymentMethod))
            throw Validation("Geçersiz ödeme yöntemi.");

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var debt = await LockDebtAsync(request.DebtId, cancellationToken);
        EnsurePayable(debt);

        var paidAmount = NetPaid(debt.Payments);
        var remainingAmount = debt.Amount - paidAmount;
        if (request.Amount > remainingAmount)
            throw Validation("Tahsilat tutarı kalan borçtan büyük olamaz.");

        var now = DateTime.UtcNow;
        var payment = new Payment
        {
            ClinicID = ClinicId,
            DebtId = debt.ID,
            EntryType = PaymentEntryType.Payment,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            PaymentDate = now,
            ActorUserId = request.ActorUserId,
        };
        _context.Payments.Add(payment);

        if (paidAmount + request.Amount == debt.Amount)
        {
            debt.IsCollected = true;
            debt.CollectedAt = now;
            debt.PaymentMethod = request.PaymentMethod;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return payment.ID;
    }

    public async Task<Guid> ReversePaymentAsync(ReversePaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw Validation("Ters kayıt nedeni zorunludur.");

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var paymentHeader = await _context.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(payment => payment.ID == request.PaymentId && payment.ClinicID == ClinicId, cancellationToken);
        if (paymentHeader == null || !paymentHeader.IsActive || paymentHeader.EntryType != PaymentEntryType.Payment)
            throw NotFound("Tahsilat bulunamadı.");

        var debt = await LockDebtAsync(paymentHeader.DebtId, cancellationToken);
        var payment = debt.Payments.SingleOrDefault(item => item.ID == request.PaymentId);
        if (payment == null || !payment.IsActive || payment.EntryType != PaymentEntryType.Payment)
            throw NotFound("Tahsilat bulunamadı.");
        if (debt.Payments.Any(item => item.EntryType == PaymentEntryType.Reversal && item.ReversesPaymentId == payment.ID))
            throw Conflict("Bu tahsilat daha önce ters kaydedilmiş.");

        var reversal = new Payment
        {
            ClinicID = ClinicId,
            DebtId = debt.ID,
            EntryType = PaymentEntryType.Reversal,
            Amount = payment.Amount,
            PaymentMethod = payment.PaymentMethod,
            PaymentDate = DateTime.UtcNow,
            ReversesPaymentId = payment.ID,
            Reason = request.Reason.Trim(),
            ActorUserId = request.ActorUserId,
        };
        _context.Payments.Add(reversal);

        debt.IsCollected = false;
        debt.CollectedAt = null;
        debt.PaymentMethod = null;

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return reversal.ID;
    }

    public async Task CancelDebtAsync(CancelDebtRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw Validation("İptal nedeni zorunludur.");

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var debt = await LockDebtAsync(request.DebtId, cancellationToken);
        if (!debt.IsActive)
            throw NotFound("Borç bulunamadı.");
        if (debt.CancelledAt.HasValue)
            throw Conflict("Borç daha önce iptal edilmiş.");
        if (NetPaid(debt.Payments) != 0)
            throw Conflict("Tahsilatı bulunan borç iptal edilemez; önce tahsilatları ters kaydedin.");

        debt.CancelledAt = DateTime.UtcNow;
        debt.CancelledByUserId = request.ActorUserId;
        debt.CancellationReason = request.Reason.Trim();
        debt.IsCollected = false;
        debt.CollectedAt = null;
        debt.PaymentMethod = null;

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Debt> LockDebtAsync(Guid debtId, CancellationToken cancellationToken)
    {
        var debt = await _context.Borçlar
            .FromSqlInterpolated($"SELECT * FROM \"Borçlar\" WHERE \"ID\" = {debtId} AND \"ClinicID\" = {ClinicId} FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);
        if (debt == null)
            throw NotFound("Borç bulunamadı.");

        await _context.Entry(debt)
            .Collection(item => item.Payments)
            .Query()
            .IgnoreQueryFilters()
            .Where(payment => payment.ClinicID == ClinicId)
            .LoadAsync(cancellationToken);
        return debt;
    }

    private static void EnsurePayable(Debt debt)
    {
        if (!debt.IsActive)
            throw NotFound("Borç bulunamadı.");
        if (debt.CancelledAt.HasValue)
            throw Conflict("İptal edilmiş borca tahsilat eklenemez.");
        if (debt.IsCollected)
            throw Conflict("Borç zaten tamamen tahsil edilmiş.");
    }

    private IQueryable<Debt> TenantDebts() =>
        _context.Borçlar.IgnoreQueryFilters().Where(debt => debt.ClinicID == ClinicId && debt.IsActive);

    private static DebtListItem ToListItem(Debt debt)
    {
        var reversedPaymentIds = debt.Payments
            .Where(payment => payment.EntryType == PaymentEntryType.Reversal && payment.ReversesPaymentId.HasValue)
            .Select(payment => payment.ReversesPaymentId!.Value)
            .ToHashSet();
        var paid = NetPaid(debt.Payments);
        var ledger = debt.Payments
            .OrderByDescending(payment => payment.PaymentDate)
            .Select(payment => new PaymentLedgerItem(
                payment.ID,
                payment.EntryType,
                payment.Amount,
                payment.PaymentDate,
                payment.PaymentMethod,
                payment.Reason,
                payment.ReversesPaymentId,
                payment.EntryType == PaymentEntryType.Payment && reversedPaymentIds.Contains(payment.ID)))
            .ToList();

        return new DebtListItem(
            debt.ID,
            $"{debt.PetOwner.FirstName} {debt.PetOwner.LastName}".Trim(),
            debt.Description,
            debt.Amount,
            paid,
            Math.Max(0, debt.Amount - paid),
            debt.DueDate,
            debt.IsCollected,
            debt.CollectedAt,
            debt.PaymentMethod,
            debt.CancelledAt,
            debt.CancellationReason,
            ledger);
    }

    private static decimal NetPaid(IEnumerable<Payment> payments) =>
        payments.Where(payment => payment.IsActive).Sum(payment =>
            payment.EntryType == PaymentEntryType.Payment ? payment.Amount : -payment.Amount);

    private static void ValidatePositiveAmount(decimal amount, string message)
    {
        if (amount <= 0)
            throw Validation(message);
    }

    private Guid ClinicId
    {
        get
        {
            var clinicId = _tenant.GetClinicId();
            return clinicId != Guid.Empty ? clinicId : throw NotFound("Klinik bulunamadı.");
        }
    }

    private static FinanceException Validation(string message) => new(FinanceError.Validation, message);
    private static FinanceException NotFound(string message) => new(FinanceError.NotFound, message);
    private static FinanceException Conflict(string message) => new(FinanceError.Conflict, message);
}
