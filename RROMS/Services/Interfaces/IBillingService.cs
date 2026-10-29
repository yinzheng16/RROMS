using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    public interface IBillingService
    {
        // Read bill details without modifying the database.
        Task<BillSummary> GetBillAsync(int orderId);

        // Legacy single-order bill summary (US31/US32 v1).
        // Does NOT create or save a Bill entity.
        Task<BillSummary> GenerateBillAsync(int orderId);

        // Calculate a bill preview without saving to the database.
        Task<BillSummary> PreviewBillAsync(int orderId);

        // Find an order using the customer-facing order number.
        Task<int?> FindOrderIdByOrderNoAsync(string orderNo);

        Task<bool> CanGenerateBillAsync(int orderId);

        // US32: Get the combined bill for a dining session.
        Task<Bill?> GetBillForSessionAsync(int sessionId);

        // US32: One bill (status and totals only). Null if not found.
        Task<Bill?> GetBillByIdAsync(int billId);

        // US32: Billing facts about one order, found by its order number
        // (order type, kitchen status, dining session and bill).
        // Null if the order does not exist.
        Task<OrderBillingInfo?> GetOrderBillingInfoAsync(string orderNo);

        // US32 (called by Member 3): true once the session's bill is
        // Confirmed or Paid. Block new dine-in orders and partial
        // cancellations for the session while this returns true.
        Task<bool> IsSessionLockedAsync(int sessionId);

        // US32 (called by Member 2 before closing a session):
        // true when every non-cancelled order in the session is linked
        // to a Paid bill whose total still matches its orders, and no
        // payment for those bills is still Pending.
        // A session with no chargeable orders is settled (nothing to pay).
        Task<bool> IsSessionFinanciallySettledAsync(int sessionId);

        // US32: Create or retrieve the Bill for a takeaway order.
        // Uses saved Order amounts without recalculation.
        Task<Bill> CreateTakeawayBillAsync(int orderId);

        // US32: Confirm a draft (takeaway) Bill and lock its billing contents.
        Task<Bill> ConfirmBillAsync(int billId, string staffUserId);

        // US32: Read-only preview of the combined bill for a dining session.
        // Sums the saved amounts of the session's non-cancelled orders.
        // Nothing is written to the database.
        Task<SessionBillPreview> PreviewSessionBillAsync(int sessionId);

        // US32: Staff confirm the combined bill for a dining session.
        // orderSnapshot must be the OrderSnapshot from the preview the
        // staff member looked at; if the orders changed since then,
        // confirmation is refused and the preview must be refreshed.
        // If some orders are still in the kitchen and
        // acknowledgeKitchenWarning is false, nothing is saved and the
        // result is KitchenWarningRequired.
        Task<SessionBillConfirmResult> ConfirmSessionBillAsync(
            int sessionId,
            string staffUserId,
            string orderSnapshot,
            bool acknowledgeKitchenWarning);

        // US32 (staff billing list, dine-in tab): every open dining session,
        // plus closed sessions whose bill was paid today. Read only.
        // Older history is for US40 (admin payment monitoring).
        Task<IReadOnlyList<SessionBillingRow>> GetSessionBillingRowsAsync();

        // US32 (staff billing list, takeaway tab): takeaway orders not paid
        // yet (any day, oldest first), plus takeaway bills paid today.
        // Read only.
        Task<IReadOnlyList<TakeawayBillingRow>> GetTakeawayBillingRowsAsync();

        // US32: Where a bill belongs, so a bill number (YB-000012) can be
        // opened: its dining session, or its takeaway order number.
        // Null if the bill does not exist.
        Task<BillLocation?> FindBillLocationAsync(int billId);
    }

    // Represents one item displayed on the bill.
    // This is not a database entity.
    public sealed record BillItemSummary(
        int MenuItemId,
        int Quantity,
        decimal UnitPrice,
        decimal LineTotal,
        string MenuItemName = ""
    );

    // Represents the complete bill displayed to staff.
    // This is not a database entity.
    public sealed record BillSummary(
        int OrderId,
        string OrderNo,
        IReadOnlyList<BillItemSummary> Items,
        decimal Subtotal,
        decimal TaxRatePercent,
        decimal TaxAmount,
        decimal ServiceChargePercent,
        decimal ServiceCharge,
        decimal TotalAmount
    );

    // US32: One order (ordering round) inside a session bill preview.
    // Amounts are the values saved on the Order. Not a database entity.
    public sealed record SessionBillOrderLine(
        int OrderId,
        string OrderNo,
        OrderStatus Status,
        IReadOnlyList<BillItemSummary> Items,
        decimal Subtotal,
        decimal TaxAmount,
        decimal ServiceCharge,
        decimal TotalAmount
    );

    // US32: Combined bill preview for one dining session.
    // Not a database entity.
    public sealed record SessionBillPreview(
        int SessionId,
        int TableId,
        string TableNumber,
        DiningSessionStatus SessionStatus,
        IReadOnlyList<SessionBillOrderLine> Orders,
        decimal Subtotal,
        decimal TaxRatePercent,
        decimal TaxAmount,
        decimal ServiceChargePercent,
        decimal ServiceCharge,
        decimal TotalAmount,
        // Order numbers still Pending / Preparing / ReadyToServe.
        IReadOnlyList<string> OrdersInKitchen,
        // The session's existing bill, if it was already confirmed.
        int? ExistingBillId,
        BillStatus? ExistingBillStatus,
        // Pass back to ConfirmSessionBillAsync unchanged.
        string OrderSnapshot
    )
    {
        public bool HasOrdersInKitchen => OrdersInKitchen.Count > 0;

        public bool CanConfirm =>
            SessionStatus == DiningSessionStatus.Open &&
            ExistingBillId == null &&
            Orders.Count > 0;
    }

    // US32: Billing facts about one order. Not a database entity.
    public sealed record OrderBillingInfo(
        int OrderId,
        string OrderNo,
        OrderType OrderType,
        OrderStatus Status,
        int? DiningSessionId,
        int? BillId
    );

    // US32: Outcome of ConfirmSessionBillAsync.
    public enum SessionBillConfirmStatus
    {
        Confirmed,
        KitchenWarningRequired
    }

    public sealed record SessionBillConfirmResult(
        SessionBillConfirmStatus Status,
        Bill? Bill,
        IReadOnlyList<string> OrdersInKitchen
    );

    // US32: One row of the staff billing list (dine-in tab).
    // TotalAmount is the confirmed bill total once a bill exists,
    // otherwise the sum of the saved amounts of the non-cancelled orders.
    // Not a database entity.
    public sealed record SessionBillingRow(
        int SessionId,
        string TableNumber,
        DateTime OpenedAt,
        string? ReservationRef,
        string? CustomerName,
        int OrderCount,
        int OrdersInKitchenCount,
        decimal TotalAmount,
        int? BillId,
        BillStatus? BillStatus,
        // True for a session already closed (shown only when paid today).
        bool IsClosed,
        // When the bill was paid (latest Paid payment), if it is Paid.
        DateTime? PaidAt
    );

    // US32: One row of the staff billing list (takeaway tab).
    // Not a database entity.
    public sealed record TakeawayBillingRow(
        int OrderId,
        string OrderNo,
        DateTime CreatedAt,
        string? CustomerName,
        OrderStatus OrderStatus,
        decimal TotalAmount,
        int? BillId,
        BillStatus? BillStatus,
        // When the bill was paid (shown only for bills paid today).
        DateTime? PaidAt
    );

    // US32: Where a bill belongs. DiningSessionId for a dine-in bill,
    // TakeawayOrderNo for a takeaway bill. Not a database entity.
    public sealed record BillLocation(
        int BillId,
        int? DiningSessionId,
        string? TakeawayOrderNo
    );
}
