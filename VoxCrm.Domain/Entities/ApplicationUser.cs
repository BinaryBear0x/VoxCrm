using Microsoft.AspNetCore.Identity;
using System;

namespace VoxCrm.Domain.Entities
{
    // Sisteme giriş yapacak olan (Login olacak) kullanıcılarımız
    public class ApplicationUser : IdentityUser<Guid>
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        // Kullanıcı bir Kliniğe ait olabilir
        public Guid? ClinicId { get; set; }
        public Clinic? Clinic { get; set; }

        // Veya kullanıcı bir Bayiye (Dealer) ait olabilir
        public Guid? DealerId { get; set; }
        public Dealer? Dealer { get; set; }
        public bool MustChangePassword { get; set; }
    }
}
