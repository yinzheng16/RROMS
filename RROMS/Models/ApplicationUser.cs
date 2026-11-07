using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace RROMS.Models
{
    // Owner: Member 1 (Identity, Access & AI Chatbot)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class ApplicationUser : IdentityUser
    {
        [Required, StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? DiningPreferences { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // US07: set when an Admin removes a Staff / Admin account. The row stays so old bills, payments and
        // logs still show who did what, but the email and phone are wiped and the account is hidden everywhere.
        public DateTime? RemovedAt { get; set; }

        public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
        public ICollection<Order> Orders { get; set; } = new List<Order>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    }
}
