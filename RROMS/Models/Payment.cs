using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 4 (Payments, Billing & Notifications)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    // TODO (US32 / US33):
    // OrderId is required by the legacy payment workflow.
    // New combined-bill payments should reference BillId.
    // Finalize how OrderId is handled for payments covering multiple orders.
    // Preserve existing payment records and database compatibility.
    public class Payment
    {
        public int Id { get; set; }

        public int OrderId { get; set; }
        public Order Order { get; set; } = null!;

        // US32: Payments can be linked to a combined bill.
        public int? BillId { get; set; }
        public Bill? Bill { get; set; }

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
