using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Clinic")]
public class FinanceController : Controller
{
    private readonly VoxCrmDbContext _context;
    public FinanceController(VoxCrmDbContext context) => _context = context;


    private static readonly HashSet<string> AllowedPaymentMethods =
        new() { "Nakit", "Kredi Karti", "Havale/EFT" };

    public async Task<IActionResult> Index(bool? collected)
    {
        var query = _context.Borçlar
            .Include(d => d.PetOwner)
            .Include(d => d.Payments)
            .AsQueryable();

        if (collected.HasValue)
            query = query.Where(d => d.IsCollected == collected.Value);

        var list             = await query.OrderByDescending(d => d.DueDate).ToListAsync();
        var totalOutstanding = await _context.Borçlar.Where(d => !d.IsCollected).SumAsync(d => (decimal?)d.Amount) ?? 0;
        var totalCollected   = await _context.Borçlar.Where(d => d.IsCollected).SumAsync(d => (decimal?)d.Amount) ?? 0;

        ViewBag.TotalOutstanding = totalOutstanding;
        ViewBag.TotalCollected   = totalCollected;
        ViewBag.FilterCollected  = collected;
        return View(list);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Owners = await _context.PetOwners.OrderBy(o => o.FirstName).ToListAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Debt model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Owners = await _context.PetOwners.OrderBy(o => o.FirstName).ToListAsync();
            return View(model);
        }

        var ownerExists = await _context.PetOwners.AnyAsync(o => o.ID == model.PetOwnerId);
        if (!ownerExists)
        {
            ModelState.AddModelError(nameof(model.PetOwnerId), "Geçerli bir müşteri seçin.");
            ViewBag.Owners = await _context.PetOwners.OrderBy(o => o.FirstName).ToListAsync();
            return View(model);
        }

        _context.Borçlar.Add(model);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Borc kaydi olusturuldu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPayment(Guid debtId, decimal amount, string paymentMethod)
    {
        if (!AllowedPaymentMethods.Contains(paymentMethod))
            return BadRequest("Geçersiz ödeme yöntemi.");

        if (amount <= 0)
            return BadRequest("Tahsilat tutarı sıfırdan büyük olmalıdır.");

        var debt = await _context.Borçlar
            .Include(d => d.Payments)
            .FirstOrDefaultAsync(d => d.ID == debtId);
            
        if (debt == null) return NotFound();

        var remainingAmount = debt.Amount - debt.Payments.Sum(p => p.Amount);
        if (amount > remainingAmount)
            return BadRequest("Tahsilat tutarı kalan borçtan büyük olamaz.");

        var payment = new Payment
        {
            DebtId = debtId,
            Amount = amount,
            PaymentMethod = paymentMethod,
            PaymentDate = DateTime.UtcNow
        };

        _context.Payments.Add(payment);
        debt.Payments.Add(payment);

        var totalPaid = debt.Payments.Sum(p => p.Amount);
        if (totalPaid >= debt.Amount)
        {
            debt.IsCollected = true;
            debt.CollectedAt = DateTime.UtcNow;
            debt.PaymentMethod = paymentMethod;
        }

        await _context.SaveChangesAsync();
        TempData["Success"] = "Tahsilat eklendi.";
        return RedirectToAction(nameof(Index));
    }
}
