using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VoxCrm.Application.Examinations;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public sealed class MuayeneController : Controller
{
    private readonly IExaminationService _service;

    public MuayeneController(IExaminationService service) => _service = service;

    public async Task<IActionResult> Index(bool includeArchived, CancellationToken cancellationToken)
    {
        ViewBag.IncludeArchived = includeArchived;
        return View(await _service.ListAsync(includeArchived, cancellationToken));
    }

    public async Task<IActionResult> Create(Guid? patientId, CancellationToken cancellationToken)
    {
        await SetPatientsAsync(patientId, cancellationToken);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("PatientId,AppointmentId,Subjective,Objective,Assessment,Plan,WeightAtVisit,Temperature")] Muayene model,
        CancellationToken cancellationToken)
    {
        RemoveSystemValidation();
        if (!ModelState.IsValid)
        {
            await SetPatientsAsync(model.PatientId, cancellationToken);
            return View(model);
        }
        var result = await _service.CreateAsync(model, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Muayene kaydedilemedi.");
            await SetPatientsAsync(model.PatientId, cancellationToken);
            return View(model);
        }
        TempData["Success"] = "Muayene kaydı başarıyla oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var item = await _service.GetAsync(id, cancellationToken);
        if (item == null) return NotFound();
        await SetPatientsAsync(item.PatientId, cancellationToken);
        return View(item);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        [Bind("ID,PatientId,AppointmentId,Subjective,Objective,Assessment,Plan,WeightAtVisit,Temperature")] Muayene model,
        CancellationToken cancellationToken)
    {
        if (id != model.ID) return BadRequest();
        RemoveSystemValidation();
        if (!ModelState.IsValid)
        {
            await SetPatientsAsync(model.PatientId, cancellationToken);
            return View(model);
        }
        var result = await _service.UpdateAsync(model, cancellationToken);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Muayene güncellenemedi.");
            await SetPatientsAsync(model.PatientId, cancellationToken);
            return View(model);
        }
        TempData["Success"] = "Muayene kaydı güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var item = await _service.GetAsync(id, cancellationToken);
        return item == null ? NotFound() : View(item);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.ArchiveAsync(id, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Muayene kaydı arşivlendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.RestoreAsync(id, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Muayene kaydı geri alındı.";
        return RedirectToAction(nameof(Index), new { includeArchived = true });
    }

    private async Task SetPatientsAsync(Guid? selected, CancellationToken cancellationToken) =>
        ViewBag.Patients = new SelectList(await _service.GetPatientOptionsAsync(cancellationToken), "ID", "Name", selected);

    private void RemoveSystemValidation()
    {
        ModelState.Remove(nameof(Muayene.ClinicID));
        ModelState.Remove(nameof(Muayene.CreatedAt));
        ModelState.Remove(nameof(Muayene.IsActive));
        ModelState.Remove(nameof(Muayene.Patient));
        ModelState.Remove(nameof(Muayene.Appointment));
    }

    private Guid ActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
