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

        public Guid DealerId { get; set; }
        public Dealer Dealer { get; set; } = null!;


        public string? phone { get; set; }
        public string? Email { get; set; }
        public String? Address { get; set; }


        public int EnabledModules { get; set; } = 7; // 1: QuickVisit, 2: OwnerFollowUp + CareCards, 4: Workday tasks + closing assistant
        public long ModulesRevision { get; set; }

        public bool IsWhatsAppEnabled { get; set; } = false;
        public bool AutomaticVaccinationRemindersEnabled { get; set; }
        public bool ManualWhatsAppEnabled { get; set; } = true;
        public string? WhatsAppPhoneNumberId { get; set; }
        public bool WhatsAppSendWindowEnabled { get; set; } = true;
        public TimeOnly WhatsAppSendWindowStart { get; set; } = new(9, 0);
        public TimeOnly WhatsAppSendWindowEnd { get; set; } = new(19, 0);
        public string WhatsAppTimeZoneId { get; set; } = "Europe/Istanbul";
        public string TimeZoneId { get; set; } = "Europe/Istanbul";
        public string? DashboardNote { get; set; }
        public long DashboardNoteRevision { get; set; }

    }
}
