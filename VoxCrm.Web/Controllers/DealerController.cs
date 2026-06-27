using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Web.Controllers;

/// <summary>
/// Sadece Dealer (bayi/siz) kullanabilir.
/// Klinik personeli bu sayfalara hiç erişemez.
/// </summary>
[Authorize(Roles = "Dealer")]
public class DealerController : Controller
{
    private readonly VoxCrmDbContext _context;
    public DealerController(VoxCrmDbContext context) => _context = context;

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
        TempData["Success"] = $"{model.Name} kliniği basariyla eklendi.";
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

        await _context.SaveChangesAsync();
        TempData["Success"] = "Klinik bilgileri guncellendi.";
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

    private bool TryGetDealerId(out Guid dealerId)
    {
        return Guid.TryParse(User.FindFirst("DealerId")?.Value, out dealerId);
    }
}
