using System;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class ServiceItem : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }
        
        public string Name { get; set; } = string.Empty; // Örn: Kuduz Aşısı, Genel Muayene
        public string? Description { get; set; }
        public decimal Price { get; set; }
    }
}
