using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace VoxCrm.Infrastructure.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(
            VoxCrmDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<Guid>> roleManager,
            bool seedDemoData)
        {
            foreach (var roleName in new[] { "SystemAdmin", "Dealer", "Clinic" })
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                    await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            }

            if (!seedDemoData)
                return;

            var dealer = await context.Dealers.FirstOrDefaultAsync(d => d.CompanyName == "VoxCrm Ana Bayi");
            if (dealer == null)
            {
                dealer = new Dealer
                {
                    ID = Guid.NewGuid(),
                    CompanyName = "VoxCrm Ana Bayi",
                    ContactPhone = "05555555555",
                    ContactEmail = "admin@voxcrm.com",
                    IsActive = true
                };
                context.Dealers.Add(dealer);
                await context.SaveChangesAsync();
            }

            var adminUser = await userManager.FindByEmailAsync("admin@voxcrm.com");
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = "admin@voxcrm.com",
                    Email = "admin@voxcrm.com",
                    FirstName = "Sistem",
                    LastName = "Yöneticisi",
                    DealerId = dealer.ID,
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(adminUser, "Admin123!");
                if (result.Succeeded) await userManager.AddToRoleAsync(adminUser, "Dealer");
            }

            // KLINIK 1 - MUTLU PATILER
            var clinic1 = await context.Clinics.FirstOrDefaultAsync(c => c.Email == "iletisim@mutlupatiler.com");
            if (clinic1 == null)
            {
                clinic1 = new Clinic
                {
                    ID = Guid.NewGuid(),
                    Name = "Mutlu Patiler Veteriner Kliniği",
                    Slug = "mutlu-patiler",
                    Email = "iletisim@mutlupatiler.com",
                    phone = "0212 111 22 33",
                    Address = "Kadıköy, İstanbul",
                    DealerId = dealer.ID,
                    IsActive = true
                };
                context.Clinics.Add(clinic1);
                await context.SaveChangesAsync();
            }

            var clinic1User = await userManager.FindByEmailAsync("iletisim@mutlupatiler.com");
            if (clinic1User == null)
            {
                clinic1User = new ApplicationUser
                {
                    UserName = "iletisim@mutlupatiler.com",
                    Email = "iletisim@mutlupatiler.com",
                    FirstName = "Mutlu",
                    LastName = "Patiler",
                    ClinicId = clinic1.ID,
                    EmailConfirmed = true
                };
                var c1Result = await userManager.CreateAsync(clinic1User, "Klinik123!");
                if (c1Result.Succeeded) await userManager.AddToRoleAsync(clinic1User, "Clinic");
            }

            // Global filter engeli: IgnoreQueryFilters kullanıyoruz
            if (!await context.PetOwners.IgnoreQueryFilters().AnyAsync(o => o.Email == "ahmet@mail.com"))
            {
                var owner1 = new PetOwner { ID = Guid.NewGuid(), ClinicID = clinic1.ID, FirstName = "Ahmet", LastName = "Yılmaz", Phone = "05321112233", NormalizedPhone = "05321112233", Email = "ahmet@mail.com" };
                var owner1_2 = new PetOwner { ID = Guid.NewGuid(), ClinicID = clinic1.ID, FirstName = "Mehmet", LastName = "Demir", Phone = "05441234567", NormalizedPhone = "05441234567", Email = "mehmet.demir@mail.com" };
                var owner1_3 = new PetOwner { ID = Guid.NewGuid(), ClinicID = clinic1.ID, FirstName = "Zeynep", LastName = "Çelik", Phone = "05559876543", NormalizedPhone = "05559876543", Email = "zeynep.c@mail.com" };
                context.PetOwners.AddRange(owner1, owner1_2, owner1_3);

                var patient1 = new Patient { ID = Guid.NewGuid(), ClinicID = clinic1.ID, Name = "Karabaş", Species = "Köpek", Breed = "Kangal", cinsiyet = 'E' };
                var patient1_2 = new Patient { ID = Guid.NewGuid(), ClinicID = clinic1.ID, Name = "Mia", Species = "Kedi", Breed = "Tekir", cinsiyet = 'D' };
                var patient1_3 = new Patient { ID = Guid.NewGuid(), ClinicID = clinic1.ID, Name = "Paşa", Species = "Kuş", Breed = "Muhabbet", cinsiyet = 'E' };
                var patient1_4 = new Patient { ID = Guid.NewGuid(), ClinicID = clinic1.ID, Name = "Boncuk", Species = "Kedi", Breed = "Scottish Fold", cinsiyet = 'D' };
                context.Patients.AddRange(patient1, patient1_2, patient1_3, patient1_4);

                context.PatientOwners.AddRange(
                    new PatientOwner { ID = Guid.NewGuid(), ClinicID = clinic1.ID, PatientId = patient1.ID, PetOwnerId = owner1.ID, IsPrimaryOwner = true },
                    new PatientOwner { ID = Guid.NewGuid(), ClinicID = clinic1.ID, PatientId = patient1_2.ID, PetOwnerId = owner1_2.ID, IsPrimaryOwner = true },
                    new PatientOwner { ID = Guid.NewGuid(), ClinicID = clinic1.ID, PatientId = patient1_3.ID, PetOwnerId = owner1_3.ID, IsPrimaryOwner = true },
                    new PatientOwner { ID = Guid.NewGuid(), ClinicID = clinic1.ID, PatientId = patient1_4.ID, PetOwnerId = owner1_2.ID, IsPrimaryOwner = true }
                );

                context.Appointments.AddRange(
                    new Appointment { ID = Guid.NewGuid(), ClinicID = clinic1.ID, PatientId = patient1.ID, ScheduledAt = DateTime.UtcNow.AddDays(1), AppointmentType = "Muayene", Status = "Planlandı", Reason = "Genel kontrol", DurationMinutes = 30 },
                    new Appointment { ID = Guid.NewGuid(), ClinicID = clinic1.ID, PatientId = patient1_2.ID, ScheduledAt = DateTime.UtcNow.AddDays(2), AppointmentType = "Aşı", Status = "Planlandı", Reason = "Karma Aşı", DurationMinutes = 15 },
                    new Appointment { ID = Guid.NewGuid(), ClinicID = clinic1.ID, PatientId = patient1_3.ID, ScheduledAt = DateTime.UtcNow.AddDays(-1), AppointmentType = "Kontrol", Status = "Tamamlandı", Reason = "Gaga kesimi", DurationMinutes = 15 },
                    new Appointment { ID = Guid.NewGuid(), ClinicID = clinic1.ID, PatientId = patient1_4.ID, ScheduledAt = DateTime.UtcNow.AddDays(5), AppointmentType = "Muayene", Status = "Planlandı", Reason = "Göz akıntısı", DurationMinutes = 20 }
                );
                
                context.Borçlar.AddRange(
                    new Debt { ID = Guid.NewGuid(), ClinicID = clinic1.ID, PetOwnerId = owner1.ID, Amount = 1500, DueDate = DateTime.UtcNow.AddDays(7), IsCollected = false, Description = "Aşı Ücreti" },
                    new Debt { ID = Guid.NewGuid(), ClinicID = clinic1.ID, PetOwnerId = owner1_2.ID, Amount = 400, DueDate = DateTime.UtcNow.AddDays(-2), IsCollected = true, CollectedAt = DateTime.UtcNow.AddDays(-1), PaymentMethod = "Nakit", Description = "Muayene" },
                    new Debt { ID = Guid.NewGuid(), ClinicID = clinic1.ID, PetOwnerId = owner1_3.ID, Amount = 200, DueDate = DateTime.UtcNow.AddDays(5), IsCollected = false, Description = "Tırnak Kesimi" }
                );

                await context.SaveChangesAsync();
            }

            // KLINIK 2 - SIFA VET
            var clinic2 = await context.Clinics.FirstOrDefaultAsync(c => c.Email == "bilgi@sifavet.com");
            if (clinic2 == null)
            {
                clinic2 = new Clinic
                {
                    ID = Guid.NewGuid(),
                    Name = "Şifa Veteriner Kliniği",
                    Slug = "sifa-vet",
                    Email = "bilgi@sifavet.com",
                    phone = "0312 444 55 66",
                    Address = "Çankaya, Ankara",
                    DealerId = dealer.ID,
                    IsActive = true
                };
                context.Clinics.Add(clinic2);
                await context.SaveChangesAsync();
            }

            var clinic2User = await userManager.FindByEmailAsync("bilgi@sifavet.com");
            if (clinic2User == null)
            {
                clinic2User = new ApplicationUser
                {
                    UserName = "bilgi@sifavet.com",
                    Email = "bilgi@sifavet.com",
                    FirstName = "Şifa",
                    LastName = "Vet",
                    ClinicId = clinic2.ID,
                    EmailConfirmed = true
                };
                var c2Result = await userManager.CreateAsync(clinic2User, "Klinik123!");
                if (c2Result.Succeeded) await userManager.AddToRoleAsync(clinic2User, "Clinic");
            }

            // Global filter engeli: IgnoreQueryFilters kullanıyoruz
            if (!await context.PetOwners.IgnoreQueryFilters().AnyAsync(o => o.Email == "ayse@mail.com"))
            {
                var owner2 = new PetOwner { ID = Guid.NewGuid(), ClinicID = clinic2.ID, FirstName = "Ayşe", LastName = "Kaya", Phone = "05332223344", NormalizedPhone = "05332223344", Email = "ayse@mail.com" };
                var owner2_2 = new PetOwner { ID = Guid.NewGuid(), ClinicID = clinic2.ID, FirstName = "Ali", LastName = "Vefa", Phone = "05051239988", NormalizedPhone = "05051239988", Email = "ali.vefa@mail.com" };
                var owner2_3 = new PetOwner { ID = Guid.NewGuid(), ClinicID = clinic2.ID, FirstName = "Fatma", LastName = "Yılmaz", Phone = "05329998877", NormalizedPhone = "05329998877", Email = "fatma.y@mail.com" };
                context.PetOwners.AddRange(owner2, owner2_2, owner2_3);

                var patient2 = new Patient { ID = Guid.NewGuid(), ClinicID = clinic2.ID, Name = "Pamuk", Species = "Kedi", Breed = "Van", cinsiyet = 'D' };
                var patient2_2 = new Patient { ID = Guid.NewGuid(), ClinicID = clinic2.ID, Name = "Duman", Species = "Köpek", Breed = "Golden", cinsiyet = 'E' };
                var patient2_3 = new Patient { ID = Guid.NewGuid(), ClinicID = clinic2.ID, Name = "Tarçın", Species = "Kedi", Breed = "Siyam", cinsiyet = 'D' };
                var patient2_4 = new Patient { ID = Guid.NewGuid(), ClinicID = clinic2.ID, Name = "Zeytin", Species = "Köpek", Breed = "Terrier", cinsiyet = 'E' };
                context.Patients.AddRange(patient2, patient2_2, patient2_3, patient2_4);

                context.PatientOwners.AddRange(
                    new PatientOwner { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PatientId = patient2.ID, PetOwnerId = owner2.ID, IsPrimaryOwner = true },
                    new PatientOwner { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PatientId = patient2_2.ID, PetOwnerId = owner2_2.ID, IsPrimaryOwner = true },
                    new PatientOwner { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PatientId = patient2_3.ID, PetOwnerId = owner2_3.ID, IsPrimaryOwner = true },
                    new PatientOwner { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PatientId = patient2_4.ID, PetOwnerId = owner2_3.ID, IsPrimaryOwner = true }
                );

                context.Appointments.AddRange(
                    new Appointment { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PatientId = patient2.ID, ScheduledAt = DateTime.UtcNow.AddHours(2), AppointmentType = "Aşı", Status = "Planlandı", Reason = "Kuduz Aşısı", DurationMinutes = 15 },
                    new Appointment { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PatientId = patient2.ID, ScheduledAt = DateTime.UtcNow.AddDays(-1), AppointmentType = "Ameliyat", Status = "Tamamlandı", Reason = "Kısırlaştırma", DurationMinutes = 120 },
                    new Appointment { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PatientId = patient2_2.ID, ScheduledAt = DateTime.UtcNow.AddDays(3), AppointmentType = "Muayene", Status = "Planlandı", Reason = "Kulak kaşıntısı", DurationMinutes = 20 },
                    new Appointment { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PatientId = patient2_3.ID, ScheduledAt = DateTime.UtcNow.AddDays(4), AppointmentType = "Aşı", Status = "Planlandı", Reason = "Lösemi Aşısı", DurationMinutes = 10 },
                    new Appointment { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PatientId = patient2_4.ID, ScheduledAt = DateTime.UtcNow.AddDays(-5), AppointmentType = "Muayene", Status = "Tamamlandı", Reason = "Yıllık checkup", DurationMinutes = 45 }
                );
                
                context.Borçlar.AddRange(
                    new Debt { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PetOwnerId = owner2.ID, Amount = 4500, DueDate = DateTime.UtcNow.AddDays(-2), IsCollected = false, Description = "Kısırlaştırma Ameliyatı" },
                    new Debt { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PetOwnerId = owner2.ID, Amount = 500, DueDate = DateTime.UtcNow.AddDays(-10), IsCollected = true, CollectedAt = DateTime.UtcNow.AddDays(-5), PaymentMethod = "Kredi Karti", Description = "İç Dış Parazit" },
                    new Debt { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PetOwnerId = owner2_2.ID, Amount = 800, DueDate = DateTime.UtcNow.AddDays(10), IsCollected = false, Description = "Kan Tahlili" },
                    new Debt { ID = Guid.NewGuid(), ClinicID = clinic2.ID, PetOwnerId = owner2_3.ID, Amount = 1200, DueDate = DateTime.UtcNow.AddDays(-1), IsCollected = true, CollectedAt = DateTime.UtcNow.AddDays(-1), PaymentMethod = "Havale/EFT", Description = "Röntgen ve Muayene" }
                );

                await context.SaveChangesAsync();
            }
        }

        public static async Task BootstrapProductionDealerAsync(
            VoxCrmDbContext context,
            UserManager<ApplicationUser> userManager,
            ProductionDealerBootstrapOptions options)
        {
            if (!options.Enabled || await context.Dealers.AnyAsync())
                return;

            ValidateBootstrapOptions(options);

            await using var transaction = await context.Database.BeginTransactionAsync();
            var dealer = new Dealer
            {
                ID = Guid.NewGuid(),
                CompanyName = options.CompanyName.Trim(),
                ContactEmail = options.Email.Trim(),
                ContactPhone = options.Phone.Trim(),
                IsActive = true
            };
            context.Dealers.Add(dealer);
            await context.SaveChangesAsync();

            var user = new ApplicationUser
            {
                UserName = options.Email.Trim(),
                Email = options.Email.Trim(),
                FirstName = options.FirstName.Trim(),
                LastName = options.LastName.Trim(),
                DealerId = dealer.ID,
                EmailConfirmed = true,
                MustChangePassword = true
            };
            var createResult = await userManager.CreateAsync(user, options.Password);
            if (!createResult.Succeeded)
            {
                var errors = string.Join("; ", createResult.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Production dealer bootstrap failed: {errors}");
            }

            var roleResult = await userManager.AddToRoleAsync(user, "Dealer");
            if (!roleResult.Succeeded)
            {
                var errors = string.Join("; ", roleResult.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Production dealer role assignment failed: {errors}");
            }

            await transaction.CommitAsync();
        }

        public static async Task BootstrapSystemAdminAsync(
            UserManager<ApplicationUser> userManager,
            SystemAdminBootstrapOptions options)
        {
            if (!options.Enabled || (await userManager.GetUsersInRoleAsync("SystemAdmin")).Count > 0)
                return;

            if (string.IsNullOrWhiteSpace(options.Email)
                || string.IsNullOrWhiteSpace(options.FirstName)
                || string.IsNullOrWhiteSpace(options.LastName)
                || string.IsNullOrWhiteSpace(options.Password)
                || options.Password.Length < 12
                || options.Password is "Admin123!" or "Klinik123!" or "change-me")
            {
                throw new InvalidOperationException(
                    "System admin bootstrap requires identity fields and a non-default password of at least 12 characters.");
            }

            if (await userManager.FindByEmailAsync(options.Email.Trim()) != null)
                throw new InvalidOperationException("System admin bootstrap email is already assigned to another account.");

            var user = new ApplicationUser
            {
                UserName = options.Email.Trim(),
                Email = options.Email.Trim(),
                FirstName = options.FirstName.Trim(),
                LastName = options.LastName.Trim(),
                EmailConfirmed = true,
                ClinicId = null,
                DealerId = null,
                MustChangePassword = true,
            };
            var createResult = await userManager.CreateAsync(user, options.Password);
            if (!createResult.Succeeded)
                throw new InvalidOperationException($"System admin bootstrap failed: {string.Join("; ", createResult.Errors.Select(error => error.Description))}");

            var roleResult = await userManager.AddToRoleAsync(user, "SystemAdmin");
            if (!roleResult.Succeeded)
                throw new InvalidOperationException($"System admin role assignment failed: {string.Join("; ", roleResult.Errors.Select(error => error.Description))}");
        }

        private static void ValidateBootstrapOptions(ProductionDealerBootstrapOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.CompanyName)
                || string.IsNullOrWhiteSpace(options.Email)
                || string.IsNullOrWhiteSpace(options.FirstName)
                || string.IsNullOrWhiteSpace(options.LastName)
                || string.IsNullOrWhiteSpace(options.Password))
            {
                throw new InvalidOperationException(
                    "Production dealer bootstrap requires company, email, first name, last name and password.");
            }

            if (options.Password is "Admin123!" or "Klinik123!" or "change-me"
                || options.Password.Length < 12)
            {
                throw new InvalidOperationException(
                    "Production dealer bootstrap password must be at least 12 characters and cannot be a known default.");
            }
        }
    }
}
