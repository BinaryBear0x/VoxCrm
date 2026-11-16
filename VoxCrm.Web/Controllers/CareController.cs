using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VoxCrm.Application.Workday;

namespace VoxCrm.Web.Controllers;

[AllowAnonymous, Route("Care/{token}"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CareController(IWorkdayService workday) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string token, CancellationToken ct)
    {
        Headers(); var card = await workday.ReadPublicAsync(token, ct);
        if (card == null) { Response.StatusCode = 404; return View("Unavailable"); }
        ViewBag.Token = token; return View(card);
    }
    [HttpPost("Respond"), ValidateAntiForgeryToken, EnableRateLimiting("authentication")]
    public async Task<IActionResult> Respond(string token, string response, CancellationToken ct)
    {
        Headers();
        if (!await workday.RespondAsync(token, response, ct)) { Response.StatusCode = 404; return View("Unavailable"); }
        return RedirectToAction(nameof(Index), new { token });
    }
    private void Headers()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers["Cache-Control"] = "no-store";
    }
}
