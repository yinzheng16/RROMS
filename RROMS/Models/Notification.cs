using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 4 (Payments, Billing & Notifications)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class Notification
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        [Required, StringLength(100)]
        public string Title { get; set; } = string.Empty;

        [Required, StringLength(500)]
        public string Message { get; set; } = string.Empty;

        public NotificationType Type { get; set; }

        public bool IsRead { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
