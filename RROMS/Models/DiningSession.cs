using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 2 (Tables & Reservations)
    // One visit at one table. Opened when staff mark a reservation Seated,
    // closed when the guests leave. Dine-in orders and the combined bill hang off this row.
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class DiningSession
    {
        public int Id { get; set; }

        public int TableId { get; set; }
        public RestaurantTable Table { get; set; } = null!;

        // Set for every visit opened from a reservation (null only in old data).
        public int? ReservationId { get; set; }
        public Reservation? Reservation { get; set; }

        // Comes from the reservation (null only in old data, until a customer orders).
        public string? CustomerId { get; set; }
        public ApplicationUser? Customer { get; set; }

        public DateTime OpenedAt { get; set; } = DateTime.Now;
        public DateTime? ClosedAt { get; set; }

        public DiningSessionStatus Status { get; set; } = DiningSessionStatus.Open;

        public ICollection<Order> Orders { get; set; } = new List<Order>();
    }

    public enum DiningSessionStatus
    {
        [Display(Name = "Open")] Open,
        [Display(Name = "Closed")] Closed
    }
}