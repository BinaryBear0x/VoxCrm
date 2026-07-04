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
    public async Task<IActionResult> Create(
        [Bind("PatientId,AppointmentId,Subjective,Objective,Assessment,Plan,WeightAtVisit,Temperature")] Muayene model)
    {
        // Sistem alanları ve navigasyon özellikleri doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(Muayene.ClinicID));
        ModelState.Remove(nameof(Muayene.CreatedAt));
        ModelState.Remove(nameof(Muayene.IsActive));
        ModelState.Remove(nameof(Muayene.Patient));
        ModelState.Remove(nameof(Muayene.Appointment));

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
        await _context.SaveChangesAsync(); // ApplyTenantRules() ClinicID'yi burada atar
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
    public async Task<IActionResult> Edit(Guid id,
        [Bind("ID,PatientId,AppointmentId,Subjective,Objective,Assessment,Plan,WeightAtVisit,Temperature")] Muayene model)
    {
        if (id != model.ID) return BadRequest();

        // Sistem alanları ve navigasyon özellikleri doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(Muayene.ClinicID));
        ModelState.Remove(nameof(Muayene.CreatedAt));
        ModelState.Remove(nameof(Muayene.IsActive));
        ModelState.Remove(nameof(Muayene.Patient));
        ModelState.Remove(nameof(Muayene.Appointment));

        if (!ModelState.IsValid)
        {
            var patients = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
            ViewBag.Patients = new SelectList(patients, "ID", "Name", model.PatientId);
            return View(model);
        }

        var existing = await _context.Muayeneler.FindAsync(id); // Global Query Filter: başka klinik = null
        if (existing == null) return NotFound();

        var patientExists = await _context.Patients.AnyAsync(p => p.ID == model.PatientId);
        if (!patientExists)
        {
            ModelState.AddModelError(nameof(model.PatientId), "Geçerli bir hasta seçin.");
            var patients = await _context.Patients.OrderBy(p => p.Name).ToListAsync();
            ViewBag.Patients = new SelectList(patients, "ID", "Name", model.PatientId);
            return View(model);
        }

        existing.PatientId     = model.PatientId;
        existing.Subjective    = model.Subjective;
        existing.Objective     = model.Objective;
        existing.Assessment    = model.Assessment;
        existing.Plan          = model.Plan;
        existing.WeightAtVisit = model.WeightAtVisit;
        existing.Temperature   = model.Temperature;
        // ClinicID, CreatedAt, IsActive → hiç dokunulmaz ✅

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
