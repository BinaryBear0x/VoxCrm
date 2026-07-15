using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Vaccinations;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public sealed class VaccineTypeController : Controller
{
    private readonly IVaccineTypeService _service;

    public VaccineTypeController(IVaccineTypeService service) => _service = service;

    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await _service.ListAsync(cancellationToken: cancellationToken));

    public IActionResult Create() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Name,ValidityDays,ReminderDaysBefore")] VaccineType model,
        CancellationToken cancellationToken)
    {
        RemoveSystemValidation();
        if (!ModelState.IsValid) return View(model);
        var result = await _service.CreateAsync(model, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Aşı türü kaydedilemedi.");
            return View(model);
        }

        TempData["Success"] = $"{model.Name} başarıyla eklendi.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var item = await _service.GetAsync(id, cancellationToken: cancellationToken);
        return item == null ? NotFound() : View(item);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        [Bind("ID,Name,ValidityDays,ReminderDaysBefore")] VaccineType model,
        CancellationToken cancellationToken)
    {
        if (id != model.ID) return BadRequest();
        RemoveSystemValidation();
        if (!ModelState.IsValid) return View(model);
        var result = await _service.UpdateAsync(model, cancellationToken);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Aşı türü güncellenemedi.");
            return View(model);
        }

        TempData["Success"] = $"{model.Name} başarıyla güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.ArchiveAsync(id, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Aşı türü arşivlendi; geçmiş kayıtlar korundu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.RestoreAsync(id, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        return RedirectToAction(nameof(Index));
    }

    private void RemoveSystemValidation()
    {
        ModelState.Remove(nameof(VaccineType.ClinicID));
        ModelState.Remove(nameof(VaccineType.CreatedAt));
        ModelState.Remove(nameof(VaccineType.IsActive));
    }

    private Guid ActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
