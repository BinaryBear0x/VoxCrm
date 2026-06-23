using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        var clinics = await _context.Clinics
            .Include(c => c.Dealer)
            .OrderBy(c => c.Name)
            .ToListAsync();
        return View(clinics);
    }

    // GET: /Dealer/Create
    public async Task<IActionResult> Create()
    {
        ViewBag.Dealers = await _context.Dealers.OrderBy(d => d.CompanyName).ToListAsync();
        return View();
    }

    // POST: /Dealer/Create
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Clinic model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Dealers = await _context.Dealers.OrderBy(d => d.CompanyName).ToListAsync();
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
        var clinic = await _context.Clinics.FindAsync(id);
        if (clinic == null) return NotFound();
        ViewBag.Dealers = await _context.Dealers.OrderBy(d => d.CompanyName).ToListAsync();
        return View(clinic);
    }

    // POST: /Dealer/Edit/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Clinic model)
    {
        // ID URL'den alınıyor, form'dan değil — ID manipülasyonu önlendi
        var clinic = await _context.Clinics.FindAsync(id);
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
        var clinic = await _context.Clinics.FindAsync(id);
        if (clinic == null) return NotFound();
        clinic.IsActive = false; // Soft delete — veriyi korur
        await _context.SaveChangesAsync();
        TempData["Success"] = "Klinik sistemden kaldirildi.";
        return RedirectToAction(nameof(Index));
    }
}
