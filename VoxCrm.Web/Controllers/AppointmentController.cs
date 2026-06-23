using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public class AppointmentController : Controller
{
    private readonly VoxCrmDbContext _context;
    public AppointmentController(VoxCrmDbContext context) => _context = context;

    // İzin verilen randevu durumları — başka değer kabul edilmez
    private static readonly HashSet<string> AllowedStatuses =
        new() { "Planlandı", "Tamamlandı", "İptal", "Gelmedi" };

    public async Task<IActionResult> Index(string? status)
    {
        if (status != null && !AllowedStatuses.Contains(status))
            status = null;

        var query = _context.Appointments.Include(a => a.Patient).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        var list = await query.OrderByDescending(a => a.ScheduledAt).ToListAsync();
        ViewBag.Status = status;
        return View(list);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Patients = await _context.Patients
            .Include(p => p.Owners).ThenInclude(po => po.PetOwner)
            .OrderBy(p => p.Name)
            .ToListAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Appointment model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Patients = await _context.Patients
                .Include(p => p.Owners).ThenInclude(po => po.PetOwner)
                .OrderBy(p => p.Name)
                .ToListAsync();
            return View(model);
        }
        _context.Appointments.Add(model);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Randevu basariyla olusturuldu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(Guid id, string status)
    {
        if (!AllowedStatuses.Contains(status))
            return BadRequest("Geçersiz durum değeri.");

        var appt = await _context.Appointments.FindAsync(id);
        if (appt == null) return NotFound();
        appt.Status = status;
        await _context.SaveChangesAsync();
        TempData["Success"] = "Randevu durumu güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var appt = await _context.Appointments.FindAsync(id);
        if (appt == null) return NotFound();

        ViewBag.Patients = await _context.Patients
            .Include(p => p.Owners).ThenInclude(po => po.PetOwner)
            .OrderBy(p => p.Name)
            .ToListAsync();
        return View(appt);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Appointment model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Patients = await _context.Patients
                .Include(p => p.Owners).ThenInclude(po => po.PetOwner)
                .OrderBy(p => p.Name)
                .ToListAsync();
            return View(model);
        }

        var existing = await _context.Appointments.FindAsync(model.ID);
        if (existing == null) return NotFound();

        existing.PatientId = model.PatientId;
        existing.ScheduledAt = model.ScheduledAt;
        existing.AppointmentType = model.AppointmentType;
        existing.DurationMinutes = model.DurationMinutes;
        existing.Reason = model.Reason;

        await _context.SaveChangesAsync();
        TempData["Success"] = "Randevu başarıyla güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var appt = await _context.Appointments.FindAsync(id);
        if (appt != null)
        {
            _context.Appointments.Remove(appt);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Randevu başarıyla silindi.";
        }
        return RedirectToAction(nameof(Index));
    }
}
