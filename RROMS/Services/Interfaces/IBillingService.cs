using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    public interface IBillingService
    {
        // Read bill details without modifying the database.
        Task<BillSummary> GetBillAsync(int orderId);

        // Calculate and save the bill for an unpaid order.
        Task<BillSummary> GenerateBillAsync(int orderId);

        // Calculate a bill preview without saving to the database.
        Task<BillSummary> PreviewBillAsync(int orderId);

        // Find an order using the customer-facing order number.
        Task<int?> FindOrderIdByOrderNoAsync(string orderNo);

        Task<bool> CanGenerateBillAsync(int orderId);

        // US32: Get the combined bill for a dining session.
        Task<Bill?> GetBillForSessionAsync(int sessionId);

        // US32: Check whether a confirmed bill locks the session.
        Task<bool> IsSessionLockedAsync(int sessionId);

        // US32: Check whether all chargeable orders
        // in a session have been fully paid.
        Task<bool> IsSessionFinanciallySettledAsync(int sessionId);

        // US32: Create or retrieve the Bill for a takeaway order.
        // Uses saved Order amounts without recalculation.
        Task<Bill> CreateTakeawayBillAsync(int orderId);

        // US32: Confirm a draft Bill and lock its billing contents.
        Task<Bill> ConfirmBillAsync(int billId, string staffUserId);

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


}