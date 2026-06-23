using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Web.Models;

namespace VoxCrm.Web.Controllers;

// Dashboard her iki rol tarafından da görülebilir.
// Ama View'da her rol kendi verilerini görür.
[Authorize]
public class HomeController : Controller
{
    private readonly VoxCrmDbContext _context;

    public HomeController(VoxCrmDbContext context) => _context = context;

    public async Task<IActionResult> Index()
    {
        // Dealer kullanıcısı → kendi dashboarduna yönlendir
        if (User.IsInRole("Dealer"))
            return RedirectToAction("Index", "Dealer");

        var vm = new DashboardViewModel
        {
            TotalPetOwners       = await _context.PetOwners.CountAsync(),
            TotalPatients        = await _context.Patients.CountAsync(),
            TotalAppointments    = await _context.Appointments.CountAsync(),
            TotalOutstandingDebt = await _context.Borçlar
                                        .Where(d => !d.IsCollected)
                                        .SumAsync(d => (decimal?)d.Amount) ?? 0,
            PendingWhatsAppMessages = await _context.WhatsAppNotifications
                                        .Where(w => w.Status == "Pending")
                                        .CountAsync()
        };
        return View(vm);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
