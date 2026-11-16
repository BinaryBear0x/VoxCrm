using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Data
{
    public class VoxCrmDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
    {

        private readonly ITenantService? _tenantService;

        public VoxCrmDbContext(DbContextOptions<VoxCrmDbContext> options, ITenantService? tenantService = null) : base(options)
        {
            _tenantService = tenantService;
        }

        public DbSet<ExcelImportBatch> ExcelImportBatches {get;set;}
        public DbSet<Dealer> Dealers { get; set; }
        public DbSet<Clinic> Clinics { get; set; }
        public DbSet<PetOwner> PetOwners { get; set; }
        public DbSet<Patient> Patients { get; set; }
        public DbSet<PatientOwner> PatientOwners { get; set; }
        public DbSet<VaccineType> VaccineTypes { get; set; }
        public DbSet<VaccinationRecord> VaccinationRecords { get; set; }
        public DbSet<Appointment> Appointments { get; set; }
        public DbSet<Muayene> Muayeneler { get; set; } 
        public DbSet<Debt> Borçlar { get; set; }
        public DbSet<WhatsAppNotification> WhatsAppNotifications { get; set; }
        public DbSet<WhatsAppTemplate> WhatsAppTemplates { get; set; }
        public DbSet<WhatsAppInboundMessage> WhatsAppInboundMessages { get; set; }
        public DbSet<SystemAuditLog> SystemAuditLogs { get; set; }
        public DbSet<ServiceItem> ServiceItems { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<FinanceAccount> FinanceAccounts { get; set; }
        public DbSet<FinanceCharge> FinanceCharges { get; set; }
        public DbSet<FinanceInstallment> FinanceInstallments { get; set; }
        public DbSet<FinanceReceipt> FinanceReceipts { get; set; }
        public DbSet<FinanceChange> FinanceChanges { get; set; }
        public DbSet<ClinicTask> ClinicTasks { get; set; }
        public DbSet<OwnerFollowUpShare> OwnerFollowUpShares { get; set; }
        public DbSet<CareCard> CareCards { get; set; }
        public DbSet<ClosingAcknowledgement> ClosingAcknowledgements { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<ExcelImportBatch>().HasQueryFilter(e=>IsSystemContext||e.ClinicID==TenantClinicId);
            builder.Entity<ExcelImportBatch>().HasIndex(e=>new{e.ClinicID,e.SubmissionId}).IsUnique();
            builder.Entity<Clinic>().Property(x=>x.EnabledModules).HasDefaultValue(7).HasSentinel(-1);
            builder.Entity<OwnerFollowUpShare>().HasQueryFilter(x => IsSystemContext || x.ClinicID == TenantClinicId);
            builder.Entity<OwnerFollowUpShare>().HasIndex(x => new { x.ClinicID, x.PetOwnerId }).IsUnique();
            builder.Entity<OwnerFollowUpShare>().HasIndex(x => x.TokenHash).IsUnique().HasFilter("\"TokenHash\" IS NOT NULL");
            builder.Entity<OwnerFollowUpShare>().Property(x => x.Revision).IsConcurrencyToken();
            builder.Entity<OwnerFollowUpShare>().HasOne<PetOwner>().WithMany().HasForeignKey(x => x.PetOwnerId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ClinicTask>().HasQueryFilter(x => IsSystemContext || x.ClinicID == TenantClinicId);
            builder.Entity<CareCard>().HasQueryFilter(x => IsSystemContext || x.ClinicID == TenantClinicId);
            builder.Entity<ClosingAcknowledgement>().HasQueryFilter(x => IsSystemContext || x.ClinicID == TenantClinicId);
            builder.Entity<ClinicTask>().Property(x => x.Revision).IsConcurrencyToken();
            builder.Entity<CareCard>().Property(x => x.Revision).IsConcurrencyToken();
            builder.Entity<ClinicTask>().HasIndex(x => new { x.ClinicID, x.SubmissionId }).IsUnique();
            builder.Entity<ClinicTask>().HasIndex(x => new { x.CareCardId, x.Kind }).IsUnique().HasFilter("\"CareCardId\" IS NOT NULL");
            builder.Entity<ClinicTask>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ClinicTask>().HasOne<CareCard>().WithMany().HasForeignKey(x => x.CareCardId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<CareCard>().HasOne<Muayene>().WithMany().HasForeignKey(x => x.ExaminationId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<CareCard>().HasOne<PetOwner>().WithMany().HasForeignKey(x => x.PetOwnerId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<CareCard>().HasIndex(x => new { x.ClinicID, x.ExaminationId }).IsUnique();
            builder.Entity<CareCard>().HasIndex(x => x.TokenHash).IsUnique().HasFilter("\"TokenHash\" IS NOT NULL");
            builder.Entity<ClosingAcknowledgement>().HasIndex(x => new { x.ClinicID, x.Date, x.IssueKey }).IsUnique();
            builder.ApplyConfigurationsFromAssembly(typeof(VoxCrmDbContext).Assembly);

            builder.Entity<PetOwner>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<Patient>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<Appointment>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<Muayene>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<Debt>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<PatientOwner>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<VaccineType>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<VaccinationRecord>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<WhatsAppNotification>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<WhatsAppTemplate>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<WhatsAppInboundMessage>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<ServiceItem>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<Payment>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<FinanceAccount>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<FinanceCharge>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<FinanceInstallment>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<FinanceReceipt>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);
            builder.Entity<FinanceChange>().HasQueryFilter(e => IsSystemContext || e.ClinicID == TenantClinicId);

            foreach (var entityType in builder.Model.GetEntityTypes()
                         .Where(entityType => typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType)))
            {
                builder.Entity(entityType.ClrType)
                    .HasOne(typeof(Clinic))
                    .WithMany()
                    .HasForeignKey(nameof(ITenantEntity.ClinicID))
                    .OnDelete(DeleteBehavior.Restrict);

                builder.Entity(entityType.ClrType)
                    .HasIndex(nameof(ITenantEntity.ClinicID));
            }

            foreach (var foreignKey in builder.Model.GetEntityTypes()
                         .Where(entityType => typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                         .SelectMany(entityType => entityType.GetForeignKeys())
                         .Where(foreignKey => typeof(BaseEntity).IsAssignableFrom(foreignKey.PrincipalEntityType.ClrType)))
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }

            builder.Entity<WhatsAppNotification>()
                .HasIndex(n => new { n.ClinicID, n.NotificationType, n.Status, n.NextAttemptAt });

            builder.Entity<WhatsAppNotification>()
                .HasIndex(n => n.GatewayMessageId);

            builder.Entity<WhatsAppNotification>()
                .HasOne(n => n.VaccinationRecord)
                .WithMany(v => v.WhatsAppNotifications)
                .HasForeignKey(n => n.VaccinationRecordId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<WhatsAppNotification>()
                .HasIndex(n => new { n.ClinicID, n.VaccinationRecordId, n.NotificationType })
                .IsUnique()
                .HasFilter("\"VaccinationRecordId\" IS NOT NULL");

            builder.Entity<WhatsAppTemplate>()
                .HasIndex(t => new { t.ClinicID, t.NotificationType, t.IsActive });

            builder.Entity<WhatsAppInboundMessage>()
                .HasIndex(m => new { m.ClinicID, m.ReceivedAt });

            builder.Entity<WhatsAppInboundMessage>()
                .HasIndex(m => new { m.ClinicID, m.ProviderMessageId })
                .IsUnique();

            builder.Entity<WhatsAppInboundMessage>()
                .Property(m => m.ProviderMessageId)
                .HasMaxLength(256);

            builder.Entity<SystemAuditLog>()
                .HasIndex(l => l.CreatedAt);

            builder.Entity<SystemAuditLog>()
                .HasIndex(l => new { l.DealerId, l.CreatedAt });

            builder.Entity<SystemAuditLog>()
                .HasIndex(l => new { l.ClinicId, l.CreatedAt });

            builder.Entity<SystemAuditLog>()
                .HasIndex(l => new { l.Level, l.CreatedAt });

            builder.Entity<SystemAuditLog>()
                .HasIndex(l => new { l.Source, l.Category, l.CreatedAt });

            builder.Entity<PetOwner>().HasIndex(e => new { e.ClinicID, e.SubmissionId }).IsUnique();
            builder.Entity<Patient>().HasIndex(e => new { e.ClinicID, e.SubmissionId }).IsUnique();
            builder.Entity<VaccinationRecord>().HasIndex(e => new { e.ClinicID, e.SubmissionId }).IsUnique();
            builder.Entity<VaccinationRecord>().HasIndex(e => new { e.ClinicID, e.BatchSubmissionId }).HasFilter("\"BatchSubmissionId\" IS NOT NULL");
        }

        public override int SaveChanges()
        {
            ApplyTenantRules();
            return base.SaveChanges();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ApplyTenantRules();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyTenantRules();
            return base.SaveChangesAsync(cancellationToken);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ApplyTenantRules();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ApplyTenantRules()
        {
            NormalizeDateTimes();

            if (_tenantService == null) return;
            var clinicId = _tenantService.GetClinicId();
            if (clinicId == Guid.Empty)
            {
                if (!_tenantService.IsSystemContext && ChangeTracker.Entries<ITenantEntity>()
                        .Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
                {
                    throw new InvalidOperationException("Tenant context is required for tenant data mutations.");
                }

                return;
            }

            foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.ClinicID = clinicId;
                    continue;
                }

                if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    var originalClinicId = entry.OriginalValues.GetValue<Guid>(nameof(ITenantEntity.ClinicID));
                    if (entry.Entity.ClinicID != clinicId || originalClinicId != clinicId)
                        throw new InvalidOperationException("Tenant boundary violation.");
                }
            }
        }

        private Guid TenantClinicId => _tenantService?.GetClinicId() ?? Guid.Empty;
        private bool IsSystemContext => _tenantService == null || _tenantService.IsSystemContext;

        private void NormalizeDateTimes()
        {
            foreach (var entry in ChangeTracker.Entries()
                         .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
            {
                foreach (var property in entry.Properties)
                {
                    // A status-only update must not rewrite timestamps read with legacy/local semantics.
                    if (entry.State == EntityState.Modified && !property.IsModified) continue;
                    if (property.Metadata.ClrType == typeof(DateTime) && property.CurrentValue is DateTime value)
                    {
                        property.CurrentValue = ToUtc(value);
                    }
                    else if (property.Metadata.ClrType == typeof(DateTime?) && property.CurrentValue is DateTime nullableValue)
                    {
                        property.CurrentValue = ToUtc(nullableValue);
                    }
                }
            }
        }

        private static DateTime ToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }
    }
}
