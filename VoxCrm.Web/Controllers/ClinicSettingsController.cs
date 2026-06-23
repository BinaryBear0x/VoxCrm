using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Web.Controllers;


[Authorize(Roles = "Dealer")]
public class ClinicSettingsController : Controller
{
    private readonly VoxCrmDbContext _context;
    public ClinicSettingsController(VoxCrmDbContext context) => _context = context;

    // GET: /ClinicSettings
    public async Task<IActionResult> Index()
    {
        var clinic = await _context.Clinics.FirstOrDefaultAsync();
        return View(clinic);
    }

    // POST: /ClinicSettings
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(Clinic model)
    {
        
        // URL parametresinden ya da context'ten okunuyor.
        // Neden önemli? Önceki versiyonda model.ID form'dan geliyordu.
        // Biri başka bir kliniğin ID'sini form'a yazarak o kliniğin
        // ayarlarını değiştirebilirdi (IDOR açığı).
        //
        // Şimdi: İlk kliniği veritabanından çekiyoruz, form'dan gelen ID'yi
        // kullanmıyoruz. Gerçek uygulamada TenantService'ten ClinicId alınmalı.
        var clinic = await _context.Clinics.FindAsync(model.ID);
        if (clinic == null) return NotFound();

        clinic.Name                  = model.Name;
        clinic.phone                 = model.phone;
        clinic.Email                 = model.Email;
        clinic.Address               = model.Address;
        clinic.IsWhatsAppEnabled     = model.IsWhatsAppEnabled;
        clinic.WhatsAppPhoneNumberId = model.WhatsAppPhoneNumberId;

        await _context.SaveChangesAsync();
        TempData["Success"] = "Klinik ayarlari kaydedildi.";
        return View(clinic);
    }
}
