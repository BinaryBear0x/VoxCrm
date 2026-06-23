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
    public async Task<IActionResult> Create(VaccinationRecord model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Patients     = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
            ViewBag.VaccineTypes = await _context.VaccineTypes.OrderBy(v => v.Name).ToListAsync();
            return View(model);
        }

        var vaccineType = await _context.VaccineTypes.FindAsync(model.VaccineTypeId);
        if (vaccineType != null)
            model.NextDueDate = model.AdministeredDate.AddDays(vaccineType.ValidityDays);

        _context.VaccinationRecords.Add(model);
        await _context.SaveChangesAsync();
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
    public async Task<IActionResult> Edit(VaccinationRecord model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Patients = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
            ViewBag.VaccineTypes = await _context.VaccineTypes.OrderBy(v => v.Name).ToListAsync();
            return View(model);
        }

        var existing = await _context.VaccinationRecords.FindAsync(model.ID);
        if (existing == null) return NotFound();

        existing.PatientId = model.PatientId;
        existing.VaccineTypeId = model.VaccineTypeId;
        existing.AdministeredDate = model.AdministeredDate;

        var vaccineType = await _context.VaccineTypes.FindAsync(model.VaccineTypeId);
        if (vaccineType != null)
            existing.NextDueDate = model.AdministeredDate.AddDays(vaccineType.ValidityDays);

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
