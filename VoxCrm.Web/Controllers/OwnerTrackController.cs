using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VoxCrm.Infrastructure.FollowUp;
namespace VoxCrm.Web.Controllers;
[AllowAnonymous,Route("OwnerTrack/{token}"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class OwnerTrackController(OwnerFollowUpService service):Controller
{
    private void Headers(){Response.Headers["Referrer-Policy"]="no-referrer";Response.Headers["X-Robots-Tag"]="noindex, nofollow, noarchive";Response.Headers["Cache-Control"]="no-store";}
    [HttpGet]
    public async Task<IActionResult> Index(string token,CancellationToken ct){Headers();var page=await service.ReadAsync(token,ct);if(page==null){Response.StatusCode=404;return View("Unavailable");}ViewBag.Token=token;return View(page);}
    [HttpPost("Request"),ValidateAntiForgeryToken,EnableRateLimiting("authentication")]
    public async Task<IActionResult> RequestFollowUp(string token,Guid patientId,string kind,CancellationToken ct){Headers();if(!await service.RequestAsync(token,patientId,kind,ct)){Response.StatusCode=404;return View("Unavailable");}return RedirectToAction(nameof(Index),new{token});}
}
