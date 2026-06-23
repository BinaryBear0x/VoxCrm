namespace VoxCrm.Web.Models
{
    public class DashboardViewModel
    {
        public int TotalPetOwners { get; set; }
        public int TotalPatients { get; set; }
        public int TotalAppointments { get; set; }
        public decimal TotalOutstandingDebt { get; set; }
        public int PendingWhatsAppMessages { get; set; }
    }
}
