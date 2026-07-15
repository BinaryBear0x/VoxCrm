using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Security;

public sealed class PiiEncryptionInterceptor(IPiiProtector protector) : SaveChangesInterceptor, IMaterializationInterceptor
{
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

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) { DecryptTracked(eventData.Context); return result; }
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) { DecryptTracked(eventData.Context); return ValueTask.FromResult(result); }
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => DecryptTracked(eventData.Context);
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default) { DecryptTracked(eventData.Context); return Task.CompletedTask; }

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        Transform(entity, protector.Unprotect);
        return entity;
    }

    private void EncryptChanges(DbContext? context)
    {
        if (!protector.Enabled || context == null) return;
        foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Entity is PetOwner owner)
            {
                owner.NormalizedPhone = protector.BlindIndex(owner.ClinicID, NormalizePhone(owner.Phone));
                owner.EmailLookupHash = protector.BlindIndex(owner.ClinicID, owner.Email?.Trim().ToLowerInvariant());
            }
            TransformEntry(entry, protector.Protect);
        }
    }

    private void DecryptTracked(DbContext? context)
    {
        if (!protector.Enabled || context == null) return;
        foreach (var entry in context.ChangeTracker.Entries()) TransformEntry(entry, protector.Unprotect);
    }

    private static void TransformEntry(EntityEntry entry, Func<string?, string?> transform)
    {
        foreach (var name in PropertyNames(entry.Entity))
            entry.Property(name).CurrentValue = transform((string?)entry.Property(name).CurrentValue);
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
        PetOwner => [nameof(PetOwner.Phone), nameof(PetOwner.Email), nameof(PetOwner.Address), nameof(PetOwner.Notes)],
        Patient => [nameof(Patient.MicrochipNumber), nameof(Patient.pasaportNumarasi), nameof(Patient.Notes)],
        Muayene => [nameof(Muayene.Subjective), nameof(Muayene.Objective), nameof(Muayene.Assessment), nameof(Muayene.Plan)],
        Appointment => [nameof(Appointment.Reason)],
        Debt => [nameof(Debt.Description), nameof(Debt.CancellationReason)],
        Payment => [nameof(Payment.Reason), nameof(Payment.Notes)],
        WhatsAppInboundMessage => [nameof(WhatsAppInboundMessage.FromPhone), nameof(WhatsAppInboundMessage.Message)],
        WhatsAppNotification => [nameof(WhatsAppNotification.PhoneNumber), nameof(WhatsAppNotification.MessageContent)],
        Clinic => [nameof(Clinic.phone), nameof(Clinic.Email), nameof(Clinic.Address)],
        _ => []
    };

    private static string? NormalizePhone(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : new string(value.Where(char.IsDigit).ToArray());
}
