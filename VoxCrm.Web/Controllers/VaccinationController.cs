using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Vaccinations;
using VoxCrm.Domain.Entities;
using VoxCrm.Web.Services;
using VoxCrm.Web.Models;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public sealed class VaccinationController : Controller
{
    private readonly IVaccinationService _service;
    private readonly IVaccinationReminderScheduler _reminderScheduler;

    public VaccinationController(
        IVaccinationService service,
        IVaccinationReminderScheduler reminderScheduler)
    {
        _service = service;
        _reminderScheduler = reminderScheduler;
    }

    public async Task<IActionResult> Index(bool includeArchived, bool history, CancellationToken cancellationToken)
    {
        ViewBag.IncludeArchived = includeArchived;
        var records = await _service.ListAsync(includeArchived, cancellationToken);
        ViewBag.History = history;
        return View(history ? records : records.Where(x => x.RenewedOn == null).ToList());
    }

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new VaccinationBatchViewModel();
        await PopulateBatch(model, cancellationToken); return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VaccinationBatchViewModel model, CancellationToken cancellationToken)
    {
        // Names, validity and choices always come from the database, never posted display fields.
        ModelState.Remove(nameof(model.Patients));
        if (ModelState.IsValid)
        {
            var result = await _service.CreateBatchAsync(new(model.PatientId, model.AdministeredDate, model.SubmissionId,
                model.Rows.Where(x => x.Selected).Select(x => new VaccinationBatchItem(x.VaccineTypeId, x.VaccineCode)).ToList()), ActorUserId(), cancellationToken);
            if (result.Succeeded)
            {
                TempData["Success"] = $"{result.Records.Count} uygulama birlikte kaydedildi. Geçmiş kayıtlar korunur; hatırlatma gönderimi ayrı ayarlara bağlıdır.";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Uygulamalar kaydedilemedi.");
        }
        await PopulateBatch(model, cancellationToken); return View(model);
    }

    private async Task PopulateBatch(VaccinationBatchViewModel model, CancellationToken ct)
    {
        var choices = await _service.GetChoicesAsync(ct);
        if (model.AdministeredDate == default) model.AdministeredDate = choices.Today ?? DateTime.UtcNow.Date;
        var selected = model.Rows.GroupBy(x => x.VaccineTypeId).ToDictionary(x => x.Key, x => x.First());
        model.Patients = choices.Patients;
        model.Rows = choices.VaccineTypes.Select(x => new VaccinationBatchRow { VaccineTypeId = x.ID, Name = x.Name, ValidityDays = x.ValidityDays,
            Selected = selected.TryGetValue(x.ID, out var old) && old.Selected, VaccineCode = selected.TryGetValue(x.ID, out var prior) ? prior.VaccineCode : null }).ToList();
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
        [Bind("ID,PatientId,VaccineTypeId,AdministeredDate,VaccineCode")] VaccinationRecord model,
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

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult RetryReminder(Guid id) => RedirectToAction("PrepareVaccination", "WhatsApp", new { id });

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SendReminderNow(Guid id) => RedirectToAction("PrepareVaccination", "WhatsApp", new { id });

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
