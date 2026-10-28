using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 2 (Tables & Reservations)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class Reservation
    {
        public int Id { get; set; }

        [Required, StringLength(20)]
        public string ReferenceNo { get; set; } = string.Empty;

        [Required]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        // Nullable: the system or staff may assign the table after the booking is made.
        public int? TableId { get; set; }
        public RestaurantTable? Table { get; set; }

        public DateTime ReservedAt { get; set; }

        // Used by the availability check to detect overlapping bookings.
        public int DurationMinutes { get; set; } = 90;

        [Range(1, 20)]
        public int Guests { get; set; }

        [StringLength(500)]
        public string? SpecialRequest { get; set; }

        public ReservationStatus Status { get; set; } = ReservationStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ICollection<Order> Orders { get; set; } = new List<Order>();

        public decimal? MinimumSpendAccepted { get; set; }

        public DateTime? MinimumSpendAcceptedAt { get; set; }

        [StringLength(40)] public string? SubmissionToken { get; set; }
    }
}
