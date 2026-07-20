using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Patients;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public sealed class PatientController : Controller
{
    private readonly IPatientService _service;

    public PatientController(IPatientService service) => _service = service;

    public async Task<IActionResult> Index(string? search, bool partial = false, CancellationToken cancellationToken = default)
    {
        ViewBag.Search = search;
        var patients = await _service.ListAsync(search, cancellationToken: cancellationToken);
        if (partial || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return PartialView("_PatientTablePartial", patients);
        return View(patients);
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q, CancellationToken cancellationToken)
    {
        var patients = await _service.SearchAsync(q, cancellationToken);
        return Json(patients.Select(patient => new
        {
            id = patient.ID,
            label = patient.Name + " (" + patient.Species + ")",
            meta = string.Join(" · ", patient.Owners
                .Where(owner => owner.PetOwner != null)
                .Select(owner => owner.PetOwner.FirstName + " " + owner.PetOwner.LastName + " " + owner.PetOwner.Phone)
                .Take(2)),
            detail = (patient.Breed ?? "-") + (patient.MicrochipNumber != null ? " · Çip: " + patient.MicrochipNumber : "")
        }));
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var details = await _service.GetDetailsAsync(id, cancellationToken: cancellationToken);
        if (details == null) return NotFound();
        ViewBag.AvailableOwners = details.AvailableOwners;
        ViewBag.Muayeneler = details.Examinations;
        ViewBag.Vaccinations = details.Vaccinations;
        ViewBag.Appointments = details.Appointments;
        ViewBag.Debts = details.Debts;
        return View(details.Patient);
    }

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        ViewBag.Owners = await _service.GetActiveOwnersAsync(cancellationToken);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Name,Species,Breed,cinsiyet,DateOfBirth,MicrochipNumber,pasaportNumarasi,Notes")] Patient model,
        Guid? ownerId,
        CancellationToken cancellationToken)
    {
        RemoveSystemValidation();
        if (!ModelState.IsValid)
        {
            ViewBag.Owners = await _service.GetActiveOwnersAsync(cancellationToken);
            return View(model);
        }

        var result = await _service.CreateAsync(model, ownerId, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Hasta kaydedilemedi.");
            ViewBag.Owners = await _service.GetActiveOwnersAsync(cancellationToken);
            return View(model);
        }

        TempData["Success"] = $"{model.Name} başarıyla eklendi.";
        return RedirectToAction(nameof(Details), new { id = result.Patient!.ID });
    }

    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var patient = await _service.GetAsync(id, cancellationToken: cancellationToken);
        return patient == null ? NotFound() : View(patient);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        [Bind("ID,Name,Species,Breed,cinsiyet,DateOfBirth,MicrochipNumber,pasaportNumarasi,Notes")] Patient model,
        CancellationToken cancellationToken)
    {
        if (id != model.ID) return BadRequest();
        RemoveSystemValidation();
        if (!ModelState.IsValid) return View(model);

        var result = await _service.UpdateAsync(model, cancellationToken);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Hasta güncellenemedi.");
            return View(model);
        }

        TempData["Success"] = $"{model.Name ?? "Hasta"} başarıyla güncellendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.ArchiveAsync(id, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Hasta arşivlendi; tıbbi geçmiş korundu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.RestoreAsync(id, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Hasta yeniden aktifleştirildi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddOwner(Guid patientId, Guid ownerId, CancellationToken cancellationToken)
    {
        var result = await _service.AddOwnerAsync(patientId, ownerId, cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Yeni sahip başarıyla eklendi.";
        return RedirectToAction(nameof(Details), new { id = patientId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveOwner(Guid patientId, Guid ownerId, CancellationToken cancellationToken)
    {
        var result = await _service.RemoveOwnerAsync(patientId, ownerId, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Sahip bağlantısı arşivlendi.";
        return RedirectToAction(nameof(Details), new { id = patientId });
    }

    private void RemoveSystemValidation()
    {
        ModelState.Remove(nameof(Patient.ClinicID));
        ModelState.Remove(nameof(Patient.CreatedAt));
        ModelState.Remove(nameof(Patient.IsActive));
        ModelState.Remove(nameof(Patient.Owners));
    }

    private Guid ActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
