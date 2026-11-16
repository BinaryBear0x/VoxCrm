using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VoxCrm.Application.Workday;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.Workday;

public sealed class WorkdayService(VoxCrmDbContext db, ITenantService tenant, DbContextOptions<VoxCrmDbContext>? options = null) : IWorkdayService
{
    private sealed record CardTenant(Guid Id) : ITenantService { public Guid GetClinicId() => Id; public bool IsSystemContext => false; }
    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw Invalid("Kayıt eşzamanlı olarak değişti. Sayfayı yenileyip tekrar deneyin."); }
    }
    private Guid ClinicId => tenant.GetClinicId() is var id && id != Guid.Empty ? id : throw Missing();
    private static WorkdayException Missing() => new("Kayıt bulunamadı veya bu kliniğe ait değil.", true);
    private static WorkdayException Invalid(string text) => new(text);
    private static string Required(string? text, int length) => !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= length ? text.Trim() : throw Invalid($"Zorunlu alanları doldurun; en fazla {length} karakter kullanın.");
    private static void Revision(long actual, long expected) { if (actual != expected) throw Invalid("Kayıt başka bir işlemde değişti. Sayfayı yenileyip tekrar deneyin."); }
    private async Task<ApplicationUser> Actor(Guid id, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.ClinicId == ClinicId && !x.LoginDisabled, ct);
        if (user == null || user.LockoutEnd > DateTimeOffset.UtcNow || !await db.UserRoles.Join(db.Roles, x => x.RoleId, x => x.Id, (ur, r) => new { ur.UserId, r.Name }).AnyAsync(x => x.UserId == id && x.Name == "Clinic", ct)
            || !await db.Clinics.AnyAsync(x => x.ID == ClinicId && x.IsActive && x.Dealer.IsActive, ct)) throw Missing();
        return user;
    }
    private async Task<(DateOnly Today, TimeZoneInfo Zone)> Clock(CancellationToken ct)
    {
        var zoneId = await db.Clinics.Where(x => x.ID == ClinicId).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct) ?? "Europe/Istanbul";
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId); } catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"); }
        return (DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone)), zone);
    }
    private void Audit(string action, Guid entityId, Guid? actor, string? message = null) => db.SystemAuditLogs.Add(new SystemAuditLog
    { ClinicId = entityId == Guid.Empty ? null : tenant.GetClinicId() == Guid.Empty ? null : tenant.GetClinicId(), Category = "Workday", Action = action, EntityType = "ClinicWorkday", EntityId = entityId.ToString(), ActorUserId = actor, Message = message ?? action });

    public async Task<WorkdayPage> GetAsync(DateOnly? date, CancellationToken ct = default)
    {
        var clock = await Clock(ct); var day = date ?? clock.Today;
        if (day.Year is < 2000 or > 2100) throw Invalid("Geçerli bir gün seçin.");
        var start = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), clock.Zone);
        var end = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), clock.Zone);
        var tasks = await db.ClinicTasks.AsNoTracking().Where(x => x.ClinicID == ClinicId && x.IsActive && (x.DueDate == day || (x.DueDate < day && x.Status == "Open"))).OrderByDescending(x => x.Important).ThenBy(x => x.DueDate).ToListAsync(ct);
        var appointments = await db.Appointments.AsNoTracking().Include(x => x.Patient).Where(x => x.ClinicID == ClinicId && x.IsActive && x.ScheduledAt >= start && x.ScheduledAt < end).OrderBy(x => x.ScheduledAt).ToListAsync(ct);
        var vaccines = await db.VaccinationRecords.AsNoTracking().Include(x => x.Patient).Include(x => x.VaccineType).Where(x => x.ClinicID == ClinicId && x.IsActive && x.Patient.IsActive && !db.VaccinationRecords.Any(other => other.ClinicID == x.ClinicID && other.IsActive && other.PatientId == x.PatientId && other.VaccineTypeId == x.VaccineTypeId && other.AdministeredDate.Date > x.AdministeredDate.Date) && x.NextDueDate >= start.Date.AddDays(-30) && x.NextDueDate < day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).OrderBy(x => x.NextDueDate).ToListAsync(ct);
        var exams = await db.Muayeneler.AsNoTracking().Include(x => x.Patient).Where(x => x.ClinicID == ClinicId && x.IsActive && x.CreatedAt >= start && x.CreatedAt < end).ToListAsync(ct);
        var issues = new List<ClosingIssue>();
        foreach (var e in exams)
        {
            if (e.BillingReviewedAt == null || (e.ReviewedFinanceAccountId.HasValue && !await db.FinanceAccounts.AnyAsync(x => x.ID == e.ReviewedFinanceAccountId && x.ClinicID == ClinicId && x.IsActive, ct))) issues.Add(new("billing:" + e.ID, e.Patient.Name + " — hesap kontrolü yapılmamış", "Bu uyarı borç olduğunu göstermez. Mevcut tahsilat hesabını ilişkilendirin veya ücretsiz/kapsam içi olduğunu gerekçesiyle işaretleyin.", "/Workday/Care/" + e.ID));
            if (string.IsNullOrWhiteSpace(e.Assessment) || string.IsNullOrWhiteSpace(e.Plan)) issues.Add(new("notes:" + e.ID, e.Patient.Name + " — muayene notu eksik", "Değerlendirme veya plan alanı boş. Kaydı kontrol edin.", "/Muayene/Edit/" + e.ID));
        }
        foreach (var a in appointments.Where(x => x.Status == "Tamamlandı"))
            if (!await db.Muayeneler.AnyAsync(x => x.ClinicID == ClinicId && x.IsActive && x.AppointmentId == a.ID, ct)) issues.Add(new("appointment:" + a.ID, (a.Patient?.Name ?? a.GuestName ?? "Randevu") + " — tamamlandı, muayene bağlantısı yok", "Muayene yapıldıysa kaydını ve randevu bağlantısını kontrol edin; her randevu muayene gerektirmeyebilir.", "/Appointment/Edit/" + a.ID));
        var cards = await db.CareCards.AsNoTracking().Where(x => x.ClinicID == ClinicId && x.State == "Published" && x.FollowUpDate == day).ToListAsync(ct);
        foreach (var card in cards)
        {
            var exam = await db.Muayeneler.AsNoTracking().SingleOrDefaultAsync(x => x.ClinicID == ClinicId && x.ID == card.ExaminationId && x.IsActive, ct);
            if (exam != null && !await db.Appointments.AnyAsync(x => x.ClinicID == ClinicId && x.PatientId == exam.PatientId && x.IsActive && x.Status != "İptal" && x.ScheduledAt >= start && x.ScheduledAt < end, ct))
                issues.Add(new("followup:" + card.ID, "Bakım kartındaki kontrol için randevu yok", "Kontrol tarihi bugün. Hasta sahibiyle görüşüldüğünü veya randevu planını kontrol edin.", "/Workday/Care/" + card.ExaminationId));
        }
        foreach (var t in tasks.Where(x => x.Status == "Open")) issues.Add(new("task:" + t.ID, t.Title, "Tamamlanmamış görev" + (t.DueDate < day ? " — gecikmiş." : "."), "/Workday/Task/" + t.ID));
        var duplicates = await db.VaccinationRecords.AsNoTracking().Where(x => x.ClinicID == ClinicId && x.IsActive).GroupBy(x => new { x.PatientId, x.VaccineTypeId, Date = x.AdministeredDate.Date }).Where(g => g.Count() > 1 && g.Any(x => x.CreatedAt >= start && x.CreatedAt < end)).Select(g => g.Key).ToListAsync(ct);
        foreach (var d in duplicates) issues.Add(new($"vaccine:{d.PatientId}:{d.VaccineTypeId}:{d.Date:yyyyMMdd}", "Olası mükerrer aşı kaydı", "Aynı hayvan ve aşı için aynı uygulama gününde birden fazla aktif kayıt var. Otomatik silinmez; kayıtları inceleyin.", "/Vaccination"));
        var acknowledgements = await db.ClosingAcknowledgements.AsNoTracking().Where(x => x.ClinicID == ClinicId && x.Date == day).ToListAsync(ct);
        issues = issues.Select(x => x with { Acknowledgement = acknowledgements.FirstOrDefault(a => a.IssueKey == x.Key)?.Note }).ToList();
        var patientRows = await db.Patients.AsNoTracking().Include(x => x.Owners).ThenInclude(x => x.PetOwner).Where(x => x.ClinicID == ClinicId && x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
        var patients = patientRows.Select(x => { var owner = x.Owners.FirstOrDefault(o => o.IsActive && o.IsPrimaryOwner && o.PetOwner.IsActive)?.PetOwner; return new WorkdayChoice(x.ID, x.Name + (owner == null ? " — Sahip belirtilmedi" : " — " + owner.FirstName + " " + owner.LastName + " — " + owner.Phone)); }).ToList();
        var staff = await db.Users.AsNoTracking().Where(x => x.ClinicId == ClinicId && !x.LoginDisabled).OrderBy(x => x.FirstName).Select(x => new WorkdayChoice(x.Id, x.FirstName + " " + x.LastName + " — " + x.Email)).ToListAsync(ct);
        return new(day, clock.Zone.Id, tasks, appointments, vaccines, issues, patients, staff);
    }

    public async Task SaveTaskAsync(TaskInput i, Guid actor, CancellationToken ct = default)
    {
        await Actor(actor, ct); var title = Required(i.Title, 240);
        if (i.DueDate.Year is < 2000 or > 2100 || i.Notes?.Length > 4000 || i.SubmissionId == Guid.Empty) throw Invalid("Görev tarihi veya notu geçersiz.");
        if (i.PatientId.HasValue && !await db.Patients.AnyAsync(x => x.ID == i.PatientId && x.ClinicID == ClinicId && x.IsActive, ct)) throw Missing();
        if (i.AssignedUserId.HasValue && !await db.Users.AnyAsync(x => x.Id == i.AssignedUserId && x.ClinicId == ClinicId && !x.LoginDisabled, ct)) throw Missing();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({(i.Id ?? i.SubmissionId).ToString()}, 0))", ct);
        ClinicTask task;
        if (i.Id.HasValue) { task = await db.ClinicTasks.SingleOrDefaultAsync(x => x.ID == i.Id && x.ClinicID == ClinicId, ct) ?? throw Missing(); Revision(task.Revision, i.Revision); }
        else
        {
            if (await db.ClinicTasks.AnyAsync(x => x.ClinicID == ClinicId && x.SubmissionId == i.SubmissionId, ct)) return;
            task = new ClinicTask { ClinicID = ClinicId, SubmissionId = i.SubmissionId, CreatedBy = actor }; db.Add(task);
        }
        task.Title = title; task.Notes = i.Notes?.Trim(); task.DueDate = i.DueDate; task.PatientId = i.PatientId; task.AssignedUserId = i.AssignedUserId; task.Important = i.Important; task.Revision++;
        Audit(i.Id.HasValue ? "Task.Updated" : "Task.Created", task.ID, actor);
        await Save(ct); await tx.CommitAsync(ct);
    }
    public Task<ClinicTask?> GetTaskAsync(Guid id, CancellationToken ct = default) => db.ClinicTasks.AsNoTracking().SingleOrDefaultAsync(x => x.ID == id && x.ClinicID == ClinicId, ct);

    public async Task SetTaskStatusAsync(Guid id, long revision, string status, Guid actor, CancellationToken ct = default)
    {
        await Actor(actor, ct); if (status is not ("Open" or "Done" or "Cancelled")) throw Invalid("Geçersiz görev durumu.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 0))", ct);
        var task = await db.ClinicTasks.SingleOrDefaultAsync(x => x.ID == id && x.ClinicID == ClinicId, ct) ?? throw Missing(); Revision(task.Revision, revision);
        task.Status = status; task.Revision++; task.CompletedAt = status == "Done" ? DateTime.UtcNow : null; task.CompletedBy = status == "Done" ? actor : null;
        Audit("Task." + status, id, actor); await Save(ct); await tx.CommitAsync(ct);
    }

    private async Task<Muayene> Examination(Guid id, CancellationToken ct) => await db.Muayeneler.Include(x => x.Patient).ThenInclude(x => x.Owners).ThenInclude(x => x.PetOwner)
        .SingleOrDefaultAsync(x => x.ID == id && x.ClinicID == ClinicId && x.IsActive && x.Patient.IsActive, ct) ?? throw Missing();
    private static PetOwner? Recipient(Muayene e) => e.Patient.Owners.FirstOrDefault(x => x.IsActive && x.IsPrimaryOwner && x.PetOwner.IsActive)?.PetOwner;
    private static string Snapshot(PetOwner owner, Patient patient) => Hash($"{owner.ID}|{owner.FirstName}|{owner.LastName}|{owner.Phone}|{patient.ID}|{patient.Name}");
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public async Task<CareEditor> GetCareEditorAsync(Guid examinationId, Guid actor, CancellationToken ct = default)
    {
        var user = await Actor(actor, ct); var e = await Examination(examinationId, ct);
        var ownerIds = e.Patient.Owners.Where(x => x.IsActive).Select(x => x.PetOwnerId).ToList();
        var accountRows = await db.FinanceAccounts.AsNoTracking().Where(x => x.ClinicID == ClinicId && x.IsActive && (x.PatientId == e.PatientId || (x.PatientId == null && x.PetOwnerId != null && ownerIds.Contains(x.PetOwnerId.Value)))).ToListAsync(ct);
        var accounts = accountRows.Select(x => new WorkdayChoice(x.ID, x.CustomerName + " — " + x.Description)).ToList();
        return new(e, await db.CareCards.AsNoTracking().SingleOrDefaultAsync(x => x.ClinicID == ClinicId && x.ExaminationId == examinationId, ct), Recipient(e), accounts, user.CanApproveCareCards);
    }

    public async Task SaveCareAsync(CareInput i, Guid actor, CancellationToken ct = default)
    {
        await Actor(actor, ct); var e = await Examination(i.ExaminationId, ct); var recipient = Recipient(e) ?? throw Invalid("Önce hastaya aktif birincil sahip bağlayın.");
        var today = (await Clock(ct)).Today;
        if (i.FollowUpDate < today || i.FollowUpDate?.Year > 2100) throw Invalid("Kontrol tarihi geçmişte olamaz.");
        var actions = Required(i.TodayActions, 4000); var steps = Required(i.HomeSteps, 8000); var call = Required(i.CallInstructions, 4000);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockCard(i.ExaminationId, ct);
        var card = await db.CareCards.SingleOrDefaultAsync(x => x.ClinicID == ClinicId && x.ExaminationId == i.ExaminationId, ct);
        if (card == null) { if (i.Revision != 0) throw Missing(); card = new CareCard { ClinicID = ClinicId, ExaminationId = e.ID }; db.Add(card); }
        else Revision(card.Revision, i.Revision);
        card.TodayActions = actions; card.HomeSteps = steps; card.CallInstructions = call; card.FollowUpDate = i.FollowUpDate; card.PetOwnerId = recipient.ID;
        card.State = "Draft"; card.TokenHash = null; card.ExpiresAt = null; card.ApprovedAt = null; card.ApprovedBy = null; card.Revision++;
        card.OwnerResponse = null; card.RespondedAt = null;
        var pending = await db.ClinicTasks.SingleOrDefaultAsync(x => x.ClinicID == ClinicId && x.CareCardId == card.ID && x.Kind == "CareFollowUp", ct);
        if (pending?.Status == "Open") { pending.Status = "Cancelled"; pending.Revision++; }
        Audit("Care.DraftSaved", card.ID, actor); await Save(ct); await tx.CommitAsync(ct);
    }

    private Task LockCard(Guid id, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 0))", ct);
    public async Task<string> PublishCareAsync(Guid examinationId, long revision, Guid actor, bool veterinarianConfirmed, CancellationToken ct = default)
    {
        var user = await Actor(actor, ct);
        if (!user.CanApproveCareCards || !veterinarianConfirmed) throw Invalid("Veteriner onay yetkisi ve içerik onayı gerekli. Yetkiyi bayi Kullanıcılar / Güvenlik ekranından verebilir.");
        var e = await Examination(examinationId, ct); var owner = Recipient(e) ?? throw Invalid("Aktif birincil sahip bulunamadı.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockCard(examinationId, ct);
        var card = await db.CareCards.SingleOrDefaultAsync(x => x.ExaminationId == examinationId && x.ClinicID == ClinicId, ct) ?? throw Missing(); Revision(card.Revision, revision);
        if (card.PetOwnerId != owner.ID) throw Invalid("Hasta sahibi değişti. Taslağı yeniden kontrol edip kaydedin.");
        Required(card.TodayActions, 4000); Required(card.HomeSteps, 8000); Required(card.CallInstructions, 4000);
        var today = (await Clock(ct)).Today; if (card.FollowUpDate < today) throw Invalid("Kontrol tarihi geçmişte. Taslağı güncelleyin.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        card.State = "Published"; card.ApprovedAt = DateTime.UtcNow; card.ApprovedBy = actor; card.RecipientSnapshot = Snapshot(owner, e.Patient);
        card.TokenHash = Hash(token); card.ExpiresAt = DateTime.UtcNow.AddDays(14); card.Revision++;
        if (card.FollowUpDate.HasValue) await UpsertCareTask(card, e.PatientId, "CareFollowUp", "Bakım kartı kontrolü — " + e.Patient.Name, card.FollowUpDate.Value, actor, ct);
        Audit("Care.ApprovedAndPublished", card.ID, actor); await Save(ct); await tx.CommitAsync(ct); return token;
    }

    public async Task RevokeCareAsync(Guid examinationId, long revision, Guid actor, CancellationToken ct = default)
    {
        await Actor(actor, ct); await using var tx = await db.Database.BeginTransactionAsync(ct); await LockCard(examinationId, ct);
        var card = await db.CareCards.SingleOrDefaultAsync(x => x.ExaminationId == examinationId && x.ClinicID == ClinicId, ct) ?? throw Missing(); Revision(card.Revision, revision);
        card.State = "Revoked"; card.TokenHash = null; card.ExpiresAt = null; card.Revision++;
        Audit("Care.LinkRevoked", card.ID, actor); await Save(ct); await tx.CommitAsync(ct);
    }

    private async Task<(CareCard Card, Muayene Exam, Clinic Clinic)?> PublicCard(string token, CancellationToken ct)
    {
        if (token.Length != 64 || token.Any(x => !Uri.IsHexDigit(x))) return null;
        var hash = Hash(token);
        var c = await db.CareCards.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash && x.IsActive && x.State == "Published" && x.ExpiresAt > DateTime.UtcNow, ct);
        if (c == null) return null;
        var clinic = await db.Clinics.AsNoTracking().SingleOrDefaultAsync(x => x.ID == c.ClinicID && x.IsActive && x.Dealer.IsActive, ct);
        var e = await db.Muayeneler.IgnoreQueryFilters().AsNoTracking().Include(x => x.Patient).ThenInclude(x => x.Owners).ThenInclude(x => x.PetOwner)
            .SingleOrDefaultAsync(x => x.ID == c.ExaminationId && x.ClinicID == c.ClinicID && x.IsActive && x.Patient.IsActive, ct);
        var approver = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == c.ApprovedBy && x.ClinicId == c.ClinicID && x.CanApproveCareCards && !x.LoginDisabled, ct);
        var owner = e == null ? null : Recipient(e);
        return clinic == null || (clinic.EnabledModules & 2) == 0 || e == null || approver == null || owner == null || owner.ID != c.PetOwnerId || c.RecipientSnapshot != Snapshot(owner, e.Patient) ? null : (c, e, clinic);
    }
    public async Task<PublicCare?> ReadPublicAsync(string token, CancellationToken ct = default)
    {
        var found = await PublicCard(token, ct); if (found == null) return null; var (c, e, clinic) = found.Value;
        return new(clinic.Name, clinic.phone, e.Patient.Name ?? "Hasta", c.TodayActions, c.HomeSteps, c.CallInstructions, c.FollowUpDate, c.OwnerResponse);
    }
    public async Task<bool> RespondAsync(string token, string response, CancellationToken ct = default)
    {
        if (response is not ("Applied" or "ContactRequested")) return false;
        var initial = await PublicCard(token, ct); if (initial == null) return false;
        if (tenant.GetClinicId() != initial.Value.Card.ClinicID)
        {
            // Only a validated, live capability link may establish this narrowly bound write scope.
            var boundTenant = new CardTenant(initial.Value.Card.ClinicID);
            await using var boundDb = new VoxCrmDbContext(options ?? (DbContextOptions<VoxCrmDbContext>)db.GetService<IDbContextOptions>(), boundTenant);
            return await new WorkdayService(boundDb, boundTenant).RespondAsync(token, response, ct);
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockCard(initial.Value.Card.ExaminationId, ct);
        db.ChangeTracker.Clear(); var found = await PublicCard(token, ct); if (found == null) return false; var (c, e, clinic) = found.Value;
        if (c.OwnerResponse == response) return true;
        db.CareCards.Attach(c);
        c.OwnerResponse = response; c.RespondedAt = DateTime.UtcNow;
        if (response == "ContactRequested")
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(clinic.TimeZoneId);
            await UpsertCareTask(c, e.PatientId, "CareContact", "Hasta sahibi görüşmek istiyor — " + e.Patient.Name, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone)), null, ct);
        }
        db.SystemAuditLogs.Add(new SystemAuditLog { ClinicId = c.ClinicID, Category = "Workday", Action = "Care.OwnerResponse", EntityType = "CareCard", EntityId = c.ID.ToString(), Message = response });
        await Save(ct); await tx.CommitAsync(ct); return true;
    }
    private async Task UpsertCareTask(CareCard card, Guid patientId, string kind, string title, DateOnly date, Guid? actor, CancellationToken ct)
    {
        var task = await db.ClinicTasks.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ClinicID == card.ClinicID && x.CareCardId == card.ID && x.Kind == kind, ct);
        if (task == null) { task = new ClinicTask { ClinicID = card.ClinicID, CareCardId = card.ID, Kind = kind, PatientId = patientId, SubmissionId = Guid.NewGuid(), CreatedBy = actor ?? Guid.Empty }; db.Add(task); }
        task.Title = title; task.DueDate = date; task.Status = "Open"; task.Important = kind == "CareContact"; task.CompletedAt = null; task.CompletedBy = null; task.Revision++;
    }

    public async Task ReviewBillingAsync(Guid examinationId, Guid? accountId, string reason, Guid actor, CancellationToken ct = default)
    {
        await Actor(actor, ct); var note = Required(reason, 500); var e = await Examination(examinationId, ct);
        if (accountId.HasValue)
        {
            var account = await db.FinanceAccounts.SingleOrDefaultAsync(x => x.ID == accountId && x.ClinicID == ClinicId && x.IsActive, ct) ?? throw Missing();
            var owners = e.Patient.Owners.Where(x => x.IsActive).Select(x => x.PetOwnerId).ToList();
            if (account.PatientId != e.PatientId && (account.PatientId != null || account.PetOwnerId == null || !owners.Contains(account.PetOwnerId.Value))) throw Invalid("Hesap bu hayvana veya aktif sahibine ait olmalıdır.");
            if (!await db.FinanceCharges.AnyAsync(x => x.FinanceAccountId == accountId && x.ClinicID == ClinicId && x.IsActive, ct)) throw Invalid("Hesapta aktif borç kalemi bulunamadı. Önce tahsilat ekranındaki kaydı kontrol edin.");
        }
        e.ReviewedFinanceAccountId = accountId; e.BillingReviewedAt = DateTime.UtcNow; e.BillingReviewedBy = actor; e.BillingReviewNote = note;
        Audit("Examination.BillingReviewed", e.ID, actor, accountId.HasValue ? "Mevcut hesap ilişkilendirildi." : "Ücretsiz / kapsam içi olarak gözden geçirildi."); await Save(ct);
    }
    public async Task AcknowledgeAsync(DateOnly date, string issueKey, string note, Guid actor, CancellationToken ct = default)
    {
        await Actor(actor, ct); var text = Required(note, 500); var page = await GetAsync(date, ct);
        if (!page.Issues.Any(x => x.Key == issueKey)) throw Invalid("Uyarı artık geçerli değil. Sayfayı yenileyin.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ClinicId + ":" + date + ":" + issueKey}, 0))", ct);
        var ack = await db.ClosingAcknowledgements.SingleOrDefaultAsync(x => x.ClinicID == ClinicId && x.Date == date && x.IssueKey == issueKey, ct);
        if (ack == null) { ack = new ClosingAcknowledgement { ClinicID = ClinicId, Date = date, IssueKey = issueKey }; db.Add(ack); }
        ack.Note = text; ack.ActorId = actor; Audit("Closing.Acknowledged", ack.ID, actor); await Save(ct); await tx.CommitAsync(ct);
    }
}
