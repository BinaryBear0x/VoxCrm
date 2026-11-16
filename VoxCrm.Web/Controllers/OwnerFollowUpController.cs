using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Workday;
using VoxCrm.Infrastructure.FollowUp;
namespace VoxCrm.Web.Controllers;
[Authorize(Roles="Clinic"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class OwnerFollowUpController(OwnerFollowUpService service):Controller
{
    private Guid Actor=>Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:Guid.Empty;
    public async Task<IActionResult> Index(Guid? ownerId,CancellationToken ct)
    {
        try { var page=await service.ManageAsync(ownerId,ct);var s=page.Share;ViewBag.Input=new FollowUpInput{OwnerId=ownerId??Guid.Empty,Revision=s?.Revision??0,PatientIds=s==null?page.Patients.Select(x=>x.ID).ToList():System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(s.PatientIdsJson)??new(),ShowVaccinations=s?.ShowVaccinations??true,ShowHistory=s?.ShowHistory??true,ShowAppointments=s?.ShowAppointments??true,ShowCare=s?.ShowCare??false,PublicNote=s?.PublicNote};return View(page); }
        catch(WorkdayException e){return e.NotFound?NotFound():BadRequest(e.Message);}
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(FollowUpInput input,CancellationToken ct)
    {
        try
        {
            if(ModelState.IsValid){var token=await service.IssueAsync(input,Actor,ct);Response.Headers["Referrer-Policy"]="no-referrer";return View("Link",Url.Action("Index","OwnerTrack",new{token},Request.Scheme));}
        }
        catch(WorkdayException e){if(e.NotFound)return NotFound();ModelState.AddModelError("",e.Message);}
        try{ViewBag.Input=input;return View("Index",await service.ManageAsync(input.OwnerId,ct));}catch(WorkdayException){return NotFound();}
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id,long revision,CancellationToken ct)
    {
        try{await service.RevokeAsync(id,revision,Actor,ct);TempData["Success"]="Bağlantı kapatıldı. Önceki bağlantı artık açılmaz.";}
        catch(WorkdayException e){if(e.NotFound)return NotFound();TempData["Error"]=e.Message;}return RedirectToAction(nameof(Index));
    }
}
