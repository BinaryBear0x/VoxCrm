using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Workday;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkdayController(IWorkdayService workday) : Controller
{
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    public async Task<IActionResult> Index(DateOnly? date, CancellationToken ct)
    {
        try { return View(await workday.GetAsync(date, ct)); } catch (WorkdayException e) { return Error(e); }
    }
    public async Task<IActionResult> Task(Guid? id, CancellationToken ct)
    {
        try
        {
            var page = await workday.GetAsync(null, ct);
            var task = id.HasValue ? await workday.GetTaskAsync(id.Value, ct) : new ClinicTask { DueDate = page.Date, SubmissionId = Guid.NewGuid() };
            if (task == null) return NotFound();
            ViewBag.Page = page; ViewBag.IsNew = !id.HasValue; return View(task);
        }
        catch (WorkdayException e) { return Error(e); }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTask(TaskInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Görev alanlarını ve tarihini kontrol edin."; return RedirectToAction(nameof(Task), new { id = input.Id }); }
        try { await workday.SaveTaskAsync(input, Actor, ct); TempData["Success"] = "Görev kaydedildi."; return RedirectToAction(nameof(Index)); }
        catch (WorkdayException e) { if (e.NotFound) return NotFound(); TempData["Error"] = e.Message; return RedirectToAction(nameof(Task), new { id = input.Id }); }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TaskStatus(Guid id, long revision, string status, string? returnUrl, CancellationToken ct)
    {
        try { await workday.SetTaskStatusAsync(id, revision, status, Actor, ct); TempData["Success"] = "Görev durumu güncellendi."; }
        catch (WorkdayException e) { if (e.NotFound) return NotFound(); TempData["Error"] = e.Message; }
        if (Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return RedirectToAction(nameof(Index));
    }
    public async Task<IActionResult> Care(Guid id, CancellationToken ct)
    {
        try { return View(await workday.GetCareEditorAsync(id, Actor, ct)); } catch (WorkdayException e) { return Error(e); }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCare(CareInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Bakım kartı alanlarını ve kontrol tarihini kontrol edin."; return RedirectToAction(nameof(Care), new { id = input.ExaminationId }); }
        try { await workday.SaveCareAsync(input, Actor, ct); TempData["Success"] = "Taslak kaydedildi. Önceki paylaşım bağlantısı varsa kapatıldı; yeniden veteriner onayı gerekir."; }
        catch (WorkdayException e) { if (e.NotFound) return NotFound(); TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Care), new { id = input.ExaminationId });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishCare(Guid id, long revision, bool veterinarianConfirmed, CancellationToken ct)
    {
        try
        {
            var token = await workday.PublishCareAsync(id, revision, Actor, veterinarianConfirmed, ct);
            ViewBag.ExaminationId = id; return View("CareLink", Url.Action("Index", "Care", new { token }, Request.Scheme));
        }
        catch (WorkdayException e) { if (e.NotFound) return NotFound(); TempData["Error"] = e.Message; return RedirectToAction(nameof(Care), new { id }); }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeCare(Guid id, long revision, CancellationToken ct)
    {
        try { await workday.RevokeCareAsync(id, revision, Actor, ct); TempData["Success"] = "Paylaşım bağlantısı kapatıldı."; }
        catch (WorkdayException e) { if (e.NotFound) return NotFound(); TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Care), new { id });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewBilling(Guid id, Guid? accountId, string reason, CancellationToken ct)
    {
        try { await workday.ReviewBillingAsync(id, accountId, reason, Actor, ct); TempData["Success"] = "Hesap kontrolü kaydedildi; yeni borç veya ödeme oluşturulmadı."; }
        catch (WorkdayException e) { if (e.NotFound) return NotFound(); TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Care), new { id });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Acknowledge(DateOnly date, string issueKey, string note, CancellationToken ct)
    {
        try { await workday.AcknowledgeAsync(date, issueKey, note, Actor, ct); TempData["Success"] = "Kontrol notu kaydedildi. Kaynak kayıt düzeltilene kadar uyarı görünür kalır."; }
        catch (WorkdayException e) { if (e.NotFound) return NotFound(); TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd"), section = "closing" });
    }
    private IActionResult Error(WorkdayException e) => e.NotFound ? NotFound() : BadRequest(e.Message);
}
