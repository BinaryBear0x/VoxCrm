using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Web.Controllers;


[Authorize(Roles = "Clinic")]
public class PetOwnerController : Controller
{
    private readonly VoxCrmDbContext _context;

    public PetOwnerController(VoxCrmDbContext context) => _context = context;

    // GET: /PetOwner
    public async Task<IActionResult> Index(string? search)
    {
        var query = _context.PetOwners.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.FirstName.Contains(search) ||
                                     p.LastName.Contains(search) ||
                                     p.Phone.Contains(search));

        var owners = await query.OrderBy(p => p.FirstName).ToListAsync();
        ViewBag.Search = search;
        return View(owners);
    }

    // GET: /PetOwner/Details/{id}
    public async Task<IActionResult> Details(Guid id)
    {
        var owner = await _context.PetOwners
            .Include(o => o.OwnedPatients)
                .ThenInclude(po => po.Patient)
            .FirstOrDefaultAsync(o => o.ID == id);

        if (owner == null) return NotFound();

        var existingPatientIds = owner.OwnedPatients.Select(op => op.PatientId).ToList();
        ViewBag.AvailablePatients = await _context.Patients
            .Where(p => !existingPatientIds.Contains(p.ID))
            .OrderBy(p => p.Name)
            .ToListAsync();

        return View(owner);
    }

    // GET: /PetOwner/Create
    public IActionResult Create() => View();

    // POST: /PetOwner/Create
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PetOwner model)
    {
        if (!ModelState.IsValid) return View(model);

        _context.PetOwners.Add(model);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"{model.FirstName} {model.LastName} başarıyla eklendi.";
        return RedirectToAction(nameof(Details), new { id = model.ID });
    }

    // GET: /PetOwner/Edit/{id}
    public async Task<IActionResult> Edit(Guid id)
    {
        var owner = await _context.PetOwners.FindAsync(id);
        if (owner == null) return NotFound();
        return View(owner);
    }

    // POST: /PetOwner/Edit/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, PetOwner model)
    {
        if (id != model.ID) return BadRequest();
        if (!ModelState.IsValid) return View(model);

        var existing = await _context.PetOwners.FindAsync(id);
        if (existing == null) return NotFound();

        existing.FirstName = model.FirstName;
        existing.LastName = model.LastName;
        existing.Phone = model.Phone;
        existing.Email = model.Email;
        existing.Address = model.Address;
        existing.WhatsAppConsent = model.WhatsAppConsent;
        existing.Notes = model.Notes;

        await _context.SaveChangesAsync();
        TempData["Success"] = $"{model.FirstName} {model.LastName} başarıyla güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /PetOwner/Delete/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var owner = await _context.PetOwners.FindAsync(id);
        if (owner != null)
        {
            _context.PetOwners.Remove(owner);
            await _context.SaveChangesAsync();
            TempData["Success"] = $"{owner.FirstName} {owner.LastName} silindi.";
        }
        return RedirectToAction(nameof(Index));
    }

    // POST: /PetOwner/AddPatient
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPatient(Guid ownerId, Guid patientId)
    {
        if (ownerId == Guid.Empty || patientId == Guid.Empty) return BadRequest();

        var exists = await _context.PatientOwners.AnyAsync(po => po.PetOwnerId == ownerId && po.PatientId == patientId);
        if (!exists)
        {
            _context.PatientOwners.Add(new PatientOwner { PetOwnerId = ownerId, PatientId = patientId, IsPrimaryOwner = true });
            await _context.SaveChangesAsync();
            TempData["Success"] = "Hasta başarıyla eklendi.";
        }
        return RedirectToAction(nameof(Details), new { id = ownerId });
    }

    // POST: /PetOwner/RemovePatient
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePatient(Guid ownerId, Guid patientId)
    {
        var po = await _context.PatientOwners.FirstOrDefaultAsync(x => x.PetOwnerId == ownerId && x.PatientId == patientId);
        if (po != null)
        {
            _context.PatientOwners.Remove(po);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Hasta bağlantısı kaldırıldı.";
        }
        return RedirectToAction(nameof(Details), new { id = ownerId });
    }
}
