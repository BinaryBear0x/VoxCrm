using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.ServiceItems;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public sealed class ServiceItemController : Controller
{
    private readonly IServiceItemService _service;

    public ServiceItemController(IServiceItemService service) => _service = service;

    public async Task<IActionResult> Index(bool includeArchived, CancellationToken cancellationToken)
    {
        ViewBag.IncludeArchived = includeArchived;
        return View(await _service.ListAsync(includeArchived, cancellationToken));
    }

    public IActionResult Create() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Name,Description,Price")] ServiceItem model,
        CancellationToken cancellationToken)
    {
        RemoveSystemValidation();
        if (!ModelState.IsValid) return View(model);
        var result = await _service.CreateAsync(model, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Hizmet kaydedilemedi.");
            return View(model);
        }
        TempData["Success"] = "Hizmet kalemi eklendi.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var item = await _service.GetAsync(id, cancellationToken);
        return item == null ? NotFound() : View(item);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        [Bind("ID,Name,Description,Price")] ServiceItem model,
        CancellationToken cancellationToken)
    {
        if (id != model.ID) return BadRequest();
        RemoveSystemValidation();
        if (!ModelState.IsValid) return View(model);
        var result = await _service.UpdateAsync(model, cancellationToken);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Hizmet güncellenemedi.");
            return View(model);
        }
        TempData["Success"] = "Hizmet kalemi güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.ArchiveAsync(id, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        TempData["Success"] = "Hizmet kalemi arşivlendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.RestoreAsync(id, ActorUserId(), cancellationToken);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error ?? "Hizmet geri alınamadı.";
            return RedirectToAction(nameof(Index), new { includeArchived = true });
        }
        TempData["Success"] = "Hizmet kalemi geri alındı.";
        return RedirectToAction(nameof(Index), new { includeArchived = true });
    }

    private void RemoveSystemValidation()
    {
        ModelState.Remove(nameof(ServiceItem.ClinicID));
        ModelState.Remove(nameof(ServiceItem.CreatedAt));
        ModelState.Remove(nameof(ServiceItem.IsActive));
    }

    private Guid ActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
