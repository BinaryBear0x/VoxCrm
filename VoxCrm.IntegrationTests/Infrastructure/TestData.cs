using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.IntegrationTests.Infrastructure;

public static class TestData
{
    public static async Task<(Clinic Clinic, PetOwner Owner)> CreateClinicWithOwnerAsync(
        VoxCrmDbContext db,
        string name,
        bool whatsAppEnabled = true)
    {
        var dealer = new Dealer
        {
            CompanyName = $"{name} Dealer",
            ContactEmail = $"{Slug(name)}-dealer@example.test",
            ContactPhone = "+905550000000",
        };
        var clinic = new Clinic
        {
            Name = name,
            Slug = Slug(name),
            Dealer = dealer,
            DealerId = dealer.ID,
            IsActive = true,
            IsWhatsAppEnabled = whatsAppEnabled,
            AutomaticVaccinationRemindersEnabled = true,
            WhatsAppSendWindowEnabled = false,
        };
        var owner = new PetOwner
        {
            ClinicID = clinic.ID,
            FirstName = "Ayse",
            LastName = "Test",
            Phone = "+905551112233",
            WhatsAppConsent = true,
        };

        db.Dealers.Add(dealer);
        db.Clinics.Add(clinic);
        db.PetOwners.Add(owner);
        await db.SaveChangesAsync();

        return (clinic, owner);
    }

    public static async Task AddNotificationsAsync(
        VoxCrmDbContext db,
        Guid clinicId,
        Guid ownerId,
        int count,
        string status = WhatsAppNotificationStatuses.Pending)
    {
        var owner = await db.PetOwners.SingleAsync(x => x.ID == ownerId);
        for (var i = 0; i < count; i++)
        {
            db.WhatsAppNotifications.Add(new WhatsAppNotification
            {
                ClinicID = clinicId,
                PetOwnerId = ownerId,
                PhoneNumber = owner.Phone!,
                RecipientSnapshot = VoxCrm.Infrastructure.WhatsApp.WhatsAppRecipientGuard.Snapshot(owner, null),
                MessageContent = $"Asi hatirlatma {i}",
                NotificationType = WhatsAppNotificationTypes.VaccinationReminder,
                Status = status,
            });
        }

        await db.SaveChangesAsync();
    }

    public static async Task ClearWhatsAppDataAsync(VoxCrmDbContext db)
    {
        await db.ClinicTasks.ExecuteDeleteAsync();
        await db.ExcelImportBatches.ExecuteDeleteAsync();
        await db.OwnerFollowUpShares.ExecuteDeleteAsync();
        await db.CareCards.ExecuteDeleteAsync();
        await db.ClosingAcknowledgements.ExecuteDeleteAsync();
        await db.UserClaims.ExecuteDeleteAsync();
        await db.UserLogins.ExecuteDeleteAsync();
        await db.UserTokens.ExecuteDeleteAsync();
        await db.UserRoles.ExecuteDeleteAsync();
        await db.Users.ExecuteDeleteAsync();
        await db.WhatsAppInboundMessages.ExecuteDeleteAsync();
        await db.WhatsAppTemplates.ExecuteDeleteAsync();
        await db.WhatsAppNotifications.ExecuteDeleteAsync();
        await db.SystemAuditLogs.ExecuteDeleteAsync();
        await db.FinanceChanges.ExecuteDeleteAsync();
        await db.FinanceReceipts.ExecuteDeleteAsync();
        await db.FinanceInstallments.ExecuteDeleteAsync();
        await db.FinanceCharges.ExecuteDeleteAsync();
        await db.FinanceAccounts.ExecuteDeleteAsync();
        await db.Payments.ExecuteDeleteAsync();
        await db.Borçlar.ExecuteDeleteAsync();
        await db.VaccinationRecords.ExecuteDeleteAsync();
        await db.Muayeneler.ExecuteDeleteAsync();
        await db.Appointments.ExecuteDeleteAsync();
        await db.PatientOwners.ExecuteDeleteAsync();
        await db.Patients.ExecuteDeleteAsync();
        await db.VaccineTypes.ExecuteDeleteAsync();
        await db.ServiceItems.ExecuteDeleteAsync();
        await db.PetOwners.ExecuteDeleteAsync();
        await db.Clinics.ExecuteDeleteAsync();
        await db.Dealers.ExecuteDeleteAsync();
    }

    private static string Slug(string value)
    {
        return value.ToLowerInvariant().Replace(' ', '-').Replace(".", string.Empty);
    }
}
