using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Web.Services;
namespace VoxCrm.Web.Controllers;
[Authorize(Roles="Clinic")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ExcelTransferController(ExcelTransferService service,ILogger<ExcelTransferController> logger):Controller
{
    private Guid Actor=>Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct)=>View(await service.HistoryAsync(ct));
    [HttpGet] public async Task<IActionResult> Export(bool template,CancellationToken ct)=>File(await service.ExportAsync(template,ct),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",template?"VoxCRM-Hasta-Sablonu.xlsx":"VoxCRM-Hasta-Sahip.xlsx");
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Upload(IFormFile? file,CancellationToken ct)
    {
        if(file==null||!file.FileName.EndsWith(".xlsx",StringComparison.OrdinalIgnoreCase)||file.Length>5_242_880){TempData["Error"]="En fazla 5 MB boyutunda .xlsx dosyası seçin. Eski .xls dosyalarını Excel'de .xlsx olarak kaydedin.";return RedirectToAction(nameof(Index));}
        try{using var stream=file.OpenReadStream();var token=await service.UploadAsync(stream,Actor,ct);return RedirectToAction(nameof(Map),new{token});}
        catch(Exception ex) when(ex is not OperationCanceledException){logger.LogWarning(ex,"Excel upload rejected");TempData["Error"]=ex is InvalidOperationException ? ex.Message:"Excel dosyası okunamadı. Geçerli bir .xlsx dosyası kullanın.";return RedirectToAction(nameof(Index));}
    }
    [HttpGet] public IActionResult Map(Guid token){try{ViewBag.Token=token;return View(service.Get(Actor,token));}catch(InvalidOperationException e){TempData["Error"]=e.Message;return RedirectToAction(nameof(Index));}}
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Preview(Guid token,int[] columns,CancellationToken ct){try{ViewBag.Token=token;return View(await service.MapAsync(Actor,token,columns,ct));}catch(InvalidOperationException e){TempData["Error"]=e.Message;return RedirectToAction(nameof(Map),new{token});}}
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Confirm(Guid token,int[] selected,CancellationToken ct){try{var count=await service.ConfirmAsync(Actor,token,selected,ct);TempData["Success"]=count==0?"Bu aktarım zaten tamamlanmış; tekrar kayıt oluşturulmadı.":$"{count} hasta aktarıldı. WhatsApp izni açılmadı; mesaj gönderilmedi.";}catch(InvalidOperationException e){TempData["Error"]=e.Message;}return RedirectToAction(nameof(Index));}
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Undo(Guid token,CancellationToken ct){try{await service.UndoAsync(Actor,token,ct);TempData["Success"]="Aktarım arşivlenerek geri alındı. Önceden var olan kayıtlar korundu.";}catch(InvalidOperationException e){TempData["Error"]=e.Message;}return RedirectToAction(nameof(Index));}
}
