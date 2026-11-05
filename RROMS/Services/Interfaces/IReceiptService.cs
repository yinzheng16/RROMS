using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // Owner: Member 4 (US35 - PDF payment receipt)
    // Builds the receipt for one Paid payment and turns it into a PDF.
    // The receipt matches the printed RECEIPT on the staff Bill page (US32).
    public interface IReceiptService
    {
        // The receipt for one of the customer's own Paid payments.
        // customerId comes from the login, never from the URL.
        // Returns null when the payment does not exist, is not Paid,
        // or belongs to someone else (the page shows 404 for all three,
        // so nobody can find out which payment IDs exist).
        Task<PaymentReceipt?> GetCustomerReceiptAsync(string customerId, int paymentId);

        // Staff (Bill page, after payment): the same receipt for any Paid payment.
        // Only the /Staff folder (StaffOrAdmin) and Admin -> Payments (US40) call this.
        Task<PaymentReceipt?> GetReceiptAsync(int paymentId);

        // The receipt as a PDF file (QuestPDF).
        byte[] CreatePdf(PaymentReceipt receipt);
    }

    // One line on the receipt. All ordering rounds are combined: the same
    // dish at the same unit price is one line (same rule as the staff bill).
    public sealed record ReceiptItem(
        string Name,
        int Quantity,
        decimal UnitPrice,
        decimal LineTotal);

    // Everything printed on the receipt. Amounts are the saved bill /
    // order amounts; nothing is recalculated.
    public sealed record PaymentReceipt
    {
        public int PaymentId { get; init; }

        // RC-000012 (payment ID), same style as the bill number YB-000012.
        public string ReceiptNo => FormatReceiptNo(PaymentId);

        // Null for older order-based payments (US31) that have no bill.
        public int? BillId { get; init; }
        public string? BillNo => BillId is int id ? $"YB-{id:D6}" : null;

        public OrderType OrderType { get; init; }

        // Dine-in: table of the dining session and how many ordering rounds.
        public string? TableNumber { get; init; }
        public int OrderCount { get; init; }

        // Takeaway (and older payments): the order number.
        public string? OrderNo { get; init; }

        public IReadOnlyList<ReceiptItem> Lines { get; init; } = Array.Empty<ReceiptItem>();

        public decimal Subtotal { get; init; }
        public decimal ServiceCharge { get; init; }
        public decimal TaxAmount { get; init; }
        public decimal TotalAmount { get; init; }

        // Payment details.
        public PaymentMethod Method { get; init; }
        public decimal AmountPaid { get; init; }
        public DateTime PaidAt { get; init; }
        public string? TransactionRef { get; init; }

        // Counter payments only: the staff member's name (never the email).
        public string? CashierName { get; init; }

        // US54: set when the payment was refunded (printed on the receipt).
        public DateTime? RefundedAt { get; init; }
        public string? RefundReason { get; init; }

        // Restaurant details (US49 Restaurant Settings).
        public string? Address { get; init; }
        public string? Phone { get; init; }
        public string? SstRegistrationNo { get; init; }
        public string? ReceiptFooterNote { get; init; }

        public static string FormatReceiptNo(int paymentId) => $"RC-{paymentId:D6}";
    }
}
