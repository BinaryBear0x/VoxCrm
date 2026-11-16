using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Security;

public sealed class PiiEncryptionInterceptor(IPiiProtector protector) : SaveChangesInterceptor, IMaterializationInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<(PropertyEntry Property, string? Plain)>> pending = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        EncryptChanges(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        EncryptChanges(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) { DecryptTracked(eventData.Context, saved: true); return result; }
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) { DecryptTracked(eventData.Context, saved: true); return ValueTask.FromResult(result); }
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => DecryptTracked(eventData.Context);
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default) { DecryptTracked(eventData.Context); return Task.CompletedTask; }

    public override void SaveChangesCanceled(DbContextEventData eventData) => DecryptTracked(eventData.Context);
    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default) { DecryptTracked(eventData.Context); return Task.CompletedTask; }

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        Transform(entity, protector.Unprotect);
        return entity;
    }

    private void EncryptChanges(DbContext? context)
    {
        if (context == null) return;
        try
        {
        foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Entity is Appointment appointment)
            {
                appointment.GuestPhoneLookupHash = protector.BlindIndex(
                    appointment.ClinicID,
                    NormalizePhone(appointment.GuestPhone));
            }

            if (!protector.Enabled)
                continue;

            if (entry.Entity is PetOwner owner)
            {
                owner.NormalizedPhone = protector.BlindIndex(owner.ClinicID, NormalizePhone(owner.Phone));
                owner.EmailLookupHash = protector.BlindIndex(owner.ClinicID, owner.Email?.Trim().ToLowerInvariant());
                owner.NationalIdentityLookupHash = protector.BlindIndex(owner.ClinicID, owner.NationalIdentityNumber);
            }
            foreach (var name in PropertyNames(entry.Entity))
            {
                var property = entry.Property(name);
                var plain = (string?)property.CurrentValue;
                property.CurrentValue = protector.Protect(plain);
                pending.GetOrCreateValue(context).Add((property, plain));
            }
        }
        }
        catch { DecryptTracked(context); throw; }
    }

    private void DecryptTracked(DbContext? context, bool saved = false)
    {
        if (context == null || !pending.TryGetValue(context, out var values)) return;
        foreach (var (property, plain) in values)
        {
            property.CurrentValue = plain;
            if (saved) property.OriginalValue = plain;
        }
        pending.Remove(context);
    }

    private static void Transform(object entity, Func<string?, string?> transform)
    {
        foreach (var name in PropertyNames(entity))
        {
            var property = entity.GetType().GetProperty(name)!;
            property.SetValue(entity, transform((string?)property.GetValue(entity)));
        }
    }

    private static string[] PropertyNames(object entity) => entity switch
    {
        PetOwner => [nameof(PetOwner.Phone), nameof(PetOwner.Email), nameof(PetOwner.NationalIdentityNumber), nameof(PetOwner.Address), nameof(PetOwner.Notes)],
        Patient => [nameof(Patient.MicrochipNumber), nameof(Patient.pasaportNumarasi), nameof(Patient.Notes)],
        Muayene => [nameof(Muayene.Subjective), nameof(Muayene.Objective), nameof(Muayene.Assessment), nameof(Muayene.Plan), nameof(Muayene.BillingReviewNote)],
        ClinicTask => [nameof(ClinicTask.Title), nameof(ClinicTask.Notes)],
        OwnerFollowUpShare => [nameof(OwnerFollowUpShare.PublicNote)],
        CareCard => [nameof(CareCard.TodayActions), nameof(CareCard.HomeSteps), nameof(CareCard.CallInstructions)],
        ClosingAcknowledgement => [nameof(ClosingAcknowledgement.Note)],
        Appointment => [nameof(Appointment.Reason), nameof(Appointment.GuestName), nameof(Appointment.GuestPhone), nameof(Appointment.GuestNotes)],
        Debt => [nameof(Debt.Description), nameof(Debt.CancellationReason)],
        Payment => [nameof(Payment.Reason), nameof(Payment.Notes)],
        FinanceAccount => [nameof(FinanceAccount.CustomerName), nameof(FinanceAccount.Description)],
        FinanceCharge => [nameof(FinanceCharge.Description)],
        FinanceInstallment => [nameof(FinanceInstallment.Description)],
        FinanceReceipt => [nameof(FinanceReceipt.Notes)],
        FinanceChange => [nameof(FinanceChange.BeforeJson), nameof(FinanceChange.AfterJson)],
        WhatsAppInboundMessage => [nameof(WhatsAppInboundMessage.FromPhone), nameof(WhatsAppInboundMessage.Message)],
        WhatsAppNotification => [nameof(WhatsAppNotification.PhoneNumber), nameof(WhatsAppNotification.MessageContent)],
        Clinic => [nameof(Clinic.phone), nameof(Clinic.Email), nameof(Clinic.Address), nameof(Clinic.DashboardNote)],
        _ => []
    };

    private static string? NormalizePhone(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : new string(value.Where(char.IsDigit).ToArray());
}
