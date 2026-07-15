using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Web.Models;
using VoxCrm.Web.Services;
using VoxCrm.Infrastructure.Security;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic,Dealer")]
public class WhatsAppController : Controller
{
    private readonly VoxCrmDbContext _context;
    private readonly WhatsAppGatewayClient _gatewayClient;
    private readonly IClinicSendWindowCalculator _sendWindowCalculator;
    private readonly IPiiProtector _protector;

    public WhatsAppController(
        VoxCrmDbContext context,
        WhatsAppGatewayClient gatewayClient,
        IClinicSendWindowCalculator sendWindowCalculator,
        IPiiProtector protector)
    {
        _context = context;
        _gatewayClient = gatewayClient;
        _sendWindowCalculator = sendWindowCalculator;
        _protector = protector;
    }

    public async Task<IActionResult> Index(Guid? clinicId, CancellationToken cancellationToken)
    {
        var clinic = await ResolveClinicAsync(clinicId, cancellationToken);
        if (clinic == null)
        {
            if (User.IsInRole("Dealer"))
            {
                TempData["Warning"] = "WhatsApp ayarlarını kullanmak için önce bir klinik ekleyin.";
                return RedirectToAction("Create", "Dealer");
            }

            return Forbid();
        }

        var template = await _context.WhatsAppTemplates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.ClinicID == clinic.ID
                                      && t.NotificationType == WhatsAppNotificationTypes.VaccinationReminder
                                      && t.IsActive,
                cancellationToken)
            ?? new WhatsAppTemplate
            {
                ClinicID = clinic.ID,
                NotificationType = WhatsAppNotificationTypes.VaccinationReminder,
                Body = DefaultVaccinationTemplate
            };

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
        catch (HttpRequestException ex)
        {
            gatewayWarning = $"Gateway çalışmıyor veya ulaşılamıyor: {ex.Message}";
        }

        var model = new WhatsAppSettingsViewModel
        {
            Clinic = clinic,
            AvailableClinics = await GetAvailableClinicsAsync(cancellationToken),
            Template = template,
            SessionStatus = status,
            GatewayHealth = health,
            Qr = qr,
            Notifications = await _context.WhatsAppNotifications
                .IgnoreQueryFilters()
                .Include(n => n.PetOwner)
                .Where(n => n.ClinicID == clinic.ID)
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .ToListAsync(cancellationToken),
            InboundMessages = await _context.WhatsAppInboundMessages
                .IgnoreQueryFilters()
                .Where(m => m.ClinicID == clinic.ID)
                .OrderByDescending(m => m.ReceivedAt)
                .Take(50)
                .ToListAsync(cancellationToken),
            IsDealer = User.IsInRole("Dealer"),
            GatewayWarning = gatewayWarning,
            NextAllowedSendAtUtc = _sendWindowCalculator.GetNextAllowedSendUtc(clinic, DateTime.UtcNow)
        };

