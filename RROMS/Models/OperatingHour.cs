namespace RROMS.Models
{
    // Owner: Member 2 (Tables & Reservations)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class OperatingHour
    {
        public int Id { get; set; }

        public DayOfWeek Day { get; set; }

        public TimeOnly OpenTime { get; set; }
        public TimeOnly CloseTime { get; set; }

        public bool IsClosed { get; set; }
    }
}
