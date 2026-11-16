using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using VoxCrm.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Vaccinations;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Web.Models;
using VoxCrm.Web.Services;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic,Dealer")]
public partial class WhatsAppController : Controller
{
    private readonly VoxCrmDbContext _context;
    private readonly WhatsAppGatewayClient _gatewayClient;
    private readonly IClinicSendWindowCalculator _sendWindowCalculator;
    private readonly IVaccinationReminderForecast _reminderForecast;

    public WhatsAppController(
        VoxCrmDbContext context,
        WhatsAppGatewayClient gatewayClient,
        IClinicSendWindowCalculator sendWindowCalculator,
        IVaccinationReminderForecast reminderForecast)
    {
        _context = context;
        _gatewayClient = gatewayClient;
        _sendWindowCalculator = sendWindowCalculator;
        _reminderForecast = reminderForecast;
    }

    public async Task<IActionResult> Index(Guid? clinicId, CancellationToken cancellationToken)
    {
        var clinic = await ResolveClinicAsync(clinicId, cancellationToken);
        if (clinic == null)
        {
            if (User.IsInRole("Dealer") && !clinicId.HasValue)
            {
                TempData["Warning"] = "WhatsApp ayarlarını kullanmak için önce bir klinik ekleyin.";
                return RedirectToAction("Create", "Dealer");
            }

            return Forbid();
        }

        GatewaySessionStatus? status = null;
        GatewayHealthResponse? health = null;
        GatewayQrResponse? qr = null;
        string? gatewayWarning = null;

        try
        {
            status = await _gatewayClient.GetStatusAsync(clinic.ID, cancellationToken);
            health = await _gatewayClient.GetHealthAsync(cancellationToken);
            qr = await _gatewayClient.GetQrAsync(clinic.ID, cancellationToken);
        }
        catch (Exception ex) when (IsGatewayFailure(ex))
        {
            LogGatewayFailure(ex, "status page");
            gatewayWarning = "Gateway çalışmıyor, yanıt vermiyor veya kimlik doğrulaması başarısız.";
        }

        var savedTemplate = await _context.WhatsAppTemplates.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.ClinicID == clinic.ID && t.IsActive && t.NotificationType == WhatsAppNotificationTypes.VaccinationReminder)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.Body)
            .FirstOrDefaultAsync(cancellationToken);
        var templateBody = VaccinationMessageTemplate.IsDefault(savedTemplate) ? VaccinationMessageTemplate.DefaultBody : savedTemplate!;

        var model = new WhatsAppSettingsViewModel
        {
            Clinic = clinic,
            TemplateBody = templateBody,
            TemplateSample = VaccinationMessageTemplate.Render(templateBody, new VaccinationMessageValues(
                "Ayşe", "Yılmaz", "Pamuk", "Kuduz", DateTime.Today.AddDays(3), clinic.Name)),
            ReminderPreview = await _reminderForecast.PreviewAsync(clinic.ID, DateTime.UtcNow, 30, cancellationToken),
            AvailableClinics = await GetAvailableClinicsAsync(cancellationToken),
            SessionStatus = status,
            GatewayHealth = health,
            Qr = qr,
            Owners = await _context.PetOwners.IgnoreQueryFilters().Where(x => x.ClinicID == clinic.ID && x.IsActive).Include(x => x.OwnedPatients).ThenInclude(x => x.Patient).OrderBy(x => x.FirstName).ToListAsync(cancellationToken),
            Notifications = await _context.WhatsAppNotifications
                .IgnoreQueryFilters()
                .Include(n => n.PetOwner)
                .Include(n => n.VaccinationRecord).ThenInclude(v => v!.Patient)
                .Where(n => n.ClinicID == clinic.ID)
                .OrderByDescending(n => n.CreatedAt)
                .Take(100)
                .ToListAsync(cancellationToken),
            IsDealer = User.IsInRole("Dealer"),
            GatewayWarning = gatewayWarning,
            NextAllowedSendAtUtc = _sendWindowCalculator.GetNextAllowedSendUtc(clinic, DateTime.UtcNow)
        };

        ViewData["ActiveMenu"] = "whatsapp";
        return View(model);
    }

    private const int MaxTemplateLength = 1000;

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTemplate(Guid clinicId, string body, CancellationToken cancellationToken)
    {
        var clinic = await ResolveClinicAsync(clinicId, cancellationToken);
        if (clinic == null) return Forbid();

        body = (body ?? string.Empty).Trim();
        if (body.Length == 0)
        {
            TempData["Error"] = "Mesaj şablonu boş olamaz.";
            return RedirectToAction(nameof(Index), new { clinicId });
        }
        if (body.Length > MaxTemplateLength)
        {
            TempData["Error"] = $"Mesaj şablonu en fazla {MaxTemplateLength} karakter olabilir.";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        // Set-based writes: a dealer has no tenant context and SaveChanges refuses tenant-entity
        // mutations without one. The scheduler always uses the newest active template.
        var existingId = await _context.WhatsAppTemplates.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.ClinicID == clinic.ID && t.IsActive && t.NotificationType == WhatsAppNotificationTypes.VaccinationReminder)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (Guid?)t.ID)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingId.HasValue)
        {
            await _context.WhatsAppTemplates.IgnoreQueryFilters()
                .Where(t => t.ID == existingId.Value && t.ClinicID == clinic.ID)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Body, body), cancellationToken);
        }
        else
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "WhatsAppTemplates" ("ID", "CreatedAt", "IsActive", "ClinicID", "NotificationType", "Body")
                VALUES ({Guid.NewGuid()}, {DateTime.UtcNow}, TRUE, {clinic.ID}, {WhatsAppNotificationTypes.VaccinationReminder}, {body})
                """, cancellationToken);
        }

        var unknown = VaccinationMessageTemplate.UnknownPlaceholders(body);
        if (unknown.Count > 0)
            TempData["Warning"] = "Şablon kaydedildi ancak tanınmayan alanlar var, mesajda olduğu gibi görünecek: "
                + string.Join(", ", unknown.Select(name => "{" + name + "}"));
        else
            TempData["Success"] = "Aşı hatırlatma şablonu kaydedildi. Sıradaki bekleyen hatırlatmaların metni günlük çalışmada yenilenir.";
        return RedirectToAction(nameof(Index), new { clinicId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(Guid clinicId, CancellationToken cancellationToken)
    {
        var clinic = await ResolveClinicAsync(clinicId, cancellationToken);
        if (clinic == null) return Forbid();

        try
        {
            await _gatewayClient.ConnectAsync(clinic.ID, cancellationToken);
        }
        catch (Exception ex) when (IsGatewayFailure(ex))
        {
            LogGatewayFailure(ex, "connect");
            TempData["Error"] = "WhatsApp bağlantı isteği başlatılamadı. Gateway servisi çalışmıyor veya yanıt vermiyor.";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        TempData["WhatsAppConnectStarted"] = "true";
        TempData["Success"] = "WhatsApp baglanti istegi baslatildi. QR ekranda gorundugunde klinik telefonundan okutun.";
        return RedirectToAction(nameof(Index), new { clinicId });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> QrStatus(Guid clinicId, CancellationToken cancellationToken)
    {
        var clinic = await ResolveClinicAsync(clinicId, cancellationToken);
        if (clinic == null) return Forbid();

        try
        {
            var status = await _gatewayClient.GetStatusAsync(clinic.ID, cancellationToken);
            var qr = await _gatewayClient.GetQrAsync(clinic.ID, cancellationToken);
            return Json(new
            {
                ok = true,
                status = qr?.Status ?? status?.Status ?? "unknown",
                qr = qr?.Qr,
                updatedAt = qr?.UpdatedAt,
                connectedPhone = WhatsAppUi.ConnectedPhone(status?.ConnectedPhone),
                whatsAppEnabled = clinic.IsWhatsAppEnabled,
                whatsAppEnabledChanged = false,
                lastError = status?.LastError
            });
        }
        catch (Exception ex) when (IsGatewayFailure(ex))
        {
            LogGatewayFailure(ex, "qr status");
            Response.StatusCode = StatusCodes.Status502BadGateway;
            return Json(new
            {
                ok = false,
                error = "Gateway'e ulaşılamadı veya yanıt vermedi."
            });
        }
    }

    // Timeouts surface as TaskCanceledException and a broken gateway answer as JsonException;
    // neither may turn a status page into a 500.
    private static bool IsGatewayFailure(Exception ex) => ex is HttpRequestException or TaskCanceledException or JsonException;

    private void LogGatewayFailure(Exception ex, string operation) =>
        HttpContext.RequestServices.GetService<ILogger<WhatsAppController>>()
            ?.LogWarning(ex, "WhatsApp gateway call failed during {Operation}", operation);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect(Guid clinicId, CancellationToken cancellationToken)
    {
        var clinic = await ResolveClinicAsync(clinicId, cancellationToken);
        if (clinic == null) return Forbid();

        try
        {
            await _gatewayClient.DisconnectAsync(clinic.ID, cancellationToken);
        }
        catch (Exception ex) when (IsGatewayFailure(ex))
        {
            LogGatewayFailure(ex, "disconnect");
            TempData["Error"] = "WhatsApp oturumu sonlandırılamadı. Gateway servisi çalışmıyor veya yanıt vermiyor.";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        TempData["Success"] = "WhatsApp oturumu sonlandırıldı.";
        return RedirectToAction(nameof(Index), new { clinicId });
    }

    private WhatsAppRecipientGuard Guard => new(_context);

    private async Task<Clinic?> ResolveClinicAsync(Guid? clinicId, CancellationToken cancellationToken)
    {
        if (User.IsInRole("Clinic"))
        {
            if (!Guid.TryParse(User.FindFirst("ClinicId")?.Value, out var currentClinicId))
                return null;

            return await _context.Clinics.FirstOrDefaultAsync(c => c.ID == currentClinicId, cancellationToken);
        }

        if (!Guid.TryParse(User.FindFirst("DealerId")?.Value, out var dealerId))
            return null;

        var query = _context.Clinics.Where(c => c.DealerId == dealerId).OrderBy(c => c.Name);
        return clinicId.HasValue
            ? await query.FirstOrDefaultAsync(c => c.ID == clinicId.Value, cancellationToken)
            : await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Clinic>> GetAvailableClinicsAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Dealer")) return Array.Empty<Clinic>();
        if (!Guid.TryParse(User.FindFirst("DealerId")?.Value, out var dealerId)) return Array.Empty<Clinic>();

        return await _context.Clinics
            .Where(c => c.DealerId == dealerId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    private IDataProtector ConfirmationProtector => HttpContext.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector("WhatsApp.Preview.v1");
    private sealed record Confirmation(WhatsAppNotification Notification, DateTime Expires);
    private IActionResult QueueError(Guid clinicId, string error)
    {
        TempData["Error"] = error; return RedirectToAction(nameof(Index), new { clinicId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSendingSettings(Guid clinicId, bool enabled, bool automatic, bool manual, CancellationToken ct)
    {
        var clinic = await ResolveClinicAsync(clinicId, ct); if (clinic == null) return Forbid();
        // IsWhatsAppEnabled is the dealer's module permission (the whole WhatsApp area is
        // closed to clinic users while it is off). A clinic switching it off could never
        // switch it back on, so only the dealer may change it; clinics control the two
        // sending types below.
        if (User.IsInRole("Dealer")) clinic.IsWhatsAppEnabled = enabled; else enabled = clinic.IsWhatsAppEnabled;
        var automaticWasOff = !clinic.AutomaticVaccinationRemindersEnabled;
        clinic.AutomaticVaccinationRemindersEnabled = automatic; clinic.ManualWhatsAppEnabled = manual;
        // Set-based update: a dealer has no tenant context, and SaveChanges refuses to mutate
        // tenant entities without one. The ClinicID condition keeps it scoped to this clinic.
        var cancelled = await _context.WhatsAppNotifications.IgnoreQueryFilters()
            .Where(x => x.ClinicID == clinic.ID
                        && (x.Status == WhatsAppNotificationStatuses.Pending || x.Status == WhatsAppNotificationStatuses.RetryScheduled)
                        && (!enabled || (x.IsAutomatic && !automatic) || (!x.IsAutomatic && !manual)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, WhatsAppNotificationStatuses.Cancelled)
                .SetProperty(x => x.LastError, "Klinik gönderim ayarını kapattığı için iptal edildi.")
                .SetProperty(x => x.NextAttemptAt, (DateTime?)null), ct);
        await _context.SaveChangesAsync(ct);
        var summary = new List<string> { "Gönderim ayarları kaydedildi." };
        if (automatic && automaticWasOff && enabled)
            summary.Add("Otomatik hatırlatma açıldı: hatırlatma günü gelen aşıların mesajları günlük çalışmada sıraya alınır. Gidecekleri aşağıdaki özetten görebilirsiniz.");
        if (cancelled > 0)
            summary.Add($"Kapatılan gönderim türünün {cancelled} bekleyen mesajı iptal edildi.");
        TempData["Success"] = string.Join(" ", summary);
        return RedirectToAction(nameof(Index), new { clinicId = clinic.ID });
    }

    [HttpPost, ValidateAntiForgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SendManual(Guid clinicId, Guid ownerId, Guid? patientId, string message, CancellationToken ct)
    {
        var clinic = await ResolveClinicAsync(clinicId, ct); if (clinic == null) return Forbid();
        var owner = await _context.PetOwners.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ID == ownerId && x.ClinicID == clinic.ID && x.IsActive, ct);
        if (owner == null) return QueueError(clinic.ID, "Listeden geçerli bir müşteri seçin.");
        if (string.IsNullOrWhiteSpace(message) || message.Trim().Length > 4000) return QueueError(clinic.ID, "Mesaj 1–4000 karakter olmalıdır.");
        var patient = patientId.HasValue ? await _context.Patients.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ID == patientId && x.ClinicID == clinic.ID, ct) : null;
        if (patientId.HasValue && patient == null) return QueueError(clinic.ID, "İlgili hayvan bulunamadı.");
        var n = new WhatsAppNotification { ClinicID = clinic.ID, PetOwnerId = owner.ID, PatientId = patient?.ID, PhoneNumber = owner.Phone ?? "", MessageContent = message.Trim(),
            NotificationType = WhatsAppNotificationTypes.ManualMessage, RecipientSnapshot = WhatsAppRecipientGuard.Snapshot(owner, patient), NextAttemptAt = _sendWindowCalculator.GetNextAllowedSendUtc(clinic, DateTime.UtcNow) };
        var error = await Guard.CheckAsync(n, ct);
        var token = error == null ? ConfirmationProtector.Protect(JsonSerializer.Serialize(new Confirmation(n, DateTime.UtcNow.AddMinutes(10)))) : null;
        return View("Preview", new WhatsAppPreview(n, owner.FirstName + " " + owner.LastName, patient?.Name, error, token));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmManual(Guid clinicId, string confirmationToken, CancellationToken ct)
    {
        var clinic = await ResolveClinicAsync(clinicId, ct); if (clinic == null) return Forbid();
        Confirmation? signed;
        try { signed = JsonSerializer.Deserialize<Confirmation>(ConfirmationProtector.Unprotect(confirmationToken)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException) { return QueueError(clinic.ID, "Önizleme doğrulanamadı. Mesajı yeniden hazırlayın."); }
        if (signed == null || signed.Expires < DateTime.UtcNow || signed.Notification.ClinicID != clinic.ID) return QueueError(clinic.ID, "Önizleme süresi dolmuş veya klinik eşleşmiyor. Yeniden hazırlayın.");
        var n = signed.Notification;
        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({(n.VaccinationRecordId ?? n.ID).ToString()}, 0))", ct);
        var existing = await _context.WhatsAppNotifications.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ID == n.ID || (n.VaccinationRecordId != null && x.VaccinationRecordId == n.VaccinationRecordId), ct);
        if (existing != null && (existing.ID != n.ID || existing.Status is not (WhatsAppNotificationStatuses.Failed or WhatsAppNotificationStatuses.Cancelled) || existing.SentAt != null || existing.GatewayMessageId != null))
            return QueueError(clinic.ID, "Bu mesaj zaten sıraya alınmış, gönderilmiş veya teslimi belirsiz. Çift gönderimi önlemek için tekrar eklenmedi.");
        var error = await Guard.CheckAsync(n, ct);
        if (error != null) return QueueError(clinic.ID, error);
        n.NextAttemptAt = _sendWindowCalculator.GetNextAllowedSendUtc(clinic, DateTime.UtcNow);
        if (existing == null) _context.WhatsAppNotifications.Add(n);
        else
        {
            existing.PhoneNumber = n.PhoneNumber; existing.MessageContent = n.MessageContent; existing.PetOwnerId = n.PetOwnerId;
            existing.PatientId = n.PatientId; existing.RecipientSnapshot = n.RecipientSnapshot; existing.IsAutomatic = false;
            existing.Status = WhatsAppNotificationStatuses.Pending; existing.RetryCount = 0; existing.LastError = null; existing.NextAttemptAt = n.NextAttemptAt;
        }
        await _context.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        TempData["Success"] = "Onayladığınız alıcıya mesaj sıraya alındı. Bağlantı yoksa gönderilmeden bekler.";
        return RedirectToAction(nameof(Index), new { clinicId = clinic.ID });
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PrepareVaccination(Guid id, Guid? clinicId, CancellationToken ct)
    {
        var clinic = await ResolveClinicAsync(clinicId, ct); if (clinic == null) return Forbid();
        var record = await _context.VaccinationRecords.IgnoreQueryFilters().Include(x => x.VaccineType).Include(x => x.Patient).ThenInclude(x => x.Owners).ThenInclude(x => x.PetOwner)
            .SingleOrDefaultAsync(x => x.ID == id && x.ClinicID == clinic.ID, ct); if (record == null) return NotFound();
        var owner = record.Patient.Owners.FirstOrDefault(x => x.IsActive && x.IsPrimaryOwner && x.PetOwner.IsActive)?.PetOwner;
        if (owner == null) return QueueError(clinic.ID, "Bu hayvan için aktif birincil sahip bulunamadı. Hasta ekranından sahip bağlantısını düzeltin.");
        var old = await _context.WhatsAppNotifications.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.VaccinationRecordId == id, ct);
        if (old != null && old.Status is not (WhatsAppNotificationStatuses.Failed or WhatsAppNotificationStatuses.Cancelled))
            return RedirectToAction(nameof(Preview), new { clinicId = clinic.ID, id = old.ID });
        var template = await _context.WhatsAppTemplates.IgnoreQueryFilters().Where(x => x.ClinicID == clinic.ID && x.IsActive && x.NotificationType == WhatsAppNotificationTypes.VaccinationReminder).OrderByDescending(x => x.CreatedAt).Select(x => x.Body).FirstOrDefaultAsync(ct);
        var text = VaccinationMessageTemplate.Render(template, new VaccinationMessageValues(
            owner.FirstName, owner.LastName, record.Patient.Name, record.VaccineType.Name, record.NextDueDate, clinic.Name));
        var n = new WhatsAppNotification { ID = old?.ID ?? Guid.NewGuid(), ClinicID = clinic.ID, PetOwnerId = owner.ID, PatientId = record.PatientId,
            VaccinationRecordId = record.ID, PhoneNumber = owner.Phone ?? "", MessageContent = text, RecipientSnapshot = WhatsAppRecipientGuard.Snapshot(owner, record.Patient, record) };
        var error = await Guard.CheckAsync(n, ct);
        var token = error == null ? ConfirmationProtector.Protect(JsonSerializer.Serialize(new Confirmation(n, DateTime.UtcNow.AddMinutes(10)))) : null;
        return View("Preview", new WhatsAppPreview(n, owner.FirstName + " " + owner.LastName, record.Patient.Name, error, token));
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Preview(Guid clinicId, Guid id, CancellationToken ct)
    {
        var clinic = await ResolveClinicAsync(clinicId, ct); if (clinic == null) return Forbid();
        var n = await _context.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().Include(x => x.PetOwner).Include(x => x.VaccinationRecord).ThenInclude(x => x!.Patient)
            .SingleOrDefaultAsync(x => x.ID == id && x.ClinicID == clinic.ID, ct); if (n == null) return QueueError(clinic.ID, "Mesaj bulunamadı. Başka bir sekmede veya cihazda değiştirilmiş olabilir.");
        var patient = n.VaccinationRecord?.Patient;
        if (patient == null && n.PatientId.HasValue) patient = await _context.Patients.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ID == n.PatientId && x.ClinicID == clinic.ID, ct);
        if (n.Status == WhatsAppNotificationStatuses.NeedsReview && n.SentAt == null && n.GatewayMessageId == null)
        {
            // Judge the stopped message against the recipient as they are now, the same way the
            // release action will, so the screen only offers what will actually work.
            var current = await Guard.CurrentSnapshotAsync(n, ct);
            if (current != null) n.RecipientSnapshot = current;
            var currentError = current == null ? "Müşteri, hayvan veya aşı kaydı bulunamadı." : await Guard.CheckAsync(n, ct);
            return View(new WhatsAppPreview(n, n.PetOwner.FirstName + " " + n.PetOwner.LastName, patient?.Name, currentError, null, currentError == null));
        }
        return View(new WhatsAppPreview(n, n.PetOwner.FirstName + " " + n.PetOwner.LastName, patient?.Name, await Guard.CheckAsync(n, ct)));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ManageNotification(Guid clinicId, Guid id, string operation, bool acknowledged, CancellationToken ct)
    {
        var clinic = await ResolveClinicAsync(clinicId, ct); if (clinic == null) return Forbid();
        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        // A dealer has no tenant of its own, so the global tenant filter would hide the row.
        // The ClinicID condition in the SQL is what keeps this scoped to the resolved clinic.
        var n = await _context.WhatsAppNotifications.FromSqlInterpolated($"SELECT * FROM \"WhatsAppNotifications\" WHERE \"ID\" = {id} AND \"ClinicID\" = {clinic.ID} FOR UPDATE").IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(ct);
        if (n == null) return QueueError(clinic.ID, "Mesaj bulunamadı. Başka bir sekmede veya cihazda değiştirilmiş olabilir; liste yenilendi.");
        // A dealer works without a tenant context, and the context refuses tenant-entity
        // SaveChanges in that case. Set-based updates with an explicit ClinicID condition work
        // for both roles; the row stays locked by the FOR UPDATE above until the commit.
        var queue = _context.WhatsAppNotifications.IgnoreQueryFilters().Where(x => x.ID == n.ID && x.ClinicID == clinic.ID);
        if (operation == "cancel" && n.Status is WhatsAppNotificationStatuses.Pending or WhatsAppNotificationStatuses.RetryScheduled or WhatsAppNotificationStatuses.NeedsReview)
        {
            await queue.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, WhatsAppNotificationStatuses.Cancelled)
                .SetProperty(x => x.LastError, "Kullanıcı tarafından iptal edildi.")
                .SetProperty(x => x.NextAttemptAt, (DateTime?)null), ct);
        }
        else if (operation == "retry" && n.Status is WhatsAppNotificationStatuses.Failed or WhatsAppNotificationStatuses.Cancelled && n.SentAt == null && n.GatewayMessageId == null)
        {
            var error = await Guard.CheckAsync(n, ct);
            if (error != null) return QueueError(clinic.ID, error + " Eski mesajı yeniden göndermek yerine yeni önizleme hazırlayın.");
            var nextAttempt = _sendWindowCalculator.GetNextAllowedSendUtc(clinic, DateTime.UtcNow);
            await queue.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, WhatsAppNotificationStatuses.Pending)
                .SetProperty(x => x.LastError, (string?)null)
                .SetProperty(x => x.RetryCount, 0)
                .SetProperty(x => x.NextAttemptAt, (DateTime?)nextAttempt), ct);
        }
        else if (operation == "requeue" && n.Status == WhatsAppNotificationStatuses.NeedsReview && n.SentAt == null && n.GatewayMessageId == null)
        {
            // The message was stopped because something about it was uncertain. A person who has
            // looked at the current recipient and message may release it; every other guard check
            // (consent, phone, record state) is still enforced against the data as it is now.
            if (!acknowledged)
                return QueueError(clinic.ID, "Yeniden sıraya almak için alıcıyı ve mesajı kontrol ettiğinizi onaylayın.");
            var current = await Guard.CurrentSnapshotAsync(n, ct);
            if (current == null)
                return QueueError(clinic.ID, "Müşteri, hayvan veya aşı kaydı bulunamadı. Mesajı iptal edin.");
            n.RecipientSnapshot = current;
            var error = await Guard.CheckAsync(n, ct);
            if (error != null) return QueueError(clinic.ID, error + " Gerekli düzeltmeyi yapın veya mesajı iptal edin.");
            var nextAttempt = _sendWindowCalculator.GetNextAllowedSendUtc(clinic, DateTime.UtcNow);
            await queue.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, WhatsAppNotificationStatuses.Pending)
                .SetProperty(x => x.RecipientSnapshot, current)
                .SetProperty(x => x.LastError, (string?)null)
                .SetProperty(x => x.RetryCount, 0)
                .SetProperty(x => x.NextAttemptAt, (DateTime?)nextAttempt)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LockedBy, (string?)null)
                .SetProperty(x => x.LockExpiresAt, (DateTime?)null), ct);
        }
        else return QueueError(clinic.ID, "İşlem uygulanamadı. Gönderilmiş, işlenmekte olan veya teslimi belirsiz mesaj tekrar gönderilemez. Sayfayı yenileyin.");
        await tx.CommitAsync(ct);
        TempData["Success"] = operation switch
        {
            "cancel" => "Mesaj iptal edildi; gönderilmeyecek.",
            "requeue" => "Mesaj yeniden sıraya alındı. Bağlantı varsa gönderim saatlerinde gider.",
            _ => "Mesaj yeniden sıraya alındı."
        };
        return RedirectToAction(nameof(Index), new { clinicId = clinic.ID });
    }
}
