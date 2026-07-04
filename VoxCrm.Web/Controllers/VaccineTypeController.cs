using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public class VaccineTypeController : Controller
{
    private readonly VoxCrmDbContext _context;

    public VaccineTypeController(VoxCrmDbContext context)
    {
        _context = context;
    }

    // GET: /VaccineType
    public async Task<IActionResult> Index()
    {
        var vaccines = await _context.VaccineTypes.OrderBy(v => v.Name).ToListAsync();
        return View(vaccines);
    }

    // GET: /VaccineType/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: /VaccineType/Create
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Name,ValidityDays,ReminderDaysBefore")] VaccineType model)
    {
        // Sistem alanları doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(VaccineType.ClinicID));
        ModelState.Remove(nameof(VaccineType.CreatedAt));
        ModelState.Remove(nameof(VaccineType.IsActive));

        if (!ModelState.IsValid) return View(model);

        _context.VaccineTypes.Add(model);
        await _context.SaveChangesAsync(); // ApplyTenantRules() ClinicID'yi burada atar
        TempData["Success"] = $"{model.Name} başarıyla eklendi.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /VaccineType/Edit/{id}
    public async Task<IActionResult> Edit(Guid id)
    {
        var vaccine = await _context.VaccineTypes.FindAsync(id);
        if (vaccine == null) return NotFound();
        return View(vaccine);
    }

    // POST: /VaccineType/Edit/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id,
        [Bind("ID,Name,ValidityDays,ReminderDaysBefore")] VaccineType model)
    {
        if (id != model.ID) return BadRequest();

        // Sistem alanları doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(VaccineType.ClinicID));
        ModelState.Remove(nameof(VaccineType.CreatedAt));
        ModelState.Remove(nameof(VaccineType.IsActive));

        if (!ModelState.IsValid) return View(model);

        var existing = await _context.VaccineTypes.FindAsync(id); // Global Query Filter: başka klinik = null
        if (existing == null) return NotFound();

        existing.Name                = model.Name;
        existing.ValidityDays        = model.ValidityDays;
        existing.ReminderDaysBefore  = model.ReminderDaysBefore;
        // ClinicID, CreatedAt, IsActive → hiç dokunulmaz ✅

        await _context.SaveChangesAsync();
        TempData["Success"] = $"{model.Name} başarıyla güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /VaccineType/Delete/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var vaccine = await _context.VaccineTypes.FindAsync(id);
        if (vaccine != null)
        {
            _context.VaccineTypes.Remove(vaccine);
            await _context.SaveChangesAsync();
            TempData["Success"] = $"{vaccine.Name} silindi.";
        }
        return RedirectToAction(nameof(Index));
    }
}
