using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Finance;
namespace VoxCrm.Web.Controllers;
[Authorize(Roles = "Clinic")]
public sealed class FinanceController(IFinanceService service) : Controller
{
    public async Task<IActionResult> Index(string? search, string? status, bool showDeleted, CancellationToken ct) => View(await service.GetIndexAsync(search, status, showDeleted, ct));
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        try { return View(await service.GetDetailsAsync(id, ct)); }
        catch (FinanceException e) when (e.Error == FinanceError.NotFound) { return NotFound(); }
    }
    public async Task<IActionResult> Create(CancellationToken ct) { ViewBag.Choices = await service.GetChoicesAsync(ct); var today=await service.GetTodayAsync(ct); return View("Account", new FinanceAccountInput{ChargeDate=today,DueDate=today}); }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FinanceAccountInput input, CancellationToken ct)
    {
        if (ModelState.IsValid) try { var id = await service.CreateAsync(input, Actor(), ct); TempData["Success"] = "Borç kaydı oluşturuldu."; return RedirectToAction(nameof(Details), new { id }); }
        catch (FinanceException e) { ModelState.AddModelError(string.Empty, e.Message); }
        ViewBag.Choices = await service.GetChoicesAsync(ct); return View("Account", input);
    }
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        try {
            var d = await service.GetDetailsAsync(id, ct); if (!d.Account.IsActive) return RedirectToAction(nameof(Details), new { id });
            ViewBag.AccountId = id; ViewBag.Choices = await service.GetChoicesAsync(ct);
            return View("Account", new FinanceAccountInput { Revision = d.Account.Revision, CustomerMode = d.Account.PetOwnerId.HasValue ? "registered" : "manual", PetOwnerId = d.Account.PetOwnerId, PatientId = d.Account.PatientId, CustomerName = d.Account.CustomerName, Description = d.Account.Description, DueDate = d.Account.DueDate });
        } catch (FinanceException e) when (e.Error == FinanceError.NotFound) { return NotFound(); }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, FinanceAccountInput input, CancellationToken ct)
    {
        if (ModelState.IsValid) try { await service.UpdateAsync(id, input, Actor(), ct); TempData["Success"] = "Kayıt güncellendi."; return RedirectToAction(nameof(Details), new { id }); }
        catch (FinanceException e) { if (e.Error == FinanceError.NotFound) return NotFound(); ModelState.AddModelError(string.Empty, e.Message); }
        ViewBag.AccountId = id; ViewBag.Choices = await service.GetChoicesAsync(ct); return View("Account", input);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Delete(Guid id, long revision, CancellationToken ct) => Run(id, () => service.DeleteAsync(id, revision, Actor(), ct), "Kayıt silindi. İşlem geçmişi saklandı.", true);
    public async Task<IActionResult> Entry(Guid accountId, string kind, Guid? id, CancellationToken ct)
    {
        try {
            var d = await service.GetDetailsAsync(accountId, ct); if (!d.Account.IsActive) return NotFound();
            if (kind is not ("charge" or "installment" or "receipt")) return BadRequest();
            var today=await service.GetTodayAsync(ct);
            var input = new FinanceEntryInput { Date=today, AccountId = accountId, Kind = kind, Id = id, Revision = d.Account.Revision };
            if (id.HasValue) {
                if (kind == "charge") { var e = d.Account.Charges.SingleOrDefault(x => x.ID == id && x.IsActive); if (e == null) return NotFound(); input.Amount = e.Amount; input.Date = e.ChargeDate; input.Description = e.Description; }
                if (kind == "installment") { var e = d.Account.Installments.SingleOrDefault(x => x.ID == id && x.IsActive); if (e == null) return NotFound(); input.Amount = e.Amount; input.Date = e.DueDate; input.Description = e.Description; }
                if (kind == "receipt") { var e = d.Account.Receipts.SingleOrDefault(x => x.ID == id && x.IsActive); if (e == null) return NotFound(); input.Amount = e.Amount; input.Date = e.PaidDate; input.PaymentMethod = e.PaymentMethod; input.Notes = e.Notes; input.InstallmentId = e.FinanceInstallmentId; }
            }
            ViewBag.Details = d; return View(input);
        } catch (FinanceException e) when (e.Error == FinanceError.NotFound) { return NotFound(); }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Entry(FinanceEntryInput input, CancellationToken ct)
    {
        if (ModelState.IsValid) try { await service.SaveEntryAsync(input, Actor(), ct); TempData["Success"] = "Kayıt kaydedildi."; return RedirectToAction(nameof(Details), new { id = input.AccountId }); }
        catch (FinanceException e) { if (e.Error == FinanceError.NotFound) return NotFound(); ModelState.AddModelError(string.Empty, e.Message); }
        try { ViewBag.Details = await service.GetDetailsAsync(input.AccountId, ct); }
        catch (FinanceException) { return NotFound(); }
        return View(input);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> DeleteEntry(Guid accountId, string kind, Guid id, long revision, CancellationToken ct) => Run(accountId, () => service.DeleteEntryAsync(accountId, kind, id, revision, Actor(), ct), "Kayıt silindi. İşlem geçmişi saklandı.");
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> GenerateInstallments(Guid accountId, int count, DateOnly firstDate, long revision, CancellationToken ct) => Run(accountId, () => service.GenerateInstallmentsAsync(accountId, count, firstDate, revision, Actor(), ct), "Taksit planı oluşturuldu.");
    private async Task<IActionResult> Run(Guid id, Func<Task> command, string message, bool toIndex = false)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Geçerli tutar ve tarih girin."; return RedirectToAction(nameof(Details), new { id }); }
        try { await command(); TempData["Success"] = message; }
        catch (FinanceException e) { if (e.Error == FinanceError.NotFound) return NotFound(); TempData["Error"] = e.Message; return RedirectToAction(nameof(Details), new { id }); }
        return toIndex ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Details), new { id });
    }
    private FinanceActor Actor() => new(Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty, User.Identity?.Name ?? "Kullanıcı");
}
