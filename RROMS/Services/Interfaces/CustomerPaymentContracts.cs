using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // Owner: Member 4 (US34 - Customer payment records)
    // Read-only results for the customer's "My payments" page.

    // One payment row as the customer sees it.
    public sealed record CustomerPaymentRecord
    {
        public int PaymentId { get; init; }

        // Combined bill (US32). Null for older order-based payments (US31).
        public int? BillId { get; init; }

        // Same format staff see and print: YB-000012.
        public string? BillNo => BillId is int id ? $"YB-{id:D6}" : null;

        public OrderType OrderType { get; init; }

        // Dine-in: the table of the dining session.
        public string? TableNumber { get; init; }

        // Takeaway (and older payments): the order number.
        // Dine-in: the first order of the session, for reference.
        public string? OrderNo { get; init; }

        // How many orders (ordering rounds) the bill covers.
        public int OrderCount { get; init; }

        public decimal Amount { get; init; }
        public PaymentMethod Method { get; init; }
        public PaymentStatus Status { get; init; }
        public string? TransactionRef { get; init; }

        public DateTime CreatedAt { get; init; }
        public DateTime? PaidAt { get; init; }

        // The date shown and filtered on: when it was paid, otherwise
        // when the attempt was made (Pending / Failed).
        public DateTime ShownAt => PaidAt ?? CreatedAt;
    }

    // The list plus a small summary for the selected date range.
    public sealed record CustomerPaymentHistory
    {
        public IReadOnlyList<CustomerPaymentRecord> Payments { get; init; }
            = Array.Empty<CustomerPaymentRecord>();

        // Sum of Paid payments only (Pending, Failed and Refunded are not money received).
        public decimal TotalPaid { get; init; }

        public int PaidCount { get; init; }
    }
}
