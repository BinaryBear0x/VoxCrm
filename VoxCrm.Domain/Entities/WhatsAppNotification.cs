using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    //python tarafındaki wp botunun mesaj kuyruğu için kullanılacak entity. Bu entity, botun mesaj göndermesi gereken durumları temsil eder.
    public class WhatsAppNotification : BaseEntity, ITenantEntity       
    {
        public Guid ClinicID { get; set; }

        public Guid PetOwnerId { get; set; } // Mesaj kime gidecek?
        public PetOwner PetOwner { get; set; } = null!;
        public string PhoneNumber { get; set; } = string.Empty; // Gidecek numara
        public string MessageContent { get; set; } = string.Empty; // "Sayın Ahmet Bey, Karabaş'ın kuduz aşısı gelmiştir."

        public string NotificationType { get; set; } = "Aşı"; // Aşı, Borç, Randevu, Doğum Günü

        // Bot bu statüye bakacak: "Pending" (Bekliyor), "Sent" (Gönderildi), "Failed" (Hata)
        public string Status { get; set; } = "Pending";

        public DateTime? SentAt { get; set; } // Bot mesajı başarıyla attığında bu tarihi dolduracak
        public string? ErrorMessage { get; set; } // Eğer numara WhatsApp'ta yoksa bot buraya hata sebebini yazacak
    }
}
