using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public class PatientController : Controller
{
    private readonly VoxCrmDbContext _context;
    public PatientController(VoxCrmDbContext context) => _context = context;

    public async Task<IActionResult> Index(string? search)
    {
        var query = _context.Patients.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.Species.Contains(search));

        var list = await query.OrderBy(p => p.Name).ToListAsync();
        ViewBag.Search = search;
        return View(list);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var patient = await _context.Patients
            .Include(p => p.Owners).ThenInclude(o => o.PetOwner)
            .FirstOrDefaultAsync(p => p.ID == id);
        if (patient == null) return NotFound();

        // Sahip ekleme dropdown'u için hastanın henüz sahibi olmayan kişileri listele
        var existingOwnerIds = patient.Owners.Select(o => o.PetOwnerId).ToList();
        ViewBag.AvailableOwners = await _context.PetOwners
            .Where(o => !existingOwnerIds.Contains(o.ID))
            .OrderBy(o => o.FirstName)
            .ToListAsync();

        ViewBag.Muayeneler = await _context.Muayeneler.Where(m => m.PatientId == id).OrderByDescending(m => m.CreatedAt).ToListAsync();
        ViewBag.Vaccinations = await _context.VaccinationRecords.Include(v => v.VaccineType).Where(v => v.PatientId == id).OrderByDescending(v => v.AdministeredDate).ToListAsync();
        ViewBag.Appointments = await _context.Appointments.Where(a => a.PatientId == id).OrderByDescending(a => a.ScheduledAt).ToListAsync();
        ViewBag.Debts = await _context.Borçlar.Include(d => d.PetOwner).Where(d => existingOwnerIds.Contains(d.PetOwnerId)).OrderByDescending(d => d.DueDate).ToListAsync();

        return View(patient);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Owners = await _context.PetOwners.OrderBy(o => o.FirstName).ToListAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Patient model, Guid? ownerId)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Owners = await _context.PetOwners.OrderBy(o => o.FirstName).ToListAsync();
            return View(model);
        }

        _context.Patients.Add(model);
        await _context.SaveChangesAsync();

        if (ownerId.HasValue && ownerId.Value != Guid.Empty)
        {
            _context.PatientOwners.Add(new PatientOwner
            {
                PatientId    = model.ID,
                PetOwnerId   = ownerId.Value,
                IsPrimaryOwner = true
            });
            await _context.SaveChangesAsync();
        }

        TempData["Success"] = $"{model.Name} başarıyla eklendi.";
        return RedirectToAction(nameof(Details), new { id = model.ID });
    }

    // GET: /Patient/Edit/{id}
    public async Task<IActionResult> Edit(Guid id)
    {
        var patient = await _context.Patients.FindAsync(id);
        if (patient == null) return NotFound();
        return View(patient);
    }

    // POST: /Patient/Edit/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Patient model)
    {
        if (id != model.ID) return BadRequest();
        if (!ModelState.IsValid) return View(model);

        var existing = await _context.Patients.FindAsync(id);
        if (existing == null) return NotFound();

        existing.Name = model.Name;
        existing.Species = model.Species;
        existing.Breed = model.Breed;
        existing.cinsiyet = model.cinsiyet;
        existing.DateOfBirth = model.DateOfBirth;
        existing.MicrochipNumber = model.MicrochipNumber;
        existing.pasaportNumarasi = model.pasaportNumarasi;
        existing.Notes = model.Notes;

        await _context.SaveChangesAsync();
        TempData["Success"] = $"{model.Name} başarıyla güncellendi.";
        return RedirectToAction(nameof(Details), new { id = model.ID });
    }

    // POST: /Patient/Delete/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var patient = await _context.Patients.FindAsync(id);
        if (patient != null)
        {
            _context.Patients.Remove(patient);
            await _context.SaveChangesAsync();
            TempData["Success"] = $"{patient.Name} silindi.";
        }
        return RedirectToAction(nameof(Index));
    }

    // POST: /Patient/AddOwner
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddOwner(Guid patientId, Guid ownerId)
    {
        if (patientId == Guid.Empty || ownerId == Guid.Empty) return BadRequest();

        var exists = await _context.PatientOwners.AnyAsync(po => po.PatientId == patientId && po.PetOwnerId == ownerId);
        if (!exists)
        {
            _context.PatientOwners.Add(new PatientOwner { PatientId = patientId, PetOwnerId = ownerId });
            await _context.SaveChangesAsync();
            TempData["Success"] = "Yeni sahip başarıyla eklendi.";
        }
        return RedirectToAction(nameof(Details), new { id = patientId });
    }

    // POST: /Patient/RemoveOwner
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveOwner(Guid patientId, Guid ownerId)
    {
        var po = await _context.PatientOwners.FirstOrDefaultAsync(x => x.PatientId == patientId && x.PetOwnerId == ownerId);
        if (po != null)
        {
            _context.PatientOwners.Remove(po);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Sahip bağlantısı kaldırıldı.";
        }
        return RedirectToAction(nameof(Details), new { id = patientId });
    }
}
