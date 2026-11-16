using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Audit;
using VoxCrm.Application.PetOwners;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Audit;
using VoxCrm.Infrastructure.PetOwners;
using VoxCrm.Infrastructure.WhatsApp;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ConsentWithdrawalTests(PostgresDatabaseFixture database)
{
    private sealed class FixedTenant(Guid clinicId) : ITenantService
    {
        public Guid GetClinicId() => clinicId;
        public bool IsSystemContext => false;
    }

    private static WhatsAppNotification Message(Clinic clinic, PetOwner owner, string status) => new()
    {
        ClinicID = clinic.ID, PetOwnerId = owner.ID, PhoneNumber = owner.Phone!,
        RecipientSnapshot = WhatsAppRecipientGuard.Snapshot(owner, null), MessageContent = "Merhaba",
        NotificationType = WhatsAppNotificationTypes.ManualMessage, Status = status,
    };

    private static PetOwner Edited(PetOwner source, bool consent) => new()
    {
        ID = source.ID, ClinicID = source.ClinicID, FirstName = source.FirstName, LastName = source.LastName,
        Phone = source.Phone, WhatsAppConsent = consent,
    };

    [Fact]
    public async Task Withdrawing_consent_cancels_the_customers_queued_messages_immediately()
    {
        Clinic clinic;
        PetOwner owner;
        PetOwner other;
        WhatsAppNotification pending, retry, processing, sent, othersPending;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Consent Clinic");
            other = new PetOwner { ClinicID = clinic.ID, FirstName = "Baska", LastName = "Musteri", Phone = "+905557778899", WhatsAppConsent = true };
            db.Add(other);
            await db.SaveChangesAsync();
            pending = Message(clinic, owner, WhatsAppNotificationStatuses.Pending);
            retry = Message(clinic, owner, WhatsAppNotificationStatuses.RetryScheduled);
            processing = Message(clinic, owner, WhatsAppNotificationStatuses.Processing);
            sent = Message(clinic, owner, WhatsAppNotificationStatuses.Sent);
            othersPending = Message(clinic, other, WhatsAppNotificationStatuses.Pending);
            db.AddRange(pending, retry, processing, sent, othersPending);
            await db.SaveChangesAsync();
        }

        PetOwnerCommandResult result;
        await using (var db = database.CreateDbContext())
            result = await new PetOwnerService(db, new FixedTenant(clinic.ID), new DbAuditLogger(db)).UpdateAsync(Edited(owner, consent: false));

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.CancelledMessages);
        await using var verify = database.CreateDbContext();
        var statuses = await verify.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(n => n.ID, n => n.Status);
        Assert.Equal(WhatsAppNotificationStatuses.Cancelled, statuses[pending.ID]);
        Assert.Equal(WhatsAppNotificationStatuses.Cancelled, statuses[retry.ID]);
        // Already handed to the gateway or already delivered: the gateway's final check / history are untouched.
        Assert.Equal(WhatsAppNotificationStatuses.Processing, statuses[processing.ID]);
        Assert.Equal(WhatsAppNotificationStatuses.Sent, statuses[sent.ID]);
        Assert.Equal(WhatsAppNotificationStatuses.Pending, statuses[othersPending.ID]);
        Assert.False((await verify.PetOwners.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.ID == owner.ID)).WhatsAppConsent);
    }

    [Fact]
    public async Task Saving_a_customer_without_changing_consent_leaves_the_queue_alone()
    {
        Clinic clinic;
        PetOwner owner;
        WhatsAppNotification pending;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, "Consent Unchanged Clinic");
            pending = Message(clinic, owner, WhatsAppNotificationStatuses.Pending);
            db.Add(pending);
            await db.SaveChangesAsync();
        }

        PetOwnerCommandResult result;
        await using (var db = database.CreateDbContext())
            result = await new PetOwnerService(db, new FixedTenant(clinic.ID), new DbAuditLogger(db)).UpdateAsync(Edited(owner, consent: true));

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.CancelledMessages);
        await using var verify = database.CreateDbContext();
        Assert.Equal(WhatsAppNotificationStatuses.Pending, (await verify.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking().SingleAsync()).Status);
    }
}
