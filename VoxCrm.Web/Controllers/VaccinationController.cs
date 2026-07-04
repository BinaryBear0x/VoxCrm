using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public class VaccinationController : Controller
{
    private readonly VoxCrmDbContext _context;
    public VaccinationController(VoxCrmDbContext context) => _context = context;

    public async Task<IActionResult> Index()
    {
        var records = await _context.VaccinationRecords
            .Include(v => v.Patient)
            .Include(v => v.VaccineType)
            .OrderBy(v => v.NextDueDate)
            .ToListAsync();
        return View(records);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Patients     = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
        ViewBag.VaccineTypes = await _context.VaccineTypes.OrderBy(v => v.Name).ToListAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("PatientId,VaccineTypeId,AdministeredDate")] VaccinationRecord model)
    {
        var patientExists = await _context.Patients.AnyAsync(p => p.ID == model.PatientId);
        var vaccineType = await _context.VaccineTypes.FindAsync(model.VaccineTypeId);

        // Sistem alanları, navigasyon özellikleri ve hesaplanan alanlar doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(VaccinationRecord.ClinicID));
        ModelState.Remove(nameof(VaccinationRecord.CreatedAt));
        ModelState.Remove(nameof(VaccinationRecord.IsActive));
        ModelState.Remove(nameof(VaccinationRecord.Patient));
        ModelState.Remove(nameof(VaccinationRecord.VaccineType));
        ModelState.Remove(nameof(VaccinationRecord.NextDueDate));
        ModelState.Remove(nameof(VaccinationRecord.IsReminderSent));

        if (!patientExists || vaccineType == null)
        {
            ModelState.AddModelError("", "Geçerli bir hasta ve aşı tipi seçin.");
            ViewBag.Patients     = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
            ViewBag.VaccineTypes = await _context.VaccineTypes.OrderBy(v => v.Name).ToListAsync();
            return View(model);
        }

        model.NextDueDate = model.AdministeredDate.AddDays(vaccineType.ValidityDays);
        if (!ModelState.IsValid)
        {
            ViewBag.Patients     = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
            ViewBag.VaccineTypes = await _context.VaccineTypes.OrderBy(v => v.Name).ToListAsync();
            return View(model);
        }

        _context.VaccinationRecords.Add(model);
        await _context.SaveChangesAsync(); // ApplyTenantRules() ClinicID'yi burada atar
        TempData["Success"] = "Aşı kaydı başarıyla eklendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var record = await _context.VaccinationRecords.FindAsync(id);
        if (record == null) return NotFound();

        ViewBag.Patients = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
        ViewBag.VaccineTypes = await _context.VaccineTypes.OrderBy(v => v.Name).ToListAsync();
        return View(record);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        [Bind("ID,PatientId,VaccineTypeId,AdministeredDate")] VaccinationRecord model)
    {
        var existing = await _context.VaccinationRecords.FindAsync(model.ID); // Global Query Filter: başka klinik = null
        if (existing == null) return NotFound();

        var patientExists = await _context.Patients.AnyAsync(p => p.ID == model.PatientId);
        var vaccineType = await _context.VaccineTypes.FindAsync(model.VaccineTypeId);

        // Sistem alanları, navigasyon özellikleri ve hesaplanan alanlar doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(VaccinationRecord.ClinicID));
        ModelState.Remove(nameof(VaccinationRecord.CreatedAt));
        ModelState.Remove(nameof(VaccinationRecord.IsActive));
        ModelState.Remove(nameof(VaccinationRecord.Patient));
        ModelState.Remove(nameof(VaccinationRecord.VaccineType));
        ModelState.Remove(nameof(VaccinationRecord.NextDueDate));
        ModelState.Remove(nameof(VaccinationRecord.IsReminderSent));

        if (!patientExists || vaccineType == null)
        {
            ModelState.AddModelError("", "Geçerli bir hasta ve aşı tipi seçin.");
            ViewBag.Patients = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
            ViewBag.VaccineTypes = await _context.VaccineTypes.OrderBy(v => v.Name).ToListAsync();
            return View(model);
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Patients = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
            ViewBag.VaccineTypes = await _context.VaccineTypes.OrderBy(v => v.Name).ToListAsync();
            return View(model);
        }

        existing.PatientId       = model.PatientId;
        existing.VaccineTypeId   = model.VaccineTypeId;
        existing.AdministeredDate = model.AdministeredDate;
        existing.NextDueDate     = model.AdministeredDate.AddDays(vaccineType.ValidityDays);
        // IsReminderSent, ClinicID, CreatedAt, IsActive → hiç dokunulmaz ✅

        await _context.SaveChangesAsync();
        TempData["Success"] = "Aşı kaydı başarıyla güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var record = await _context.VaccinationRecords.FindAsync(id);
        if (record != null)
        {
            _context.VaccinationRecords.Remove(record);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Aşı kaydı başarıyla silindi.";
        }
        return RedirectToAction(nameof(Index));
    }
}
