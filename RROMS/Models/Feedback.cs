using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 5 (Feedback, Reports & Dashboard)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    // Linked to the order only; the customer is found through Order.UserId (3NF).
    public class Feedback
    {
        public int Id { get; set; }

        public int OrderId { get; set; }
        public Order Order { get; set; } = null!;

        [Range(1, 5)]
        public int Rating { get; set; }

        [StringLength(500)]
        public string? Comment { get; set; }

        [StringLength(500)]
        public string? AdminReply { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
