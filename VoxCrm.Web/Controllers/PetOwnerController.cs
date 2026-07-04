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
        var query = _context.PetOwners
            .Include(o => o.OwnedPatients)
                .ThenInclude(po => po.Patient)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                (p.FirstName != null && p.FirstName.Contains(term)) ||
                (p.LastName != null && p.LastName.Contains(term)) ||
                (p.Phone != null && p.Phone.Contains(term)) ||
                (p.Email != null && p.Email.Contains(term)) ||
                (p.Address != null && p.Address.Contains(term)) ||
                (p.Notes != null && p.Notes.Contains(term)) ||
                p.OwnedPatients.Any(op =>
                    (op.Patient.Name != null && op.Patient.Name.Contains(term)) ||
                    (op.Patient.Species != null && op.Patient.Species.Contains(term)) ||
                    (op.Patient.Breed != null && op.Patient.Breed.Contains(term)) ||
                    (op.Patient.MicrochipNumber != null && op.Patient.MicrochipNumber.Contains(term)) ||
                    (op.Patient.pasaportNumarasi != null && op.Patient.pasaportNumarasi.Contains(term))));
        }

        var owners = await query.OrderBy(p => p.FirstName).ToListAsync();
        ViewBag.Search = search;
        return View(owners);
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q)
    {
        var query = _context.PetOwners
            .Include(o => o.OwnedPatients)
                .ThenInclude(po => po.Patient)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(o =>
                (o.FirstName != null && o.FirstName.Contains(term)) ||
                (o.LastName != null && o.LastName.Contains(term)) ||
                (o.Phone != null && o.Phone.Contains(term)) ||
                (o.Email != null && o.Email.Contains(term)) ||
                (o.Address != null && o.Address.Contains(term)) ||
                (o.Notes != null && o.Notes.Contains(term)) ||
                o.OwnedPatients.Any(op =>
                    (op.Patient.Name != null && op.Patient.Name.Contains(term)) ||
                    (op.Patient.Species != null && op.Patient.Species.Contains(term)) ||
                    (op.Patient.Breed != null && op.Patient.Breed.Contains(term)) ||
                    (op.Patient.MicrochipNumber != null && op.Patient.MicrochipNumber.Contains(term)) ||
                    (op.Patient.pasaportNumarasi != null && op.Patient.pasaportNumarasi.Contains(term))));
        }

        var owners = await query
            .OrderBy(o => o.FirstName)
            .ThenBy(o => o.LastName)
            .Take(20)
            .ToListAsync();

        var results = owners.Select(o => new
            {
                id = o.ID,
                label = o.FirstName + " " + o.LastName,
                meta = o.Phone + (string.IsNullOrWhiteSpace(o.Email) ? "" : " · " + o.Email),
                detail = string.Join(", ", o.OwnedPatients
                    .Where(op => op.Patient != null)
                    .Select(op => op.Patient.Name + " (" + op.Patient.Species + ")")
                    .Take(3))
            })
            .ToList();

        return Json(results);
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
    public async Task<IActionResult> Create(
        [Bind("FirstName,LastName,Phone,Email,Address,WhatsAppConsent,Notes")] PetOwner model)
    {
        // Sistem alanları formda olmadığı için doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(PetOwner.ClinicID));
        ModelState.Remove(nameof(PetOwner.CreatedAt));
        ModelState.Remove(nameof(PetOwner.IsActive));

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Müşteri bilgileri kaydedilemedi. Lütfen formdaki alanları kontrol edin.";
            return View(model);
        }

        // Telefon numarası zaten kayıtlı mı?
        if (!string.IsNullOrWhiteSpace(model.Phone))
        {
            var duplicate = await _context.PetOwners
                .AnyAsync(p => p.Phone == model.Phone);
            if (duplicate)
            {
                TempData["Error"] = $"Bu telefon numarası ({model.Phone}) zaten kayıtlı.";
                return View(model);
            }
        }

        _context.PetOwners.Add(model);
        await _context.SaveChangesAsync(); // ApplyTenantRules() ClinicID'yi burada atar
        var displayName = string.Join(" ", new[] { model.FirstName, model.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        TempData["Success"] = $"{(string.IsNullOrWhiteSpace(displayName) ? "Müşteri" : displayName)} başarıyla eklendi.";
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
    public async Task<IActionResult> Edit(Guid id,
        [Bind("ID,FirstName,LastName,Phone,Email,Address,WhatsAppConsent,Notes")] PetOwner model)
    {
        if (id != model.ID) return BadRequest();

        // Sistem alanları formda olmadığı için doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(PetOwner.ClinicID));
        ModelState.Remove(nameof(PetOwner.CreatedAt));
        ModelState.Remove(nameof(PetOwner.IsActive));

        if (!ModelState.IsValid) return View(model);

        var existing = await _context.PetOwners.FindAsync(id); // Global Query Filter: başka klinik = null
        if (existing == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(model.Phone))
        {
            var duplicate = await _context.PetOwners
                .AnyAsync(p => p.ID != id && p.Phone == model.Phone);
            if (duplicate)
            {
                TempData["Error"] = $"Bu telefon numarası ({model.Phone}) başka bir müşteride kayıtlı.";
                return View(model);
            }
        }

        existing.FirstName       = model.FirstName;
        existing.LastName        = model.LastName;
        existing.Phone           = model.Phone;
        existing.Email           = model.Email;
        existing.Address         = model.Address;
        existing.WhatsAppConsent = model.WhatsAppConsent;
        existing.Notes           = model.Notes;
        // ClinicID, CreatedAt, IsActive → hiç dokunulmaz ✅

        await _context.SaveChangesAsync();
        var editName = string.Join(" ", new[] { model.FirstName, model.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        TempData["Success"] = $"{(string.IsNullOrWhiteSpace(editName) ? "Müşteri" : editName)} başarıyla güncellendi.";
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

        var ownerExists = await _context.PetOwners.AnyAsync(o => o.ID == ownerId);
        var patientExists = await _context.Patients.AnyAsync(p => p.ID == patientId);
        if (!ownerExists || !patientExists) return NotFound();

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
