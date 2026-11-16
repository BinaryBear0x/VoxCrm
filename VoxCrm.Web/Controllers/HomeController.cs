using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Dashboard;
using VoxCrm.Web.Models;
using VoxCrm.Application.Workday;
using System.Security.Claims;

namespace VoxCrm.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly IClinicWorkspaceService _workspace;

    public HomeController(IClinicWorkspaceService workspace) => _workspace = workspace;

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(DateOnly? date, CancellationToken cancellationToken)
    {
        if (User.IsInRole("Dealer"))
            return RedirectToAction("Index", "Dealer");

        if (!User.IsInRole("Clinic")) return Forbid();
        try { return View(await _workspace.GetAsync(date, cancellationToken)); }
        catch (WorkdayException e) { return e.NotFound ? NotFound() : BadRequest(e.Message); }
    }

    [Authorize(Roles = "Clinic"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveClinicNote(string? note, long revision, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Forbid();
        try { await _workspace.SaveNoteAsync(note, revision, actor, ct); TempData["Success"] = "Klinik notu kaydedildi."; }
        catch (WorkdayException e) { if (e.NotFound) return NotFound(); TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Index));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