        ViewData["ActiveMenu"] = "whatsapp";
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTemplate(Guid clinicId, string body, CancellationToken cancellationToken)
    {
        var clinic = await ResolveClinicAsync(clinicId, cancellationToken);
        if (clinic == null) return Forbid();

        if (string.IsNullOrWhiteSpace(body))
        {
            TempData["Error"] = "Mesaj sablonu bos olamaz.";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        var template = await _context.WhatsAppTemplates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.ClinicID == clinic.ID
                                      && t.NotificationType == WhatsAppNotificationTypes.VaccinationReminder
                                      && t.IsActive,
                cancellationToken);

        if (template == null)
        {
            template = new WhatsAppTemplate
            {
                ClinicID = clinic.ID,
                NotificationType = WhatsAppNotificationTypes.VaccinationReminder
            };
            _context.WhatsAppTemplates.Add(template);
        }

        template.Body = body.Trim();
        await _context.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "WhatsApp sablonu kaydedildi.";
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
        catch (HttpRequestException ex)
        {
            TempData["Error"] = $"WhatsApp baglanti istegi baslatilamadi: {ex.Message}";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        try
        {
            var status = await _gatewayClient.GetStatusAsync(clinic.ID, cancellationToken);
            if (status?.Status == "ready")
                await EnsureWhatsAppEnabledAsync(clinic, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // QR polling status'u tekrar okuyacak; connect isteği başarılıysa kullanıcı akışını kesmeyelim.
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
            var enabledChanged = false;

            if ((status?.Status == "ready" || qr?.Status == "ready") && !clinic.IsWhatsAppEnabled)
            {
                clinic.IsWhatsAppEnabled = true;
                await _context.SaveChangesAsync(cancellationToken);
                enabledChanged = true;
            }

            return Json(new
            {
                ok = true,
                status = qr?.Status ?? status?.Status ?? "unknown",
                qr = qr?.Qr,
                updatedAt = qr?.UpdatedAt,
                connectedPhone = NormalizeWhatsAppPhone(status?.ConnectedPhone),
                whatsAppEnabled = clinic.IsWhatsAppEnabled,
                whatsAppEnabledChanged = enabledChanged,
                lastError = status?.LastError
            });
        }
        catch (HttpRequestException ex)
        {
            Response.StatusCode = StatusCodes.Status502BadGateway;
            return Json(new
            {
                ok = false,
                error = $"Gateway'e ulasilamadi: {ex.Message}"
            });
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect(Guid clinicId, CancellationToken cancellationToken)
    {
        var clinic = await ResolveClinicAsync(clinicId, cancellationToken);
        if (clinic == null) return Forbid();

        try
        {
            await _gatewayClient.DisconnectAsync(clinic.ID, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            TempData["Error"] = $"WhatsApp oturumu sonlandirilamadi: {ex.Message}";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        TempData["Success"] = "WhatsApp oturumu sonlandırıldı.";
        return RedirectToAction(nameof(Index), new { clinicId });
    }

    /// <summary>Bağlı kendi numarasına test mesajı gönderir.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TestMessage(Guid clinicId, CancellationToken cancellationToken)
    {
        var clinic = await ResolveClinicAsync(clinicId, cancellationToken);
        if (clinic == null) return Forbid();

        GatewaySessionStatus? status;
        try { status = await _gatewayClient.GetStatusAsync(clinic.ID, cancellationToken); }
        catch (HttpRequestException ex)
        {
            TempData["Error"] = $"Gateway'e ulaşılamadı: {ex.Message}";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        if (status?.Status != "ready" || string.IsNullOrWhiteSpace(status.ConnectedPhone))
        {
            TempData["Error"] = "Test mesajı göndermek için WhatsApp oturumunun bağlı (ready) olması gerekiyor.";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        await EnsureWhatsAppEnabledAsync(clinic, cancellationToken);

        var targetPhone = NormalizeWhatsAppPhone(status.ConnectedPhone);
        if (string.IsNullOrWhiteSpace(targetPhone))
        {
            TempData["Error"] = "Bağlı WhatsApp numarası okunamadı. Oturumu kesip tekrar bağlamayı deneyin.";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        var testMsg = $"VoxCRM test mesaji - {clinic.Name} - {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        var nextAttemptAt = GetNextAttemptAtWithToast(clinic);

        // Test mesajı göndermek için anonim bir PetOwner kullan/oluştur
        var owner = await GetOrCreateAnonymousOwnerAsync(clinic.ID, targetPhone, cancellationToken);

        var notification = new WhatsAppNotification
        {
            ClinicID = clinic.ID,
            PetOwnerId = owner.ID,
            PhoneNumber = targetPhone,
            MessageContent = testMsg,
            NotificationType = WhatsAppNotificationTypes.ManualMessage,
            Status = WhatsAppNotificationStatuses.Pending,
            NextAttemptAt = nextAttemptAt
        };

        _context.WhatsAppNotifications.Add(notification);
        await _context.SaveChangesAsync(cancellationToken);

        TempData["Success"] = $"Test mesajı sıraya alındı ({targetPhone}).";
        return RedirectToAction(nameof(Index), new { clinicId });
    }

    /// <summary>Belirtilen numara ve mesajla manuel bildirim gönderir.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendManual(Guid clinicId, string phone, string message, CancellationToken cancellationToken)
    {
        var clinic = await ResolveClinicAsync(clinicId, cancellationToken);
        if (clinic == null) return Forbid();

        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(message))
        {
            TempData["Error"] = "Telefon numarası ve mesaj alanları boş bırakılamaz.";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        GatewaySessionStatus? status;
        try { status = await _gatewayClient.GetStatusAsync(clinic.ID, cancellationToken); }
        catch (HttpRequestException ex)
        {
            TempData["Error"] = $"Gateway çalışmıyor veya ulaşılamıyor: {ex.Message}";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        if (status?.Status != "ready")
        {
            TempData["Error"] = "Manuel mesaj göndermek için WhatsApp oturumunun bağlı (ready) olması gerekiyor.";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        await EnsureWhatsAppEnabledAsync(clinic, cancellationToken);

        var normalizedPhone = NormalizeWhatsAppPhone(phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
        {
            TempData["Error"] = "Telefon numarası geçerli değil.";
            return RedirectToAction(nameof(Index), new { clinicId });
        }

        var owner = await GetOrCreateAnonymousOwnerAsync(clinic.ID, normalizedPhone, cancellationToken);
        var nextAttemptAt = GetNextAttemptAtWithToast(clinic);

        var notification = new WhatsAppNotification
        {
            ClinicID = clinic.ID,
            PetOwnerId = owner.ID,
            PhoneNumber = normalizedPhone,
            MessageContent = message.Trim(),
            NotificationType = WhatsAppNotificationTypes.ManualMessage,
            Status = WhatsAppNotificationStatuses.Pending,
            NextAttemptAt = nextAttemptAt
        };

        _context.WhatsAppNotifications.Add(notification);
        await _context.SaveChangesAsync(cancellationToken);

        TempData["Success"] = $"Mesaj sıraya alındı ({normalizedPhone}).";
        return RedirectToAction(nameof(Index), new { clinicId });
    }

    private DateTime GetNextAttemptAtWithToast(Clinic clinic)
    {
        var now = DateTime.UtcNow;
        var nextAttemptAt = _sendWindowCalculator.GetNextAllowedSendUtc(clinic, now);
        if (nextAttemptAt > now.AddSeconds(30))
        {
            TempData["Warning"] =
                $"Klinik gönderim penceresi dışında. Mesaj {nextAttemptAt.ToLocalTime():dd.MM.yyyy HH:mm} için kuyruklandı.";
        }
        else
        {
            TempData["Info"] = "Mesaj gönderim penceresi içinde, gateway tarafından birazdan gönderilecek.";
        }

        return nextAttemptAt;
    }

    private async Task EnsureWhatsAppEnabledAsync(Clinic clinic, CancellationToken cancellationToken)
    {
        if (clinic.IsWhatsAppEnabled) return;

        clinic.IsWhatsAppEnabled = true;
        await _context.SaveChangesAsync(cancellationToken);
        TempData["Info"] = "Klinik WhatsApp gönderimi otomatik aktifleştirildi.";
    }

    private static string NormalizeWhatsAppPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;

        var value = phone.Trim();
        var atIndex = value.IndexOf('@');
        if (atIndex >= 0) value = value[..atIndex];

        var colonIndex = value.IndexOf(':');
        if (colonIndex >= 0) value = value[..colonIndex];

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits)) return string.Empty;

        if (digits.StartsWith("0", StringComparison.Ordinal))
            digits = "90" + digits[1..];
        else if (!digits.StartsWith("90", StringComparison.Ordinal))
            digits = "90" + digits;

        return digits;
    }

    private async Task<PetOwner> GetOrCreateAnonymousOwnerAsync(Guid clinicId, string phone, CancellationToken cancellationToken)
    {
        var phoneLookup = _protector.BlindIndex(clinicId, new string(phone.Where(char.IsDigit).ToArray()));
        var existing = await _context.PetOwners
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.ClinicID == clinicId
                && p.NormalizedPhone == phoneLookup, cancellationToken);

        if (existing != null) return existing;

        var newOwner = new PetOwner
        {
            ClinicID = clinicId,
            FirstName = "Manuel",
            LastName = "Kayıt",
            Phone = phone,
            NormalizedPhone = phoneLookup,
            WhatsAppConsent = true,
            Notes = "Manuel mesaj gönderimi için otomatik oluşturuldu."
        };
        _context.PetOwners.Add(newOwner);
        await _context.SaveChangesAsync(cancellationToken);
        return newOwner;
    }

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

    private const string DefaultVaccinationTemplate =
        "Sayin {ownerFirstName}, {patientName} icin {vaccineName} hatirlatmasi: sonraki tarih {dueDate}. {clinicName}";
}
