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
        var query = _context.Patients
            .Include(p => p.Owners)
                .ThenInclude(po => po.PetOwner)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                (p.Name != null && p.Name.Contains(term)) ||
                (p.Species != null && p.Species.Contains(term)) ||
                (p.Breed != null && p.Breed.Contains(term)) ||
                (p.MicrochipNumber != null && p.MicrochipNumber.Contains(term)) ||
                (p.pasaportNumarasi != null && p.pasaportNumarasi.Contains(term)) ||
                (p.Notes != null && p.Notes.Contains(term)) ||
                p.Owners.Any(o =>
                    (o.PetOwner.FirstName != null && o.PetOwner.FirstName.Contains(term)) ||
                    (o.PetOwner.LastName != null && o.PetOwner.LastName.Contains(term)) ||
                    (o.PetOwner.Phone != null && o.PetOwner.Phone.Contains(term)) ||
                    (o.PetOwner.Email != null && o.PetOwner.Email.Contains(term))));
        }

        var list = await query.OrderBy(p => p.Name).ToListAsync();
        ViewBag.Search = search;
        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q)
    {
        var query = _context.Patients
            .Include(p => p.Owners)
                .ThenInclude(po => po.PetOwner)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p =>
                (p.Name != null && p.Name.Contains(term)) ||
                (p.Species != null && p.Species.Contains(term)) ||
                (p.Breed != null && p.Breed.Contains(term)) ||
                (p.MicrochipNumber != null && p.MicrochipNumber.Contains(term)) ||
                (p.pasaportNumarasi != null && p.pasaportNumarasi.Contains(term)) ||
                (p.Notes != null && p.Notes.Contains(term)) ||
                p.Owners.Any(o =>
                    (o.PetOwner.FirstName != null && o.PetOwner.FirstName.Contains(term)) ||
                    (o.PetOwner.LastName != null && o.PetOwner.LastName.Contains(term)) ||
                    (o.PetOwner.Phone != null && o.PetOwner.Phone.Contains(term)) ||
                    (o.PetOwner.Email != null && o.PetOwner.Email.Contains(term)) ||
                    (o.PetOwner.Address != null && o.PetOwner.Address.Contains(term)) ||
                    (o.PetOwner.Notes != null && o.PetOwner.Notes.Contains(term))));
        }

        var patients = await query
            .OrderBy(p => p.Name)
            .Take(20)
            .ToListAsync();

        var results = patients.Select(p => new
            {
                id = p.ID,
                label = p.Name + " (" + p.Species + ")",
                meta = string.Join(" · ", p.Owners
                    .Where(o => o.PetOwner != null)
                    .Select(o => o.PetOwner.FirstName + " " + o.PetOwner.LastName + " " + o.PetOwner.Phone)
                    .Take(2)),
                detail = (p.Breed ?? "-") + (p.MicrochipNumber != null ? " · Çip: " + p.MicrochipNumber : "")
            })
            .ToList();

        return Json(results);
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
    public async Task<IActionResult> Create(
        [Bind("Name,Species,Breed,cinsiyet,DateOfBirth,MicrochipNumber,pasaportNumarasi,Notes")] Patient model,
        Guid? ownerId)
    {
        // Sistem alanları ve navigasyon özellikleri doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(Patient.ClinicID));
        ModelState.Remove(nameof(Patient.CreatedAt));
        ModelState.Remove(nameof(Patient.IsActive));
        ModelState.Remove(nameof(Patient.Owners));

        if (!ModelState.IsValid)
        {
            ViewBag.Owners = await _context.PetOwners.OrderBy(o => o.FirstName).ToListAsync();
            return View(model);
        }

        _context.Patients.Add(model);
        await _context.SaveChangesAsync(); // ApplyTenantRules() ClinicID'yi burada atar

        if (ownerId.HasValue && ownerId.Value != Guid.Empty)
        {
            var ownerExists = await _context.PetOwners.AnyAsync(o => o.ID == ownerId.Value);
            if (!ownerExists) return BadRequest("Geçersiz sahip seçimi.");

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
    public async Task<IActionResult> Edit(Guid id,
        [Bind("ID,Name,Species,Breed,cinsiyet,DateOfBirth,MicrochipNumber,pasaportNumarasi,Notes")] Patient model)
    {
        if (id != model.ID) return BadRequest();

        // Sistem alanları ve navigasyon özellikleri doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(Patient.ClinicID));
        ModelState.Remove(nameof(Patient.CreatedAt));
        ModelState.Remove(nameof(Patient.IsActive));
        ModelState.Remove(nameof(Patient.Owners));

        if (!ModelState.IsValid) return View(model);

        var existing = await _context.Patients.FindAsync(id); // Global Query Filter: başka klinik = null
        if (existing == null) return NotFound();

        existing.Name             = model.Name;
        existing.Species          = model.Species;
        existing.Breed            = model.Breed;
        existing.cinsiyet         = model.cinsiyet;
        existing.DateOfBirth      = model.DateOfBirth;
        existing.MicrochipNumber  = model.MicrochipNumber;
        existing.pasaportNumarasi = model.pasaportNumarasi;
        existing.Notes            = model.Notes;
        // ClinicID, CreatedAt, IsActive → hiç dokunulmaz ✅

        await _context.SaveChangesAsync();
        TempData["Success"] = $"{model.Name ?? "Hasta"} başarıyla güncellendi.";
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

        var patientExists = await _context.Patients.AnyAsync(p => p.ID == patientId);
        var ownerExists = await _context.PetOwners.AnyAsync(o => o.ID == ownerId);
        if (!patientExists || !ownerExists) return NotFound();

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
