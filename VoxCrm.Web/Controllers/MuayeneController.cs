using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public class MuayeneController : Controller
{
    private readonly VoxCrmDbContext _context;
    public MuayeneController(VoxCrmDbContext context) => _context = context;

    public async Task<IActionResult> Index()
    {
        var list = await _context.Muayeneler
            .Include(m => m.Patient)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Create(Guid? patientId)
    {
        var patients = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
        ViewBag.Patients = new SelectList(patients, "ID", "Name", patientId);
        
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Muayene model)
    {
        if (!ModelState.IsValid)
        {
            var patients = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
            ViewBag.Patients = new SelectList(patients, "ID", "Name", model.PatientId);
            return View(model);
        }

        var patientExists = await _context.Patients.AnyAsync(p => p.ID == model.PatientId);
        if (!patientExists)
        {
            ModelState.AddModelError(nameof(model.PatientId), "Geçerli bir hasta seçin.");
            var patients = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
            ViewBag.Patients = new SelectList(patients, "ID", "Name", model.PatientId);
            return View(model);
        }

        _context.Muayeneler.Add(model);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Muayene kaydı başarıyla oluşturuldu.";
        
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var muayene = await _context.Muayeneler.FindAsync(id);
        if (muayene == null) return NotFound();

        var patients = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
        ViewBag.Patients = new SelectList(patients, "ID", "Name", muayene.PatientId);

        return View(muayene);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Muayene model)
    {
        if (id != model.ID) return BadRequest();

        if (!ModelState.IsValid)
        {
            var patients = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
            ViewBag.Patients = new SelectList(patients, "ID", "Name", model.PatientId);
            return View(model);
        }

        var existing = await _context.Muayeneler.FindAsync(id);
        if (existing == null) return NotFound();

        var patientExists = await _context.Patients.AnyAsync(p => p.ID == model.PatientId);
        if (!patientExists)
        {
            ModelState.AddModelError(nameof(model.PatientId), "Geçerli bir hasta seçin.");
            var patients = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
            ViewBag.Patients = new SelectList(patients, "ID", "Name", model.PatientId);
            return View(model);
        }

        existing.PatientId = model.PatientId;
        existing.Subjective = model.Subjective;
        existing.Objective = model.Objective;
        existing.Assessment = model.Assessment;
        existing.Plan = model.Plan;
        existing.WeightAtVisit = model.WeightAtVisit;
        existing.Temperature = model.Temperature;

        await _context.SaveChangesAsync();
        TempData["Success"] = "Muayene kaydı güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var muayene = await _context.Muayeneler
            .Include(m => m.Patient)
            .FirstOrDefaultAsync(m => m.ID == id);
            
        if (muayene == null) return NotFound();

        return View(muayene);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var muayene = await _context.Muayeneler.FindAsync(id);
        if (muayene == null) return NotFound();

        _context.Muayeneler.Remove(muayene);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Muayene kaydı silindi.";
        return RedirectToAction(nameof(Index));
    }
}
