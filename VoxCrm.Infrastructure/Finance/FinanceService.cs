using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Finance;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
namespace VoxCrm.Infrastructure.Finance;

public sealed class FinanceService(VoxCrmDbContext db, ITenantService tenant) : IFinanceService
{
    public Task<DateOnly> GetTodayAsync(CancellationToken ct=default)=>ClinicCalendar.TodayAsync(db,ClinicId,ct);
    private Guid ClinicId => tenant.GetClinicId() is var id && id != Guid.Empty ? id : throw Missing();
    private IQueryable<FinanceAccount> Accounts => db.FinanceAccounts.IgnoreQueryFilters().Where(x => x.ClinicID == ClinicId);
    private IQueryable<FinanceAccount> Full => Accounts.Include(x => x.Charges).Include(x => x.Receipts).Include(x => x.Installments).Include(x => x.Changes).AsSplitQuery();
    public async Task<FinanceIndexModel> GetIndexAsync(string? search, string? status, bool showDeleted, CancellationToken ct = default)
    {
        var accounts = await Accounts.Include(x => x.Charges).Include(x => x.Receipts).Include(x => x.Installments).AsSplitQuery().AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var today=await ClinicCalendar.TodayAsync(db,ClinicId,ct);
        var all = accounts.Select(a=>Summarize(a,today)).ToList(); var active = all.Where(x => x.IsActive).ToList();
        var rows = all.Where(x => showDeleted || x.IsActive);
        if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(x => (x.CustomerName + " " + x.Description).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
        rows = status switch { "open" => rows.Where(x => x.IsActive && x.Remaining > 0), "paid" => rows.Where(x => x.IsActive && x.Total > 0 && x.Remaining == 0), "overdue" => rows.Where(x => x.IsActive && x.Overdue > 0), _ => rows };
        return new(rows.ToList(), active.Sum(x => x.Remaining), active.Sum(x => x.Paid), active.Sum(x => x.Overdue), search, status, showDeleted);
    }
    public async Task<FinanceChoices> GetChoicesAsync(CancellationToken ct = default)
    {
        var owners = await db.PetOwners.IgnoreQueryFilters().Where(x => x.ClinicID == ClinicId && x.IsActive).AsNoTracking().ToListAsync(ct);
        var patients = await db.PatientOwners.IgnoreQueryFilters().Include(x => x.Patient).Where(x => x.ClinicID == ClinicId && x.IsActive && x.Patient.IsActive).AsNoTracking().ToListAsync(ct);
        return new(owners.Select(x => new FinanceOwnerListItem(x.ID, (x.FirstName + " " + x.LastName).Trim())).OrderBy(x => x.DisplayName).ToList(), patients.Where(x => owners.Any(o => o.ID == x.PetOwnerId)).Select(x => new FinancePatientListItem(x.PatientId, x.PetOwnerId, x.Patient.Name ?? "İsimsiz hasta")).OrderBy(x => x.DisplayName).ToList());
    }
    public async Task<FinanceDetailsModel> GetDetailsAsync(Guid id, CancellationToken ct = default)
    {
        var a = await Full.AsNoTracking().SingleOrDefaultAsync(x => x.ID == id, ct) ?? throw Missing();
        var name = a.PatientId.HasValue ? await db.Patients.IgnoreQueryFilters().Where(x => x.ClinicID == ClinicId && x.ID == a.PatientId).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return new(a, name);
    }
    public async Task<Guid> CreateAsync(FinanceAccountInput input, FinanceActor actor, CancellationToken ct = default)
    {
        Actor(actor); Money(input.Amount); Date(input.DueDate); Date(input.ChargeDate);
        if (input.SubmissionId == Guid.Empty) throw Invalid("Kayıt anahtarı eksik. Sayfayı yenileyin.");
        await using var tx = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(ct) : null;
        var key = BitConverter.ToInt64(input.SubmissionId.ToByteArray()) ^ BitConverter.ToInt64(ClinicId.ToByteArray());
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
        var prior = await Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.SubmissionId == input.SubmissionId, ct);
        if (prior != null) return prior.ID;
        var a = new FinanceAccount { ClinicID = ClinicId, SubmissionId = input.SubmissionId, Description = Text(input.Description, 1000), DueDate = input.DueDate };
        await SetCustomer(a, input, ct);
        var c = new FinanceCharge { ClinicID = ClinicId, FinanceAccountId = a.ID, Description = a.Description, Amount = input.Amount, ChargeDate = input.ChargeDate };
        a.Charges.Add(c); db.FinanceAccounts.Add(a); Log(a, "Kayıt eklendi", a, null, Snapshot(a), actor); Log(a, "Borç eklendi", c, null, Snapshot(c), actor);
        await db.SaveChangesAsync(ct); if (tx != null) await tx.CommitAsync(ct); return a.ID;
    }
    public Task UpdateAsync(Guid id, FinanceAccountInput input, FinanceActor actor, CancellationToken ct = default) => Mutate(id, input.Revision, actor, async a =>
    {
        var before = Snapshot(a); Date(input.DueDate); await SetCustomer(a, input, ct); a.Description = Text(input.Description, 1000); a.DueDate = input.DueDate;
        Log(a, "Kayıt güncellendi", a, before, Snapshot(a), actor);
    }, ct);
    public Task DeleteAsync(Guid id, long revision, FinanceActor actor, CancellationToken ct = default) => Mutate(id, revision, actor, a =>
    {
        var before = Snapshot(a); Archive(a, actor); Log(a, "Kayıt silindi", a, before, Snapshot(a), actor); return Task.CompletedTask;
    }, ct);
    public Task SaveEntryAsync(FinanceEntryInput input, FinanceActor actor, CancellationToken ct = default) => Mutate(input.AccountId, input.Revision, actor, async a =>
    {
        Money(input.Amount); Date(input.Date); var existing = input.Id.HasValue;
        BaseEntity entry; object? before;
        switch (input.Kind)
        {
            case "charge":
                var c = existing ? a.Charges.SingleOrDefault(x => x.ID == input.Id && x.IsActive) ?? throw Missing() : new FinanceCharge { ClinicID = ClinicId, FinanceAccountId = a.ID };
                before = existing ? Snapshot(c) : null; c.Amount = input.Amount; c.Description = Text(input.Description, 1000); c.ChargeDate = input.Date;
                if (!existing) { a.Charges.Add(c); db.FinanceCharges.Add(c); } entry = c; break;
            case "installment":
                var i = existing ? a.Installments.SingleOrDefault(x => x.ID == input.Id && x.IsActive) ?? throw Missing() : new FinanceInstallment { ClinicID = ClinicId, FinanceAccountId = a.ID };
                before = existing ? Snapshot(i) : null; i.Amount = input.Amount; i.Description = Text(input.Description, 200); i.DueDate = input.Date;
                if (!existing) { a.Installments.Add(i); db.FinanceInstallments.Add(i); } entry = i; break;
            case "receipt":
                if (input.Date > await ClinicCalendar.TodayAsync(db,ClinicId,ct)) throw Invalid("Ödeme tarihi gelecekte olamaz. Gelecek ödeme için taksit ekleyin.");
                if (!FinancePaymentMethods.Allowed.Contains(input.PaymentMethod)) throw Invalid("Geçerli bir ödeme yöntemi seçin.");
                if (input.InstallmentId.HasValue && !a.Installments.Any(x => x.ID == input.InstallmentId && x.IsActive)) throw Invalid("Taksit bu kayda ait değil veya silinmiş.");
                var r = existing ? a.Receipts.SingleOrDefault(x => x.ID == input.Id && x.IsActive) ?? throw Missing() : new FinanceReceipt { ClinicID = ClinicId, FinanceAccountId = a.ID };
                before = existing ? Snapshot(r) : null; r.Amount = input.Amount; r.PaidDate = input.Date; r.PaymentMethod = input.PaymentMethod; r.Notes = OptionalText(input.Notes, 1000); r.FinanceInstallmentId = input.InstallmentId;
                if (!existing) { a.Receipts.Add(r); db.FinanceReceipts.Add(r); } entry = r; break;
            default: throw Invalid("Geçersiz kayıt türü.");
        }
        ValidateTotals(a); Log(a, Label(input.Kind) + (existing ? " güncellendi" : " eklendi"), entry, before, Snapshot(entry), actor); return;
    }, ct);
    public Task DeleteEntryAsync(Guid accountId, string kind, Guid id, long revision, FinanceActor actor, CancellationToken ct = default) => Mutate(accountId, revision, actor, a =>
    {
        BaseEntity e = kind switch { "charge" => a.Charges.SingleOrDefault(x => x.ID == id && x.IsActive) ?? throw Missing(), "installment" => a.Installments.SingleOrDefault(x => x.ID == id && x.IsActive) ?? throw Missing(), "receipt" => a.Receipts.SingleOrDefault(x => x.ID == id && x.IsActive) ?? throw Missing(), _ => throw Invalid("Geçersiz kayıt türü.") };
        var before = Snapshot(e);
        if (kind == "installment") foreach (var r in a.Receipts.Where(x => x.IsActive && x.FinanceInstallmentId == id))
        {
            var rb = Snapshot(r); r.FinanceInstallmentId = null; r.FinanceInstallment = null;
            Log(a, "Tahsilat taksit bağlantısı kaldırıldı", r, rb, Snapshot(r), actor);
        }
        Archive(e, actor); ValidateTotals(a); Log(a, Label(kind) + " silindi", e, before, Snapshot(e), actor); return Task.CompletedTask;
    }, ct);
    public Task GenerateInstallmentsAsync(Guid accountId, int count, DateOnly firstDate, long revision, FinanceActor actor, CancellationToken ct = default) => Mutate(accountId, revision, actor, a =>
    {
        Date(firstDate); if (count is < 1 or > 60) throw Invalid("Taksit sayısı 1 ile 60 arasında olmalıdır.");
        var amount = new FinanceDetailsModel(a, null).Unplanned;
        if (amount < count * 0.01m) throw Invalid("Taksitlere bölünecek yeterli plansız bakiye yok.");
        if (firstDate.AddMonths(count - 1).Year > 2100) throw Invalid("Taksit tarihleri 2100 yılını aşamaz.");
        var each = decimal.Floor(amount / count * 100m) / 100m;
        for (var n = 0; n < count; n++)
        {
            var i = new FinanceInstallment { ClinicID = ClinicId, FinanceAccountId = a.ID, Description = $"{n + 1}. taksit", DueDate = firstDate.AddMonths(n), Amount = n == count - 1 ? amount - each * (count - 1) : each };
            a.Installments.Add(i); db.FinanceInstallments.Add(i); Log(a, "Taksit eklendi", i, null, Snapshot(i), actor);
        }
        ValidateTotals(a); return Task.CompletedTask;
    }, ct);
    private async Task Mutate(Guid id, long revision, FinanceActor actor, Func<FinanceAccount, Task> command, CancellationToken ct)
    {
        Actor(actor); await using var tx = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var a = await db.FinanceAccounts.FromSqlInterpolated($"SELECT * FROM \"FinanceAccounts\" WHERE \"ID\" = {id} AND \"ClinicID\" = {ClinicId} FOR UPDATE").IgnoreQueryFilters().SingleOrDefaultAsync(ct) ?? throw Missing();
            if (!a.IsActive) throw new FinanceException(FinanceError.Conflict, "Bu kayıt silinmiş. Yalnızca geçmişi görüntülenebilir.");
            if (a.Revision != revision) throw new FinanceException(FinanceError.Conflict, "Kayıt başka bir işlemle değişti. Sayfayı yenileyip tekrar deneyin.");
            await db.Entry(a).Collection(x => x.Charges).Query().IgnoreQueryFilters().Where(x => x.ClinicID == ClinicId).LoadAsync(ct);
            await db.Entry(a).Collection(x => x.Installments).Query().IgnoreQueryFilters().Where(x => x.ClinicID == ClinicId).LoadAsync(ct);
            await db.Entry(a).Collection(x => x.Receipts).Query().IgnoreQueryFilters().Where(x => x.ClinicID == ClinicId).LoadAsync(ct);
            await command(a); a.Revision++; await db.SaveChangesAsync(ct); if (tx != null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct); db.ChangeTracker.Clear();
            throw new FinanceException(FinanceError.Conflict, "Kayıt değişti. Sayfayı yenileyip tekrar deneyin.");
        }
        catch { if (tx != null) await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); throw; }
    }
    private async Task SetCustomer(FinanceAccount a, FinanceAccountInput input, CancellationToken ct)
    {
        if (input.CustomerMode == "manual") { a.CustomerName = Text(input.CustomerName, 200); a.PetOwnerId = null; a.PatientId = null; return; }
        if (input.CustomerMode != "registered" || !input.PetOwnerId.HasValue) throw Invalid("Kayıtlı müşteri seçin veya manuel isim girin.");
        var owner = await db.PetOwners.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.ClinicID == ClinicId && x.IsActive && x.ID == input.PetOwnerId, ct) ?? throw Invalid("Müşteri bu klinikte bulunamadı.");
        if (input.PatientId.HasValue && !await db.PatientOwners.IgnoreQueryFilters().AnyAsync(x => x.ClinicID == ClinicId && x.IsActive && x.PetOwnerId == owner.ID && x.PatientId == input.PatientId && x.Patient.IsActive, ct)) throw Invalid("Hasta seçilen müşteriye ait değil.");
        a.PetOwnerId = owner.ID; a.PatientId = input.PatientId; a.CustomerName = (owner.FirstName + " " + owner.LastName).Trim();
    }
    private void Log(FinanceAccount a, string action, BaseEntity e, object? before, object? after, FinanceActor actor)
    {
        db.FinanceChanges.Add(new FinanceChange { ClinicID = ClinicId, FinanceAccountId = a.ID, ActorUserId = actor.Id, ActorName = actor.Name, Action = action, EntityType = e.GetType().Name, EntityId = e.ID, BeforeJson = before == null ? null : JsonSerializer.Serialize(before), AfterJson = after == null ? null : JsonSerializer.Serialize(after) });
        db.SystemAuditLogs.Add(new SystemAuditLog { ClinicId = ClinicId, ActorUserId = actor.Id, ActorUserName = actor.Name, Category = "Finance", Action = "Finance." + e.GetType().Name, Message = action, EntityType = e.GetType().Name, EntityId = e.ID.ToString() });
    }
    private static object Snapshot(BaseEntity e) => e switch {
        FinanceAccount a => new { a.CustomerName, a.PetOwnerId, a.PatientId, a.Description, a.DueDate, a.IsActive },
        FinanceCharge c => new { c.Description, c.Amount, c.ChargeDate, c.IsActive },
        FinanceInstallment i => new { i.Description, i.Amount, i.DueDate, i.IsActive },
        FinanceReceipt r => new { r.Amount, r.PaidDate, r.PaymentMethod, r.FinanceInstallmentId, r.Notes, r.IsActive },
        _ => throw new InvalidOperationException("Unknown finance entity") };
    private static void Archive(BaseEntity e, FinanceActor actor) { e.IsActive = false; e.ArchivedAt = DateTime.UtcNow; e.ArchivedByUserId = actor.Id; }
    private static string Label(string kind) => kind switch { "charge" => "Borç kalemi", "receipt" => "Tahsilat", _ => "Taksit" };
    private static void ValidateTotals(FinanceAccount a)
    {
        var m = new FinanceDetailsModel(a, null);
        if (m.Paid > m.Total) throw Invalid($"Tahsilat toplamı ({m.Paid:N2} TL) borç toplamını ({m.Total:N2} TL) aşamaz. Tutarı düzeltin; borcu düşürüyorsanız önce ilgili tahsilatı düzenleyin veya silin.");
        foreach (var i in a.Installments.Where(x => x.IsActive)) if (m.PaidFor(i.ID) > i.Amount) throw Invalid("Tahsilat, bağlı olduğu taksit tutarını aşamaz.");
        if (m.Unplanned < 0) throw Invalid("Taksit planı ve taksite bağlı olmayan tahsilatlar borcu aşıyor. Taksitleri düzenleyin veya tahsilatı ilgili taksite bağlayın.");
    }
    public static FinanceSummary Summarize(FinanceAccount a, DateOnly today)
    {
        var m = new FinanceDetailsModel(a, null);
        var overdue = a.Installments.Where(x => x.IsActive && x.DueDate < today).Sum(x => x.Amount - m.PaidFor(x.ID));
        if (a.DueDate < today) overdue += m.Unplanned;
        return new(a.ID, a.CustomerName, a.Description, a.DueDate, m.Total, m.Paid, m.Remaining, overdue, a.IsActive, a.PatientId);
    }
    private static void Actor(FinanceActor a) { if (a.Id == Guid.Empty) throw Invalid("İşlemi yapan kullanıcı belirlenemedi."); }
    private static void Money(decimal n) { if (n <= 0 || n > 9999999999999999.99m || decimal.Round(n, 2) != n) throw Invalid("Tutar pozitif ve en fazla iki ondalıklı olmalıdır."); }
    private static void Date(DateOnly d) { if (d.Year is < 2000 or > 2100) throw Invalid("2000–2100 arasında geçerli bir tarih girin."); }
    private static string Text(string? s, int max) => !string.IsNullOrWhiteSpace(s) && s.Trim().Length <= max ? s.Trim() : throw Invalid($"Zorunlu metni doldurun (en fazla {max} karakter).");
    private static string? OptionalText(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : Text(s, max);
    private static FinanceException Missing() => new(FinanceError.NotFound, "Finans kaydı bulunamadı.");
    private static FinanceException Invalid(string message) => new(FinanceError.Validation, message);
}
