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

        // US24 INTEGRATION POINT (Member 3):
        // Call this method from OrderService or the Checkout PageModel
        // to calculate the full checkout amount BEFORE saving an Order.
        //
        // This method does NOT create an Order, modify OrderItems,
        // or save any changes to the database.
        //
        // IMPORTANT:
        // Unit prices must be verified against MenuItem prices
        // on the server before calling this method.
        Task<CheckoutSummary> CalculateCheckoutAsync(
            IReadOnlyList<CheckoutItemInput> items);

        // Find an order using the customer-facing order number.
        Task<int?> FindOrderIdByOrderNoAsync(string orderNo);

        Task<bool> CanGenerateBillAsync(int orderId);
    }

    // Represents one item displayed on the bill.
    // This is not a database entity.
    public sealed record BillItemSummary(
        int MenuItemId,
        int Quantity,
        decimal UnitPrice,
        decimal LineTotal
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


    // US24: Input for checkout calculation.
    // Prices must come from trusted server-side menu data.
    public sealed record CheckoutItemInput(
        int MenuItemId,
        int Quantity,
        decimal UnitPrice);

    // US24: Full amount shown to the customer at checkout.
    // This is a preview only, NOT a finalized bill.
    public sealed record CheckoutSummary(
        decimal Subtotal,
        decimal TaxRatePercent,
        decimal TaxAmount,
        decimal ServiceChargePercent,
        decimal ServiceCharge,
        decimal TotalAmount);

}