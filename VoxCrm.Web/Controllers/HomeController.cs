using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Application.Dashboard;
using VoxCrm.Web.Models;

namespace VoxCrm.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly IDashboardService _dashboard;

    public HomeController(IDashboardService dashboard) => _dashboard = dashboard;

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (User.IsInRole("Dealer"))
            return RedirectToAction("Index", "Dealer");

        var dashboard = await _dashboard.GetClinicDashboardAsync(cancellationToken);
        var vm = new DashboardViewModel
        {
            TotalPetOwners = dashboard.TotalPetOwners,
            TotalPatients = dashboard.TotalPatients,
            TotalAppointments = dashboard.TotalAppointments,
            TotalOutstandingDebt = dashboard.TotalOutstandingDebt,
            PendingWhatsAppMessages = dashboard.PendingWhatsAppMessages
        };
        return View(vm);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
