using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class Dealer : BaseEntity
    {
        public string CompanyName { get; set; } = string.Empty;
      
        public string ContactEmail { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        
        public ICollection<Clinic> Clinics { get; set; } = new List<Clinic>();
    }
}
