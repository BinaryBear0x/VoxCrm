using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Finance;
using VoxCrm.Web.Models;
using VoxCrm.Web.Services;
namespace VoxCrm.Web.Controllers;
[Authorize(Roles="Clinic")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class QuickVisitController(QuickVisitService service):Controller
{
    private Guid Actor=>Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet] public async Task<IActionResult> Index(Guid? patientId,CancellationToken ct){var today=await service.TodayAsync(ct);var m=new QuickVisitModel{PatientId=patientId??Guid.Empty,VisitDate=today.ToDateTime(TimeOnly.MinValue),DueDate=today};await service.ChoicesAsync(m,ct);return View(m);}
    [HttpPost,ValidateAntiForgeryToken] public IActionResult Parse(string? text){if(text==null||text.Length>4000)return BadRequest();var p=QuickVisitService.Parse(text);return Json(new{weight=p.Weight,amount=p.Amount,paid=p.Paid});}
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Preview(QuickVisitModel model,CancellationToken ct)
    {
        if(ModelState.IsValid)try{model.SubmissionId=Guid.NewGuid();return View(await service.PreviewAsync(model,Actor,ct));}catch(InvalidOperationException e){ModelState.AddModelError("",e.Message);}
        await service.ChoicesAsync(model,ct);return View("Index",model);
    }
    [HttpGet] public async Task<IActionResult> Edit(Guid token,CancellationToken ct){try{var m=service.Get(Actor,token).Model;await service.ChoicesAsync(m,ct);return View("Index",m);}catch(InvalidOperationException e){TempData["Error"]=e.Message;return RedirectToAction(nameof(Index));}}
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Commit(Guid token,bool confirmed,CancellationToken ct)
    {
        if(!confirmed){TempData["Error"]="Son kontrol onayını işaretleyin.";return RedirectToAction(nameof(Edit),new{token});}
        try{var exam=await service.CommitAsync(Actor,User.Identity?.Name??"Kullanıcı",token,ct);TempData["Success"]="Ziyaret kaydedildi. Varsa bakım kartı taslak hazır. Bu ekran doğrudan mesaj göndermez; otomatik aşı hatırlatmaları klinik ayarlarına bağlıdır.";return RedirectToAction("Details","Muayene",new{id=exam});}
        catch(Exception e) when(e is InvalidOperationException or FinanceException){TempData["Error"]=e.Message;return RedirectToAction(nameof(Edit),new{token});}
    }
}
