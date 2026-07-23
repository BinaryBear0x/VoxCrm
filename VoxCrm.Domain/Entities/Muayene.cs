using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;   
namespace VoxCrm.Domain.Entities
{
    public class Muayene : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set;}


        public Guid PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public Guid? AppointmentId { get; set; }
        public Appointment? Appointment { get; set; }
        public string? Subjective { get; set; }
        public string? Objective { get; set; }
        public string? Assessment { get; set; }
        public string? Plan { get; set; }
        public decimal? WeightAtVisit { get; set; }
        public decimal? Temperature { get; set; }
    }
}
