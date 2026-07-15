using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Finance;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.IntegrationTests.Infrastructure;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Finance;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class FinanceIntegrationTests
{
    private readonly PostgresDatabaseFixture _database;

    public FinanceIntegrationTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Concurrent_payments_lock_the_debt_and_cannot_overcollect()
    {
        var seed = await CreateDebtAsync(100m);
        await using var firstContext = CreateTenantContext(seed.ClinicId);
        await using var secondContext = CreateTenantContext(seed.ClinicId);
        var firstService = new FinanceService(firstContext, new FixedTenantService(seed.ClinicId));
        var secondService = new FinanceService(secondContext, new FixedTenantService(seed.ClinicId));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = new[]
        {
            AddPaymentAfterGateAsync(firstService, seed.DebtId, 80m, gate.Task),
            AddPaymentAfterGateAsync(secondService, seed.DebtId, 80m, gate.Task),
        };
        gate.SetResult();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, result => result.Succeeded);
        var rejected = Assert.Single(results, result => !result.Succeeded);
        Assert.Equal(FinanceError.Validation, rejected.Error);

        await using var verification = _database.CreateDbContext();
        var payments = await verification.Payments
            .Where(payment => payment.DebtId == seed.DebtId)
            .AsNoTracking()
            .ToListAsync();
        var debt = await verification.Borçlar.AsNoTracking().SingleAsync(item => item.ID == seed.DebtId);
        Assert.Single(payments);
        Assert.Equal(80m, payments.Single().Amount);
        Assert.False(debt.IsCollected);
    }

    [Fact]
    public async Task Concurrent_reversals_append_exactly_one_reversal()
    {
        var seed = await CreateDebtAsync(100m);
        Guid paymentId;
        await using (var paymentContext = CreateTenantContext(seed.ClinicId))
        {
            var paymentService = new FinanceService(paymentContext, new FixedTenantService(seed.ClinicId));
            paymentId = await paymentService.AddPaymentAsync(
                new AddPaymentRequest(seed.DebtId, 100m, FinancePaymentMethods.Cash, Guid.NewGuid()));
        }

        await using var firstContext = CreateTenantContext(seed.ClinicId);
        await using var secondContext = CreateTenantContext(seed.ClinicId);
        var firstService = new FinanceService(firstContext, new FixedTenantService(seed.ClinicId));
        var secondService = new FinanceService(secondContext, new FixedTenantService(seed.ClinicId));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = new[]
        {
            ReverseAfterGateAsync(firstService, paymentId, gate.Task),
            ReverseAfterGateAsync(secondService, paymentId, gate.Task),
        };
        gate.SetResult();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, result => result.Succeeded);
        var rejected = Assert.Single(results, result => !result.Succeeded);
        Assert.Equal(FinanceError.Conflict, rejected.Error);

        await using var verification = _database.CreateDbContext();
        var entries = await verification.Payments
            .Where(payment => payment.DebtId == seed.DebtId)
            .OrderBy(payment => payment.PaymentDate)
            .AsNoTracking()
            .ToListAsync();
        Assert.Equal(2, entries.Count);
        var reversal = Assert.Single(entries, entry => entry.EntryType == PaymentEntryType.Reversal);
        Assert.Equal(paymentId, reversal.ReversesPaymentId);
        Assert.Equal(100m, reversal.Amount);
        Assert.True(await verification.Payments.AnyAsync(entry => entry.ID == paymentId));
        Assert.False((await verification.Borçlar.AsNoTracking().SingleAsync(item => item.ID == seed.DebtId)).IsCollected);
    }

    [Fact]
    public async Task Cancelled_debt_rejects_payments_and_keeps_ledger_append_only()
    {
        var seed = await CreateDebtAsync(75m);
        await using var context = CreateTenantContext(seed.ClinicId);
        var service = new FinanceService(context, new FixedTenantService(seed.ClinicId));

        await service.CancelDebtAsync(new CancelDebtRequest(seed.DebtId, "Mükerrer kayıt", Guid.NewGuid()));
        var exception = await Assert.ThrowsAsync<FinanceException>(() => service.AddPaymentAsync(
            new AddPaymentRequest(seed.DebtId, 10m, FinancePaymentMethods.Cash, Guid.NewGuid())));

        Assert.Equal(FinanceError.Conflict, exception.Error);
        await using var verification = _database.CreateDbContext();
        Assert.Empty(await verification.Payments.Where(payment => payment.DebtId == seed.DebtId).ToListAsync());
        Assert.NotNull((await verification.Borçlar.AsNoTracking().SingleAsync(item => item.ID == seed.DebtId)).CancelledAt);
    }

    private async Task<(Guid ClinicId, Guid DebtId)> CreateDebtAsync(decimal amount)
    {
        await using (var cleanup = _database.CreateDbContext())
            await TestData.ClearWhatsAppDataAsync(cleanup);

        Guid clinicId;
        Guid ownerId;
        await using (var seedContext = _database.CreateDbContext())
        {
            var seed = await TestData.CreateClinicWithOwnerAsync(seedContext, $"Finance {Guid.NewGuid():N}");
            clinicId = seed.Clinic.ID;
            ownerId = seed.Owner.ID;
        }

        await using var tenantContext = CreateTenantContext(clinicId);
        var service = new FinanceService(tenantContext, new FixedTenantService(clinicId));
        var debtId = await service.CreateDebtAsync(
            new CreateDebtRequest(ownerId, "Test borcu", amount, DateTime.UtcNow.AddDays(30)));
        return (clinicId, debtId);
    }

    private static async Task<AttemptResult> AddPaymentAfterGateAsync(
        IFinanceService service,
        Guid debtId,
        decimal amount,
        Task gate)
    {
        await gate;
        try
        {
            await service.AddPaymentAsync(
                new AddPaymentRequest(debtId, amount, FinancePaymentMethods.Cash, Guid.NewGuid()));
            return new AttemptResult(true, null);
        }
        catch (FinanceException exception)
        {
            return new AttemptResult(false, exception.Error);
        }
    }

    private static async Task<AttemptResult> ReverseAfterGateAsync(IFinanceService service, Guid paymentId, Task gate)
    {
        await gate;
        try
        {
            await service.ReversePaymentAsync(new ReversePaymentRequest(paymentId, "Hatalı tahsilat", Guid.NewGuid()));
            return new AttemptResult(true, null);
        }
        catch (FinanceException exception)
        {
            return new AttemptResult(false, exception.Error);
        }
    }

    private VoxCrmDbContext CreateTenantContext(Guid clinicId)
    {
        var options = new DbContextOptionsBuilder<VoxCrmDbContext>()
            .UseNpgsql(_database.ConnectionString, builder => builder.MigrationsAssembly("VoxCrm.Infrastructure"))
            .Options;
        return new VoxCrmDbContext(options, new FixedTenantService(clinicId));
    }

    private sealed record AttemptResult(bool Succeeded, FinanceError? Error);

    private sealed class FixedTenantService : ITenantService
    {
        private readonly Guid _clinicId;

        public FixedTenantService(Guid clinicId) => _clinicId = clinicId;
        public Guid GetClinicId() => _clinicId;
        public bool IsSystemContext => false;
    }
}
