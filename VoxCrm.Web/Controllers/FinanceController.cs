using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Finance;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public sealed class FinanceController : Controller
{
    private readonly IFinanceService _service;

    public FinanceController(IFinanceService service) => _service = service;

    public async Task<IActionResult> Index(bool? collected, CancellationToken cancellationToken) =>
        View(await _service.GetIndexAsync(collected, cancellationToken));

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        ViewBag.Owners = await _service.GetOwnersAsync(cancellationToken);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid petOwnerId,
        string description,
        decimal amount,
        DateTime dueDate,
        CancellationToken cancellationToken)
    {
        try
        {
            await _service.CreateDebtAsync(
                new CreateDebtRequest(petOwnerId, description, amount, dueDate), cancellationToken);
            TempData["Success"] = "Borç kaydı oluşturuldu.";
            return RedirectToAction(nameof(Index));
        }
        catch (FinanceException exception) when (exception.Error == FinanceError.Validation)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            ViewBag.Owners = await _service.GetOwnersAsync(cancellationToken);
            return View();
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPayment(
        Guid debtId,
        decimal amount,
        string paymentMethod,
        CancellationToken cancellationToken) =>
        await RunCommandAsync(
            () => _service.AddPaymentAsync(
                new AddPaymentRequest(debtId, amount, paymentMethod, ActorUserId()), cancellationToken),
            "Tahsilat eklendi.");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReversePayment(Guid paymentId, string reason, CancellationToken cancellationToken) =>
        await RunCommandAsync(
            () => _service.ReversePaymentAsync(
                new ReversePaymentRequest(paymentId, reason, ActorUserId()), cancellationToken),
            "Tahsilat ters kaydedildi.");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelDebt(Guid debtId, string reason, CancellationToken cancellationToken) =>
        await RunCommandAsync(
            async () =>
            {
                await _service.CancelDebtAsync(
                    new CancelDebtRequest(debtId, reason, ActorUserId()), cancellationToken);
                return Guid.Empty;
            },
            "Borç iptal edildi.");

    private async Task<IActionResult> RunCommandAsync(Func<Task<Guid>> command, string successMessage)
    {
        try
        {
            await command();
            TempData["Success"] = successMessage;
        }
        catch (FinanceException exception)
        {
            if (exception.Error == FinanceError.NotFound)
                return NotFound();
            TempData["Error"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private Guid ActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
