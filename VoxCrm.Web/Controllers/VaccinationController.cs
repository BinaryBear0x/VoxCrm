using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Vaccinations;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public sealed class VaccinationController : Controller
{
    private readonly IVaccinationService _service;

    public VaccinationController(IVaccinationService service) => _service = service;

    public async Task<IActionResult> Index(bool includeArchived, CancellationToken cancellationToken)
    {
        ViewBag.IncludeArchived = includeArchived;
        return View(await _service.ListAsync(includeArchived, cancellationToken));
    }

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        await SetChoicesAsync(cancellationToken);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("PatientId,VaccineTypeId,AdministeredDate")] VaccinationRecord model,
        CancellationToken cancellationToken)
    {
        RemoveSystemValidation();
        if (!ModelState.IsValid)
        {
            await SetChoicesAsync(cancellationToken);
            return View(model);
        }

        var result = await _service.CreateAsync(model, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Aşı kaydı oluşturulamadı.");
            await SetChoicesAsync(cancellationToken);
            return View(model);
        }

        TempData["Success"] = "Aşı kaydı başarıyla eklendi.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var record = await _service.GetAsync(id, cancellationToken: cancellationToken);
        if (record == null) return NotFound();
        await SetChoicesAsync(cancellationToken);
        return View(record);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        [Bind("ID,PatientId,VaccineTypeId,AdministeredDate")] VaccinationRecord model,
        CancellationToken cancellationToken)
    {
        RemoveSystemValidation();
        if (!ModelState.IsValid)
        {
            await SetChoicesAsync(cancellationToken);
            return View(model);
        }

        var result = await _service.UpdateAsync(model, cancellationToken);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Aşı kaydı güncellenemedi.");
            await SetChoicesAsync(cancellationToken);
            return View(model);
        }

        TempData["Success"] = "Aşı kaydı başarıyla güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.ArchiveAsync(id, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Aşı kaydı arşivlendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.RestoreAsync(id, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Aşı kaydı geri alındı.";
        return RedirectToAction(nameof(Index), new { includeArchived = true });
    }

    private async Task SetChoicesAsync(CancellationToken cancellationToken)
    {
        var choices = await _service.GetChoicesAsync(cancellationToken);
        ViewBag.Patients = choices.Patients;
        ViewBag.VaccineTypes = choices.VaccineTypes;
    }

    private void RemoveSystemValidation()
    {
        ModelState.Remove(nameof(VaccinationRecord.ClinicID));
        ModelState.Remove(nameof(VaccinationRecord.CreatedAt));
        ModelState.Remove(nameof(VaccinationRecord.IsActive));
        ModelState.Remove(nameof(VaccinationRecord.Patient));
        ModelState.Remove(nameof(VaccinationRecord.VaccineType));
        ModelState.Remove(nameof(VaccinationRecord.NextDueDate));
        ModelState.Remove(nameof(VaccinationRecord.IsReminderSent));
    }

    private Guid ActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
