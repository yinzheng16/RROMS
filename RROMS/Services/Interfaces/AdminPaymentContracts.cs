using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // Owner: Member 4 (US40 - Administrative payment transaction monitoring)
    // Read-only results for the Admin → Payments page (/Admin/Payments).

    // What the admin picked on the page. Every value is optional.
    // From / To are inclusive whole days (local server time, team rule),
    // matched on PaidAt, or CreatedAt for attempts that were never paid.
    public sealed record AdminPaymentFilter
    {
        // Payment no. (RC-000012), bill no. (YB-000012), order no.,
        // table number, customer name or gateway / terminal reference.
        public string? Search { get; init; }

        public DateOnly? From { get; init; }
        public DateOnly? To { get; init; }

        // Null = all statuses / all methods.
        public PaymentStatus? Status { get; init; }
        public PaymentMethod? Method { get; init; }

        // 1-based page number of the list.
        public int Page { get; init; } = 1;
    }

    // One payment row as the admin sees it.
    public sealed record AdminPaymentRecord
    {
        public int PaymentId { get; init; }

        // Same number the receipt PDF uses (US35): RC-000012.
        public string PaymentNo => PaymentReceipt.FormatReceiptNo(PaymentId);

        // Combined bill (US32). Null for older order-based payments (US31).
        public int? BillId { get; init; }
        public string? BillNo => BillId is int id ? $"YB-{id:D6}" : null;

        public OrderType OrderType { get; init; }

        // Dine-in: table of the dining session and how many ordering rounds.
        public string? TableNumber { get; init; }
        public int OrderCount { get; init; }

        // Takeaway (and older payments): the order number.
        public string? OrderNo { get; init; }

        // The customer's full name (never the email). Null if unknown.
        public string? CustomerName { get; init; }

        public decimal Amount { get; init; }
        public PaymentMethod Method { get; init; }
        public PaymentStatus Status { get; init; }
        public string? TransactionRef { get; init; }

        // Counter payments: the staff member who recorded it.
        public string? RecordedByName { get; init; }

        public DateTime CreatedAt { get; init; }
        public DateTime? PaidAt { get; init; }

        // The time shown, grouped and filtered on.
        public DateTime ShownAt => PaidAt ?? CreatedAt;

        // ---------- Checks that help the admin spot problems ----------

        // The bill this payment belongs to (null for older order-based payments).
        public BillStatus? BillStatus { get; init; }
        public decimal? BillTotal { get; init; }

        // How many Paid payments the same bill has (older payments: the same order).
        public int PaidOnSameBill { get; init; }

        // Failed attempt: was the bill paid afterwards (e.g. by card at the counter)?
        // null when the payment is not Failed.
        public bool? FailedButPaidLater => Status == PaymentStatus.Failed
            ? BillStatus == Models.BillStatus.Paid || PaidOnSameBill > 0
            : null;

        // Should never happen: the bill was paid more than once.
        public bool IsDuplicatePaid => Status == PaymentStatus.Paid && PaidOnSameBill > 1;

        // Should never happen: a Paid payment that is not the bill total (full payment only).
        public bool IsAmountMismatch => Status == PaymentStatus.Paid && BillTotal is decimal total && Amount != total;

        public bool NeedsCheck => IsDuplicatePaid || IsAmountMismatch;
    }

    // How many payments have one status, and their amount.
    public sealed record AdminPaymentStatusTotal(int Count, decimal Amount);

    // One day in the selected range.
    // PaidAmount: money received that day (Paid payments only).
    public sealed record AdminPaymentDayTotal(
        DateOnly Date,
        int PaymentCount,
        int PaidCount,
        decimal PaidAmount);

    public sealed record AdminPaymentMonitor
    {
        // Rows of the current page, newest first.
        public IReadOnlyList<AdminPaymentRecord> Payments { get; init; }
            = Array.Empty<AdminPaymentRecord>();

        // Rows that match every filter (all pages).
        public int Matches { get; init; }

        public int Page { get; init; } = 1;
        public int PageCount { get; init; } = 1;
        public int PageSize { get; init; }

        // KPI cards and status chips: search, date and method applied,
        // status NOT applied (so every chip still shows its count).
        public IReadOnlyDictionary<PaymentStatus, AdminPaymentStatusTotal> ByStatus { get; init; }
            = new Dictionary<PaymentStatus, AdminPaymentStatusTotal>();

        // Daily totals for the day headers (same rule as ByStatus),
        // newest day first. Only days that have payments.
        public IReadOnlyList<AdminPaymentDayTotal> Days { get; init; }
            = Array.Empty<AdminPaymentDayTotal>();

        public AdminPaymentStatusTotal Total(PaymentStatus status) =>
            ByStatus.TryGetValue(status, out var t) ? t : new AdminPaymentStatusTotal(0, 0m);

        public int AllCount => ByStatus.Values.Sum(t => t.Count);
    }

    // US40: Admin → Payments. Read-only; only the /Admin folder
    // (AdminOnly policy) calls this.
    public interface IAdminPaymentService
    {
        // Throws ArgumentException if From is after To.
        Task<AdminPaymentMonitor> GetPaymentsAsync(AdminPaymentFilter filter);
    }
}
