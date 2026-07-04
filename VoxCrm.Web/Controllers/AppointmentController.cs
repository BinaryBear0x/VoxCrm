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
    public async Task<IActionResult> Create(
        [Bind("PatientId,ScheduledAt,AppointmentType,DurationMinutes,Reason,Status")] Appointment model)
    {
        // Sistem alanları ve navigasyon özellikleri doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(Appointment.ClinicID));
        ModelState.Remove(nameof(Appointment.CreatedAt));
        ModelState.Remove(nameof(Appointment.IsActive));
        ModelState.Remove(nameof(Appointment.Patient));
        ModelState.Remove(nameof(Appointment.IsReminderSent));

        if (!ModelState.IsValid)
        {
            ViewBag.Patients = await _context.Patients
                .Include(p => p.Owners).ThenInclude(po => po.PetOwner)
                .OrderBy(p => p.Name)
                .ToListAsync();
            return View(model);
        }
        var patientExists = await _context.Patients.AnyAsync(p => p.ID == model.PatientId);
        if (!patientExists)
        {
            ModelState.AddModelError(nameof(model.PatientId), "Geçerli bir hasta seçin.");
            ViewBag.Patients = await _context.Patients
                .Include(p => p.Owners).ThenInclude(po => po.PetOwner)
                .OrderBy(p => p.Name)
                .ToListAsync();
            return View(model);
        }

        _context.Appointments.Add(model);
        await _context.SaveChangesAsync(); // ApplyTenantRules() ClinicID'yi burada atar
        TempData["Success"] = "Randevu başarıyla oluşturuldu.";
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
    public async Task<IActionResult> Edit(
        [Bind("ID,PatientId,ScheduledAt,AppointmentType,DurationMinutes,Reason,Status")] Appointment model)
    {
        // Sistem alanları ve navigasyon özellikleri doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(Appointment.ClinicID));
        ModelState.Remove(nameof(Appointment.CreatedAt));
        ModelState.Remove(nameof(Appointment.IsActive));
        ModelState.Remove(nameof(Appointment.Patient));
        ModelState.Remove(nameof(Appointment.IsReminderSent));

        if (!ModelState.IsValid)
        {
            ViewBag.Patients = await _context.Patients
                .Include(p => p.Owners).ThenInclude(po => po.PetOwner)
                .OrderBy(p => p.Name)
                .ToListAsync();
            return View(model);
        }

        var existing = await _context.Appointments.FindAsync(model.ID); // Global Query Filter: başka klinik = null
        if (existing == null) return NotFound();

        var patientExists = await _context.Patients.AnyAsync(p => p.ID == model.PatientId);
        if (!patientExists)
        {
            ModelState.AddModelError(nameof(model.PatientId), "Geçerli bir hasta seçin.");
            ViewBag.Patients = await _context.Patients
                .Include(p => p.Owners).ThenInclude(po => po.PetOwner)
                .OrderBy(p => p.Name)
                .ToListAsync();
            return View(model);
        }

        existing.PatientId       = model.PatientId;
        existing.ScheduledAt     = model.ScheduledAt;
        existing.AppointmentType = model.AppointmentType;
        existing.DurationMinutes = model.DurationMinutes;
        existing.Reason          = model.Reason;
        // IsReminderSent, ClinicID, CreatedAt, IsActive → hiç dokunulmaz ✅

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
