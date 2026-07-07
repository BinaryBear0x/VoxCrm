using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Web.Models;
using VoxCrm.Web.Services;

namespace VoxCrm.Web.Controllers;

/// <summary>
/// Sadece Dealer (bayi/siz) kullanabilir.
/// Klinik personeli bu sayfalara hiç erişemez.
/// </summary>
[Authorize(Roles = "Dealer")]
public class DealerController : Controller
{
    private readonly VoxCrmDbContext _context;
    private readonly SystemHealthService _healthService;

    public DealerController(VoxCrmDbContext context, SystemHealthService healthService)
    {
        _context = context;
        _healthService = healthService;
    }

    // GET: /Dealer — Tüm klinikler
    public async Task<IActionResult> Index()
    {
        // Dealer, Global Query Filter dışındadır (Clinic entitysi değil)
        // Bu yüzden tüm klinikleri görebilir — bu doğru davranış.
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        var clinics = await _context.Clinics
            .Include(c => c.Dealer)
            .Where(c => c.DealerId == dealerId)
            .OrderBy(c => c.Name)
            .ToListAsync();
        return View(clinics);
    }

    // GET: /Dealer/Create
    public async Task<IActionResult> Create()
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        ViewBag.Dealers = await _context.Dealers.Where(d => d.ID == dealerId).ToListAsync();
        return View();
    }

    // POST: /Dealer/Create
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Clinic model)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        model.DealerId = dealerId;
        if (!ModelState.IsValid)
        {
            ViewBag.Dealers = await _context.Dealers.Where(d => d.ID == dealerId).ToListAsync();
            return View(model);
        }
        model.Slug = model.Name.ToLower()
            .Replace(" ", "-").Replace("ı", "i").Replace("ş", "s")
            .Replace("ğ", "g").Replace("ü", "u").Replace("ö", "o").Replace("ç", "c");
        _context.Clinics.Add(model);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"{model.Name} kliniği başarıyla eklendi.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Dealer/Edit/{id}
    public async Task<IActionResult> Edit(Guid id)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        var clinic = await _context.Clinics.FirstOrDefaultAsync(c => c.ID == id && c.DealerId == dealerId);
        if (clinic == null) return NotFound();
        ViewBag.Dealers = await _context.Dealers.Where(d => d.ID == dealerId).ToListAsync();
        return View(clinic);
    }

    // POST: /Dealer/Edit/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Clinic model)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        // ID URL'den alınıyor, form'dan değil — ID manipülasyonu önlendi
        var clinic = await _context.Clinics.FirstOrDefaultAsync(c => c.ID == id && c.DealerId == dealerId);
        if (clinic == null) return NotFound();

        clinic.Name                  = model.Name;
        clinic.phone                 = model.phone;
        clinic.Email                 = model.Email;
        clinic.Address               = model.Address;
        clinic.IsWhatsAppEnabled     = model.IsWhatsAppEnabled;
        clinic.WhatsAppPhoneNumberId = model.WhatsAppPhoneNumberId;
        clinic.WhatsAppSendWindowEnabled = model.WhatsAppSendWindowEnabled;
        clinic.WhatsAppSendWindowStart = model.WhatsAppSendWindowStart;
        clinic.WhatsAppSendWindowEnd = model.WhatsAppSendWindowEnd;
        clinic.WhatsAppTimeZoneId = string.IsNullOrWhiteSpace(model.WhatsAppTimeZoneId)
            ? "Europe/Istanbul"
            : model.WhatsAppTimeZoneId.Trim();

        await _context.SaveChangesAsync();
        TempData["Success"] = "Klinik bilgileri güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Dealer/Delete/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        var clinic = await _context.Clinics.FirstOrDefaultAsync(c => c.ID == id && c.DealerId == dealerId);
        if (clinic == null) return NotFound();
        clinic.IsActive = false; // Soft delete — veriyi korur
        await _context.SaveChangesAsync();
        TempData["Success"] = "Klinik sistemden kaldirildi.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Logs(
        string? level,
        string? source,
        string? category,
        string? search,
        DateTime? from,
        DateTime? to,
        Guid? clinicId,
        CancellationToken cancellationToken)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        var clinics = await _context.Clinics
            .Where(c => c.DealerId == dealerId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
        var clinicIds = clinics.Select(c => c.ID).ToList();

        if (clinicId.HasValue && !clinicIds.Contains(clinicId.Value))
            return Forbid();

        var auditQuery = _context.SystemAuditLogs.AsNoTracking()
            .Where(l => l.DealerId == dealerId || (l.ClinicId != null && clinicIds.Contains(l.ClinicId.Value)));

        if (!string.IsNullOrWhiteSpace(level))
            auditQuery = auditQuery.Where(l => l.Level == level);

        if (!string.IsNullOrWhiteSpace(source))
            auditQuery = auditQuery.Where(l => l.Source == source);

        if (!string.IsNullOrWhiteSpace(category))
            auditQuery = auditQuery.Where(l => l.Category == category);

        if (from.HasValue)
            auditQuery = auditQuery.Where(l => l.CreatedAt >= DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc));

        if (to.HasValue)
            auditQuery = auditQuery.Where(l => l.CreatedAt < DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            auditQuery = auditQuery.Where(l =>
                l.Action.Contains(term)
                || l.Message.Contains(term)
                || (l.ActorUserName != null && l.ActorUserName.Contains(term))
                || (l.ErrorCode != null && l.ErrorCode.Contains(term))
                || (l.TraceId != null && l.TraceId.Contains(term)));
        }

        if (clinicId.HasValue)
            auditQuery = auditQuery.Where(l => l.ClinicId == clinicId.Value);

        var notificationQuery = _context.WhatsAppNotifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(n => n.PetOwner)
            .Where(n => clinicIds.Contains(n.ClinicID)
                        && (n.LastError != null
                            || n.Status == WhatsAppNotificationStatuses.Failed
                            || n.Status == WhatsAppNotificationStatuses.NeedsReview));

        if (clinicId.HasValue)
            notificationQuery = notificationQuery.Where(n => n.ClinicID == clinicId.Value);

        var model = new DealerLogsViewModel
        {
            AuditLogs = await auditQuery
                .OrderByDescending(l => l.CreatedAt)
                .Take(200)
                .ToListAsync(cancellationToken),
            WhatsAppErrors = await notificationQuery
                .OrderByDescending(n => n.CreatedAt)
                .Take(100)
                .ToListAsync(cancellationToken),
            Clinics = clinics,
            Level = level,
            Source = source,
            Category = category,
            Search = search,
            From = from,
            To = to,
            ClinicId = clinicId
        };

        ViewData["ActiveMenu"] = "dealer-logs";
        return View(model);
    }

    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        var model = await _healthService.BuildDealerHealthAsync(dealerId, cancellationToken);
        ViewData["ActiveMenu"] = "dealer-health";
        return View(model);
    }

    private bool TryGetDealerId(out Guid dealerId)
    {
        return Guid.TryParse(User.FindFirst("DealerId")?.Value, out dealerId);
    }
}
