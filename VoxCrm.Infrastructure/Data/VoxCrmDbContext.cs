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
        public DbSet<ServiceItem> ServiceItems { get; set; }
        public DbSet<Payment> Payments { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder); // Identity tablolarının (Kullanıcı, Rol) düzgün oluşması için şart!

            // Geliştirici veya Migration anında servis yoksa filtreyi atla, aksi halde filtreyi uygula
            if (_tenantService != null)
            {
                
                // Sisteme her veritabanı sorgusu atıldığında, arka planda EF Core otomatik olarak
                // "WHERE ClinicID = Aktif_Kullanıcının_Kliniği" filtresini ekleyecektir.
                
                builder.Entity<PetOwner>().HasQueryFilter(e => e.ClinicID == _tenantService.GetClinicId());
                builder.Entity<Patient>().HasQueryFilter(e => e.ClinicID == _tenantService.GetClinicId());
                builder.Entity<Appointment>().HasQueryFilter(e => e.ClinicID == _tenantService.GetClinicId());
                builder.Entity<Muayene>().HasQueryFilter(e => e.ClinicID == _tenantService.GetClinicId());
                builder.Entity<Debt>().HasQueryFilter(e => e.ClinicID == _tenantService.GetClinicId());
                builder.Entity<PatientOwner>().HasQueryFilter(e => e.ClinicID == _tenantService.GetClinicId());
                
                builder.Entity<WhatsAppNotification>().HasQueryFilter(e => e.ClinicID == _tenantService.GetClinicId());
                builder.Entity<ServiceItem>().HasQueryFilter(e => e.ClinicID == _tenantService.GetClinicId());
                builder.Entity<Payment>().HasQueryFilter(e => e.ClinicID == _tenantService.GetClinicId());
            }
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
