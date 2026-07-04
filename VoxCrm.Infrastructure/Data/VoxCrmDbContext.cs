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
    // Sistemdeki giriş (Login) ve rol tablolarını otomatik oluşturması için DbContext yerine IdentityDbContext kullanıyoruz
    public class VoxCrmDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
    {

        private readonly ITenantService? _tenantService;

        // EF Core araçları (Migration vb) için boş constructor veya ITenantService'i opsiyonel (?) yapıyoruz
        public VoxCrmDbContext(DbContextOptions<VoxCrmDbContext> options, ITenantService? tenantService = null) : base(options)
        {
            _tenantService = tenantService;
        }

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

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder); // Identity tablolarının (Kullanıcı, Rol) düzgün oluşması için şart!

            // EF model cache ilk context'e göre paylaşıldığı için filtreler her zaman modele eklenir.
            // Tenant servisi olmayan API/migration akışları tüm veriyi görür; Web tenant context'i klinik scope uygular.
            builder.Entity<PetOwner>().HasQueryFilter(e => TenantClinicId == Guid.Empty || e.ClinicID == TenantClinicId);
            builder.Entity<Patient>().HasQueryFilter(e => TenantClinicId == Guid.Empty || e.ClinicID == TenantClinicId);
            builder.Entity<Appointment>().HasQueryFilter(e => TenantClinicId == Guid.Empty || e.ClinicID == TenantClinicId);
            builder.Entity<Muayene>().HasQueryFilter(e => TenantClinicId == Guid.Empty || e.ClinicID == TenantClinicId);
            builder.Entity<Debt>().HasQueryFilter(e => TenantClinicId == Guid.Empty || e.ClinicID == TenantClinicId);
            builder.Entity<PatientOwner>().HasQueryFilter(e => TenantClinicId == Guid.Empty || e.ClinicID == TenantClinicId);
            builder.Entity<WhatsAppNotification>().HasQueryFilter(e => TenantClinicId == Guid.Empty || e.ClinicID == TenantClinicId);
            builder.Entity<WhatsAppTemplate>().HasQueryFilter(e => TenantClinicId == Guid.Empty || e.ClinicID == TenantClinicId);
            builder.Entity<WhatsAppInboundMessage>().HasQueryFilter(e => TenantClinicId == Guid.Empty || e.ClinicID == TenantClinicId);
            builder.Entity<ServiceItem>().HasQueryFilter(e => TenantClinicId == Guid.Empty || e.ClinicID == TenantClinicId);
            builder.Entity<Payment>().HasQueryFilter(e => TenantClinicId == Guid.Empty || e.ClinicID == TenantClinicId);

            builder.Entity<WhatsAppNotification>()
                .HasIndex(n => new { n.ClinicID, n.NotificationType, n.Status, n.NextAttemptAt });

            builder.Entity<WhatsAppNotification>()
                .HasIndex(n => n.GatewayMessageId);

            builder.Entity<WhatsAppTemplate>()
                .HasIndex(t => new { t.ClinicID, t.NotificationType, t.IsActive });

            builder.Entity<WhatsAppInboundMessage>()
                .HasIndex(m => new { m.ClinicID, m.ReceivedAt });

            builder.Entity<SystemAuditLog>()
                .HasIndex(l => l.CreatedAt);

            builder.Entity<SystemAuditLog>()
                .HasIndex(l => new { l.DealerId, l.CreatedAt });

            builder.Entity<SystemAuditLog>()
                .HasIndex(l => new { l.ClinicId, l.CreatedAt });

            builder.Entity<SystemAuditLog>()
                .HasIndex(l => new { l.Level, l.CreatedAt });
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
            if (clinicId == Guid.Empty) return;

            foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    // Eğer ClinicID atanmamışsa veya boş Guid ise mevcut kliniği ata
                    if (entry.Entity.ClinicID == Guid.Empty)
                    {
                        entry.Entity.ClinicID = clinicId;
                    }
                }
            }
        }

        private Guid TenantClinicId => _tenantService?.GetClinicId() ?? Guid.Empty;

        private void NormalizeDateTimes()
        {
            foreach (var entry in ChangeTracker.Entries()
                         .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
            {
                foreach (var property in entry.Properties)
                {
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
