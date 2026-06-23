using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class Clinic :BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;

        // Her kliniğin bağlı olduğu bir ana bayi (Dealer) vardır
        public Guid DealerId { get; set; }
        public Dealer Dealer { get; set; } = null!;


        public string? phone { get; set; }
        public string? Email { get; set; }
        public String? Address { get; set; }


        public bool IsWhatsAppEnabled { get; set; } = false;
        public string? WhatsAppPhoneNumberId { get; set; } // Meta API üzerinden alınacak ID :D yersen meta api 

    }
}
