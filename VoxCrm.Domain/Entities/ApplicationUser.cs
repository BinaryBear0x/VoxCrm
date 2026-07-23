using Microsoft.AspNetCore.Identity;
using System;

namespace VoxCrm.Domain.Entities
{
    public class ApplicationUser : IdentityUser<Guid>
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        public Guid? ClinicId { get; set; }
        public Clinic? Clinic { get; set; }

        public Guid? DealerId { get; set; }
        public Dealer? Dealer { get; set; }
        public bool MustChangePassword { get; set; }
    }
}
