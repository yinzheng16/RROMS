using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // US54 (Member 4): Admin refunds a Paid payment in full.
    // Only the Admin → Payments page (AdminOnly) calls this.
    public interface IPaymentRefundService
    {
        // Online (Stripe): the money goes back to the customer's card first,
        // then the payment becomes Refunded.
        // Cash / Card / Bank: staff return the money by hand; moneyReturned
        // must be true, then the refund is recorded.
        // The bill keeps its status (Paid) and the kitchen status never changes.
        // Throws ArgumentException (bad input) or InvalidOperationException
        // (not Paid, already refunded, Stripe refused); nothing is saved then.
        Task<RefundOutcome> RefundAsync(
            int paymentId,
            string adminUserId,
            string reason,
            string? note,
            bool moneyReturned);
    }

    public sealed record RefundOutcome(int PaymentId, decimal Amount, PaymentMethod Method)
    {
        public string PaymentNo => PaymentReceipt.FormatReceiptNo(PaymentId);
    }

    // The reasons the admin picks from. "Other" needs a note.
    public static class RefundReasons
    {
        public const string ChargedTwice = "Charged twice";
        public const string CustomerComplaint = "Customer complaint";
        public const string OrderProblem = "Order problem";
        public const string Other = "Other";

        public const int NoteMaxLength = 300;

        public static readonly IReadOnlyList<string> All =
            new[] { ChargedTwice, CustomerComplaint, OrderProblem, Other };
    }
}
