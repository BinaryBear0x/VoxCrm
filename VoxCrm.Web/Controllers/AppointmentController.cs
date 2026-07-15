using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Appointments;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public class AppointmentController : Controller
{
    private readonly IAppointmentService _appointments;

    public AppointmentController(IAppointmentService appointments)
    {
        _appointments = appointments;
    }

    public async Task<IActionResult> Index(
        string? status,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        if (status != null && !AppointmentRules.IsAllowedStatus(status))
            status = null;

        ViewBag.Status = status;
        ViewBag.IncludeArchived = includeArchived;
        return View(await _appointments.ListAsync(status, includeArchived, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        await PopulatePatientsAsync(cancellationToken);
        return View(new AppointmentCommand(
            Guid.Empty,
            DateTime.Today.AddDays(1).AddHours(9),
            AppointmentRules.AllowedTypes[0],
            30,
            null));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        AppointmentCommand command,
        bool confirmConflict,
        CancellationToken cancellationToken)
    {
        var result = await _appointments.CreateAsync(command, confirmConflict, cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = "Randevu başarıyla oluşturuldu.";
            return RedirectToAction(nameof(Index));
        }

        if (result.Outcome == AppointmentCommandOutcome.ConflictWarning)
            ViewBag.ConflictWarning = result.Error;
        else
            ModelState.AddModelError(string.Empty, result.Error ?? "Randevu oluşturulamadı.");

        await PopulatePatientsAsync(cancellationToken);
        return View(command);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        string status,
        CancellationToken cancellationToken)
    {
        var result = await _appointments.UpdateStatusAsync(id, status, cancellationToken);
        if (result.Outcome == AppointmentCommandOutcome.ValidationFailed)
            return BadRequest(result.Error);
        if (result.Outcome == AppointmentCommandOutcome.NotFound)
            return NotFound();

        TempData["Success"] = "Randevu durumu güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var appointment = await _appointments.GetAsync(id, cancellationToken);
        if (appointment == null)
            return NotFound();

        await PopulatePatientsAsync(cancellationToken);
        return View(appointment);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        AppointmentCommand command,
        bool confirmConflict,
        CancellationToken cancellationToken)
    {
        var result = await _appointments.UpdateAsync(id, command, confirmConflict, cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = "Randevu başarıyla güncellendi.";
            return RedirectToAction(nameof(Index));
        }
        if (result.Outcome == AppointmentCommandOutcome.NotFound)
            return NotFound();

        if (result.Outcome == AppointmentCommandOutcome.ConflictWarning)
            ViewBag.ConflictWarning = result.Error;
        else
            ModelState.AddModelError(string.Empty, result.Error ?? "Randevu güncellenemedi.");

        var existing = await _appointments.GetAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        await PopulatePatientsAsync(cancellationToken);
        return View(new AppointmentEditModel(
            id,
            command.PatientId,
            command.ScheduledAtLocal,
            command.DurationMinutes,
            command.AppointmentType,
            existing.Status,
            command.Reason));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _appointments.ArchiveAsync(id, ActorUserId(), cancellationToken);
        if (result.Outcome == AppointmentCommandOutcome.NotFound)
            return NotFound();

        TempData["Success"] = "Randevu arşivlendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _appointments.RestoreAsync(id, ActorUserId(), cancellationToken);
        if (result.Outcome == AppointmentCommandOutcome.NotFound)
            return NotFound();
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error ?? "Randevu geri alınamadı.";
            return RedirectToAction(nameof(Index), new { includeArchived = true });
        }

        TempData["Success"] = "Randevu geri alındı.";
        return RedirectToAction(nameof(Index), new { includeArchived = true });
    }

    private async Task PopulatePatientsAsync(CancellationToken cancellationToken)
    {
        ViewBag.Patients = await _appointments.GetPatientOptionsAsync(cancellationToken);
        ViewBag.AppointmentTypes = AppointmentRules.AllowedTypes;
    }

    private Guid ActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
