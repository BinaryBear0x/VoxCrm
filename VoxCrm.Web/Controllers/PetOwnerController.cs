using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.PetOwners;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public sealed class PetOwnerController : Controller
{
    private readonly IPetOwnerService _service;

    public PetOwnerController(IPetOwnerService service) => _service = service;

    public async Task<IActionResult> Index(string? search, bool partial = false, CancellationToken cancellationToken = default)
    {
        ViewBag.Search = search;
        var owners = await _service.ListAsync(search, cancellationToken: cancellationToken);
        if (partial || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return PartialView("_PetOwnerTablePartial", owners);
        return View(owners);
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q, CancellationToken cancellationToken)
    {
        var owners = await _service.SearchAsync(q, cancellationToken);
        return Json(owners.Select(owner => new
        {
            id = owner.ID,
            label = owner.FirstName + " " + owner.LastName,
            meta = owner.Phone + (string.IsNullOrWhiteSpace(owner.Email) ? "" : " · " + owner.Email),
            detail = string.Join(", ", owner.OwnedPatients
                .Where(link => link.Patient != null)
                .Select(link => link.Patient.Name + " (" + link.Patient.Species + ")")
                .Take(3))
        }));
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var details = await _service.GetDetailsAsync(id, cancellationToken: cancellationToken);
        if (details == null) return NotFound();
        ViewBag.AvailablePatients = details.AvailablePatients;
        return View(details.Owner);
    }

    public IActionResult Create() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("FirstName,LastName,Phone,Email,Address,WhatsAppConsent,Notes")] PetOwner model,
        CancellationToken cancellationToken)
    {
        RemoveSystemValidation();
        if (!ModelState.IsValid) return View(model);
        var result = await _service.CreateAsync(model, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Müşteri kaydedilemedi.");
            return View(model);
        }

        TempData["Success"] = "Müşteri başarıyla eklendi.";
        return RedirectToAction(nameof(Details), new { id = result.Owner!.ID });
    }

    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var owner = await _service.GetAsync(id, cancellationToken: cancellationToken);
        return owner == null ? NotFound() : View(owner);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        [Bind("ID,FirstName,LastName,Phone,Email,Address,WhatsAppConsent,Notes")] PetOwner model,
        CancellationToken cancellationToken)
    {
        if (id != model.ID) return BadRequest();
        RemoveSystemValidation();
        if (!ModelState.IsValid) return View(model);
        var result = await _service.UpdateAsync(model, cancellationToken);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Müşteri güncellenemedi.");
            return View(model);
        }

        TempData["Success"] = "Müşteri başarıyla güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.ArchiveAsync(id, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Müşteri arşivlendi; finansal geçmiş korundu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.RestoreAsync(id, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPatient(Guid ownerId, Guid patientId, CancellationToken cancellationToken)
    {
        var result = await _service.AddPatientAsync(ownerId, patientId, cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Hasta bağlantısı eklendi.";
        return RedirectToAction(nameof(Details), new { id = ownerId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePatient(Guid ownerId, Guid patientId, CancellationToken cancellationToken)
    {
        var result = await _service.RemovePatientAsync(ownerId, patientId, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Hasta bağlantısı arşivlendi.";
        return RedirectToAction(nameof(Details), new { id = ownerId });
    }

    private void RemoveSystemValidation()
    {
        ModelState.Remove(nameof(PetOwner.ClinicID));
        ModelState.Remove(nameof(PetOwner.CreatedAt));
        ModelState.Remove(nameof(PetOwner.IsActive));
    }

    private Guid ActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
