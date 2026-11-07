using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 4 (Payments, Billing & Notifications)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    //
    // US32 / US33 (Dine-in Workflow Decision, 27 Oct):
    // - New payments are recorded against a Bill (BillId).
    //   A combined bill covers several orders, so it has no single OrderId.
    // - OrderId is kept (nullable) for payments recorded before combined billing.
    public class Payment
    {
        public int Id { get; set; }

        // Older (US31) payments: the order that was paid. Null for bill payments.
        public int? OrderId { get; set; }
        public Order? Order { get; set; }

        // US32: Payments can be linked to a combined bill.
        public int? BillId { get; set; }
        public Bill? Bill { get; set; }

        public decimal Amount { get; set; }

        public PaymentMethod Method { get; set; }

        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

        // Reference from the payment gateway (e.g. Stripe), if paid online.
        [StringLength(100)]
        public string? TransactionRef { get; set; }

        // When the payment was received (counter: same as CreatedAt;
        // online: when the gateway confirmed it).
        public DateTime? PaidAt { get; set; }

        // When the payment record was created.
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // US32: Staff member who recorded a counter payment
        // (AspNetUsers.Id). Null for online payments and older records.
        [StringLength(450)]
        public string? RecordedByUserId { get; set; }

        // US54 (Member 4): Full refund by an Admin. All null until refunded;
        // Status then becomes Refunded. The bill keeps its own status.
        public DateTime? RefundedAt { get; set; }

        // Admin who refunded it (AspNetUsers.Id).
        [StringLength(450)]
        public string? RefundedByUserId { get; set; }

        // Reason picked by the admin, plus the optional note ("Other · ...").
        [StringLength(400)]
        public string? RefundReason { get; set; }
    }
}
