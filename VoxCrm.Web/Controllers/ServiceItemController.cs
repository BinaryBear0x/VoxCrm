using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using System;
using System.Threading.Tasks;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public class ServiceItemController : Controller
{
    private readonly VoxCrmDbContext _context;

    public ServiceItemController(VoxCrmDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var items = await _context.ServiceItems.OrderBy(s => s.Name).ToListAsync();
        return View(items);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Name,Description,Price")] ServiceItem model)
    {
        // Sistem alanları doğrulama dışı bırakılıyor
        ModelState.Remove(nameof(ServiceItem.ClinicID));
        ModelState.Remove(nameof(ServiceItem.CreatedAt));
        ModelState.Remove(nameof(ServiceItem.IsActive));

        if (ModelState.IsValid)
        {
            _context.ServiceItems.Add(model);
            await _context.SaveChangesAsync(); // ApplyTenantRules() ClinicID'yi burada atar
            TempData["Success"] = "Hizmet kalemi eklendi.";
            return RedirectToAction(nameof(Index));
        }
        return View(model);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var item = await _context.ServiceItems.FindAsync(id);
        if (item == null) return NotFound();
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id,
        [Bind("ID,Name,Description,Price,IsActive")] ServiceItem model)
    {
        if (id != model.ID) return NotFound();

        // Sistem alanları doğrulama dışı bırakılıyor (IsActive formdan geliyor, Bind'e dahil)
        ModelState.Remove(nameof(ServiceItem.ClinicID));
        ModelState.Remove(nameof(ServiceItem.CreatedAt));

        if (ModelState.IsValid)
        {
            try
            {
                var existing = await _context.ServiceItems.FindAsync(id); // Global Query Filter: başka klinik = null
                if (existing == null) return NotFound();
                
                existing.Name        = model.Name;
                existing.Description = model.Description;
                existing.Price       = model.Price;
                existing.IsActive    = model.IsActive;
                // ClinicID, CreatedAt → hiç dokunulmaz ✅

                await _context.SaveChangesAsync();
                TempData["Success"] = "Hizmet kalemi güncellendi.";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.ServiceItems.AnyAsync(e => e.ID == id))
                    return NotFound();
                else
                    throw;
            }
            return RedirectToAction(nameof(Index));
        }
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var item = await _context.ServiceItems.FindAsync(id);
        if (item != null)
        {
            _context.ServiceItems.Remove(item);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Hizmet kalemi silindi.";
        }
        return RedirectToAction(nameof(Index));
    }
}
