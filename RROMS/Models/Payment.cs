using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 4 (Payments, Billing & Notifications)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class Payment
    {
        public int Id { get; set; }

        public int OrderId { get; set; }
        public Order Order { get; set; } = null!;

        public decimal Amount { get; set; }

        public PaymentMethod Method { get; set; }

        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

        // Reference from the payment gateway (e.g. Stripe), if paid online.
        [StringLength(100)]
        public string? TransactionRef { get; set; }

        public DateTime? PaidAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
