using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VoxCrm.Web.Controllers;


[Authorize(Roles = "Dealer")]
public class ClinicSettingsController : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Dealer");

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult IndexPost() => RedirectToAction("Index", "Dealer");
}
