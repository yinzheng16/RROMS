using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    public class BillingService : IBillingService
    {
        private readonly IRepository<Order> _orders;
        private readonly IRepository<RestaurantSetting> _settings;
        private readonly IRepository<Payment> _payments;
        private readonly IRepository<Bill> _bills;

        private readonly IBillingTransactionRepository _billingTransaction;

        public BillingService(
            IRepository<Order> orders,
            IRepository<RestaurantSetting> settings,
            IRepository<Payment> payments,
            IRepository<Bill> bills,
            IBillingTransactionRepository billingTransaction)
        {
            _orders = orders;
            _settings = settings;
            _payments = payments;
            _bills = bills;
            _billingTransaction = billingTransaction;
        }


        // US32: Create one Bill for one takeaway Order.
        // Use the amounts saved in the Order at checkout.
        // Never recalculate tax or service charge.
        public async Task<Bill> CreateTakeawayBillAsync(int orderId)
        {
            ValidateOrderId(orderId);

            // US32: Lock this Order before reading or creating its Bill.
            return await _billingTransaction.ExecuteForTakeawayOrderAsync(
                orderId,
                async () =>
                {
                    // Re-read the Order after acquiring the database lock.
                    // AsNoTracking prevents reuse of an older tracked Order.
                    var order = await _orders.Query()
                        .AsNoTracking()
                        .Include(o => o.Items)
                        .Include(o => o.Bill)
                        .FirstOrDefaultAsync(o => o.Id == orderId);

                    if (order == null)
                        throw new InvalidOperationException(
                            "Order does not exist.");

                    if (order.OrderType != OrderType.Takeaway)
                        throw new InvalidOperationException(
                            "This method only supports takeaway orders.");

                    if (order.Status == OrderStatus.Cancelled)
                        throw new InvalidOperationException(
                            "Cannot bill a cancelled order.");

                    ValidateOrderItems(order);

                    if (order.Subtotal <= 0 ||
                        order.TotalAmount <= 0 ||
                        order.TaxAmount < 0 ||
                        order.ServiceCharge < 0)
                    {
                        throw new InvalidOperationException(
                            "Order does not have valid saved amounts.");
                    }

                    if (order.TotalAmount !=
                        order.Subtotal + order.TaxAmount + order.ServiceCharge)
                    {
                        throw new InvalidOperationException(
                            "Saved order amounts are inconsistent.");
                    }

                    // Reuse an existing Bill instead of creating another.
                    if (order.BillId.HasValue)
                    {
                        if (order.Bill == null)
                            throw new InvalidOperationException(
                                "Order references a Bill that could not be loaded.");

                        return order.Bill;
                    }

                    bool hasActiveLegacyPayment = await _payments.Query()
                        .AsNoTracking()
                        .AnyAsync(p =>
                            p.OrderId == orderId &&
                            (p.Status == PaymentStatus.Pending ||
                             p.Status == PaymentStatus.Paid));

                    if (hasActiveLegacyPayment)
                        throw new InvalidOperationException(
                            "Resolve the existing payment before creating a Bill.");

                    var bill = new Bill
                    {
                        DiningSessionId = null,
                        Subtotal = order.Subtotal,
                        TaxAmount = order.TaxAmount,
                        ServiceCharge = order.ServiceCharge,
                        TotalAmount = order.TotalAmount,
                        Status = BillStatus.Draft
                    };

                    // Explicitly track the new Bill.
                    await _bills.AddAsync(bill);

                    // Load the Order as a tracked entity.
                    var trackedOrder = await _orders.Query()
                        .FirstAsync(o => o.Id == orderId);

                    // Verify that the saved Order amounts have not changed
                    // since the Bill was prepared.
                    if (trackedOrder.Subtotal != bill.Subtotal ||
                        trackedOrder.TaxAmount != bill.TaxAmount ||
                        trackedOrder.ServiceCharge != bill.ServiceCharge ||
                        trackedOrder.TotalAmount != bill.TotalAmount)
                    {
                        throw new InvalidOperationException(
                            "Order amounts changed during Bill creation. Please retry.");
                    }

                    // Link only the Bill relationship.
                    trackedOrder.Bill = bill;

                    // Save Bill and Order in the same transaction.
                    await _orders.SaveChangesAsync();

                    return bill;
                });
        }



        // TODO (US32 - MEMBER 2 INTEGRATION):
        // Verify all chargeable Orders in this DiningSession
        // are included in the Bill before confirmation.
        // Reject Orders belonging to another DiningSession.
        public async Task<Bill> ConfirmBillAsync(
            int billId,
            string staffUserId)
        {
            if (billId <= 0)
                throw new ArgumentException(
                    "Invalid bill ID.", nameof(billId));

            if (string.IsNullOrWhiteSpace(staffUserId))
                throw new ArgumentException(
                    "Staff user ID is required.", nameof(staffUserId));

            var bill = await _bills.Query()
                .Include(b => b.Orders)
                .FirstOrDefaultAsync(b => b.Id == billId);

            if (bill == null)
                throw new InvalidOperationException(
                    "Bill does not exist.");

            if (bill.Status != BillStatus.Draft)
                throw new InvalidOperationException(
                    "Only draft bills can be confirmed.");

            // US32: A Bill must contain a valid positive total.
            if (bill.Subtotal <= 0 ||
                bill.TaxAmount < 0 ||
                bill.ServiceCharge < 0 ||
                bill.TotalAmount <= 0)
            {
                throw new InvalidOperationException(
                    "Cannot confirm a Bill with invalid amounts.");
            }

            if (bill.Orders.Any(o =>
                o.Status == OrderStatus.Cancelled))
            {
                throw new InvalidOperationException(
                    "Bill contains a cancelled order.");
            }

            if (bill.Orders.Any(o =>
                o.Subtotal < 0 ||
                o.TaxAmount < 0 ||
                o.ServiceCharge < 0 ||
                o.TotalAmount <= 0 ||
                o.TotalAmount !=
                    o.Subtotal + o.TaxAmount + o.ServiceCharge))
            {
                throw new InvalidOperationException(
                    "Bill contains invalid saved order amounts.");
            }

            // US32: Aggregate existing saved amounts only.
            // Never recalculate SST or service charge.
            decimal subtotal = bill.Orders.Sum(o => o.Subtotal);
            decimal taxAmount = bill.Orders.Sum(o => o.TaxAmount);
            decimal serviceCharge = bill.Orders.Sum(o => o.ServiceCharge);
            decimal totalAmount = bill.Orders.Sum(o => o.TotalAmount);

            if (bill.Subtotal != subtotal ||
                bill.TaxAmount != taxAmount ||
                bill.ServiceCharge != serviceCharge ||
                bill.TotalAmount != totalAmount)
            {
                throw new InvalidOperationException(
                    "Bill totals do not match saved order amounts.");
            }

            bool hasActivePayment = await _payments.Query()
                .AsNoTracking()
                .AnyAsync(p =>
                    p.BillId == billId &&
                    (p.Status == PaymentStatus.Pending ||
                     p.Status == PaymentStatus.Paid));

            if (hasActivePayment)
                throw new InvalidOperationException(
                    "Cannot confirm a bill with an existing pending or completed payment.");

            bill.Status = BillStatus.Confirmed;
            bill.ConfirmedAt = DateTime.UtcNow;
            bill.ConfirmedByUserId = staffUserId.Trim();

            await _bills.SaveChangesAsync();

            return bill;
        }


        // US32: Retrieve the combined bill for a dining session.
        public async Task<Bill?> GetBillForSessionAsync(int sessionId)
        {
            if (sessionId <= 0)
                throw new ArgumentException("Invalid session ID.");

            return await _bills.Query()
                .AsNoTracking()
                .SingleOrDefaultAsync(b =>
                    b.DiningSessionId == sessionId);
        }

        // US32: Confirmed or paid bills lock the dining session.
        public async Task<bool> IsSessionLockedAsync(int sessionId)
        {
            if (sessionId <= 0)
                return false;

            return await _bills.Query()
                .AsNoTracking()
                .AnyAsync(b =>
                    b.DiningSessionId == sessionId &&
                    (b.Status == BillStatus.Confirmed ||
                     b.Status == BillStatus.Paid));
        }

        // TODO (US32 - MEMBER 2 INTEGRATION):
        // Implement after Order.DiningSessionId is merged.
        // Verify all chargeable orders belong to Paid bills
        // and no payments remain Pending.
        public Task<bool> IsSessionFinanciallySettledAsync(int sessionId)
        {
            if (sessionId <= 0)
                return Task.FromResult(false);

            return Task.FromResult(false);
        }

        public async Task<int?> FindOrderIdByOrderNoAsync(
            string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                throw new ArgumentException(
                    "Order number is required.");

            string normalizedOrderNo = orderNo.Trim();

            return await _orders.Query()
                .AsNoTracking()
                .Where(o => o.OrderNo == normalizedOrderNo)
                .Select(o => (int?)o.Id)
                .SingleOrDefaultAsync();
        }


        // =====================================================
        // US24 INTEGRATION POINT - MEMBER 3
        // =====================================================
        // Provide checkout calculation method
        // Call CalculateCheckoutAsync() from OrderService or
        // the Checkout PageModel BEFORE creating an Order.
        //
        // Example:
        // var checkout = await _billingService.CalculateCheckoutAsync(
        //     checkoutItems);
        //
        // checkout.TotalAmount contains:
        // Subtotal + SST + Service Charge.
        //
        // IMPORTANT:
        // - Prices must be retrieved and verified server-side.
        // - Do NOT trust prices sent from the customer's browser.
        // - This method only calculates amounts.
        // - It does NOT save an Order or confirm a bill.
        // - Recalculate when the customer confirms the order.
        // =====================================================
        public async Task<CheckoutSummary> CalculateCheckoutAsync(
            IReadOnlyList<CheckoutItemInput> items)
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException(
                    "Checkout must contain at least one item.");

            if (items.Any(item =>
                item.MenuItemId <= 0 ||
                item.Quantity <= 0 ||
                item.UnitPrice < 0))
            {
                throw new ArgumentException(
                    "Checkout contains invalid item values.");
            }

            decimal subtotal = items.Sum(item =>
                item.Quantity * item.UnitPrice);

            if (subtotal <= 0)
                throw new InvalidOperationException(
                    "Checkout subtotal must be positive.");

            var settings = await GetSettingsAsync();


            var amounts = CalculateAmounts(
                subtotal,
                settings.TaxRatePercent,
                settings.ServiceChargePercent);


            return new CheckoutSummary(
                amounts.Subtotal,
                settings.TaxRatePercent,
                amounts.TaxAmount,
                settings.ServiceChargePercent,
                amounts.ServiceCharge,
                amounts.TotalAmount);
        }


        public async Task<bool> CanGenerateBillAsync(int orderId)
        {
            if (orderId <= 0)
                return false;

            var order = await _orders.Query()
                .AsNoTracking()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null ||
                order.Status == OrderStatus.Cancelled ||
                order.BillId.HasValue)
                return false;

            if (!HasValidOrderItems(order))
                return false;

            bool hasActivePayment = await _payments.Query()
                .AsNoTracking()
                .AnyAsync(p =>
                    p.OrderId == orderId &&
                    (p.Status == PaymentStatus.Pending ||
                     p.Status == PaymentStatus.Paid));

            return !hasActivePayment;
        }


        // US32: Preview the amounts already saved in the Order.
        // Do not recalculate tax or service charge using current settings.
        // This method does not modify the database.
        public async Task<BillSummary> PreviewBillAsync(int orderId)
        {
            ValidateOrderId(orderId);

            var order = await _orders.Query()
                .AsNoTracking()
                .Include(o => o.Items)
                    .ThenInclude(item => item.MenuItem)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new InvalidOperationException(
                    "Order does not exist.");

            if (order.Status == OrderStatus.Cancelled)
                throw new InvalidOperationException(
                    "Cannot preview a cancelled order.");

            ValidateOrderItems(order);

            if (order.TotalAmount <= 0)
                throw new InvalidOperationException(
                    "Order does not have a valid saved total.");

            // US32: Historical rates are not stored in Order.
            // Derive display percentages from saved amounts only.
            // Never use current restaurant settings for an existing order.
            decimal taxRate = order.Subtotal > 0
                ? Math.Round(order.TaxAmount / order.Subtotal * 100m, 2)
                : 0m;

            decimal serviceChargeRate = order.Subtotal > 0
                ? Math.Round(order.ServiceCharge / order.Subtotal * 100m, 2)
                : 0m;

            return CreateSummary(
                order,
                taxRate,
                serviceChargeRate);
        }



        // US32: Read the amounts already saved in the Order.
        // Never recalculate historical bills using current settings.
        public async Task<BillSummary> GetBillAsync(int orderId)
        {
            ValidateOrderId(orderId);

            var order = await _orders.Query()
                .AsNoTracking()
                .Include(o => o.Items)
                    .ThenInclude(item => item.MenuItem)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new InvalidOperationException(
                    "Order does not exist.");

            if (!order.Items.Any())
                throw new InvalidOperationException(
                    "Order has no items. Cannot display a bill.");

            if (order.TotalAmount <= 0)
                throw new InvalidOperationException(
                    "Order does not have a valid saved total.");

            // Display-only percentages derived from saved amounts.
            // These are estimates, not stored historical tax rates.
            decimal taxRate = order.Subtotal > 0
                ? Math.Round(
                    order.TaxAmount / order.Subtotal * 100m, 2)
                : 0m;

            decimal serviceChargeRate = order.Subtotal > 0
                ? Math.Round(
                    order.ServiceCharge / order.Subtotal * 100m, 2)
                : 0m;

            return CreateSummary(
                order,
                taxRate,
                serviceChargeRate);
        }

        // US32: Generate the legacy single-order bill summary.
        // Use amounts already saved when the Order was created.
        // Do not recalculate or overwrite Order amounts.
        //
        // This method does not create a Bill database entity.
        // Combined Bill creation is handled separately.
        public async Task<BillSummary> GenerateBillAsync(int orderId)
        {
            ValidateOrderId(orderId);

            var order = await _orders.Query()
                .AsNoTracking()
                .Include(o => o.Items)
                    .ThenInclude(item => item.MenuItem)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new InvalidOperationException(
                    "Order does not exist.");

            if (order.Status == OrderStatus.Cancelled)
                throw new InvalidOperationException(
                    "Cannot generate a bill for a cancelled order.");

            ValidateOrderItems(order);

            if (order.Subtotal <= 0 || order.TotalAmount <= 0)
                throw new InvalidOperationException(
                    "Order does not have valid saved amounts.");

            if (order.TotalAmount !=
                order.Subtotal + order.TaxAmount + order.ServiceCharge)
            {
                throw new InvalidOperationException(
                    "Saved order amounts are inconsistent.");
            }

            // Do not generate a new legacy bill for an order
            // already associated with a combined or takeaway Bill.
            if (order.BillId.HasValue)
                throw new InvalidOperationException(
                    "Order is already linked to a Bill.");

            bool hasActivePayment = await _payments.Query()
                .AsNoTracking()
                .AnyAsync(p =>
                    p.OrderId == orderId &&
                    (p.Status == PaymentStatus.Pending ||
                     p.Status == PaymentStatus.Paid));

            if (hasActivePayment)
                throw new InvalidOperationException(
                    "Order already has a pending or completed payment.");

            // Display-only percentages derived from saved amounts.
            // Do not use current restaurant settings.
            decimal taxRate = Math.Round(
                order.TaxAmount / order.Subtotal * 100m, 2);

            decimal serviceChargeRate = Math.Round(
                order.ServiceCharge / order.Subtotal * 100m, 2);

            return CreateSummary(
                order,
                taxRate,
                serviceChargeRate);
        }


        private async Task<RestaurantSetting> GetSettingsAsync()
        {
            var settings = await _settings.Query()
                .AsNoTracking()
                .OrderBy(s => s.Id)
                .FirstOrDefaultAsync();

            if (settings == null)
                throw new InvalidOperationException(
                    "Restaurant settings have not been configured.");

            if (settings.TaxRatePercent < 0 ||
                settings.TaxRatePercent > 100 ||
                settings.ServiceChargePercent < 0 ||
                settings.ServiceChargePercent > 100)
            {
                throw new InvalidOperationException(
                    "Restaurant settings contain invalid rates.");
            }

            return settings;
        }


        private static void ValidateOrderId(int orderId)
        {
            if (orderId <= 0)
                throw new ArgumentException("Invalid order ID.");
        }

        private static bool HasValidOrderItems(Order order)
        {
            if (!order.Items.Any())
                return false;

            if (order.Items.Any(item =>
                item.Quantity <= 0 || item.UnitPrice < 0))
                return false;

            return order.Items.Sum(item =>
                item.Quantity * item.UnitPrice) > 0;
        }

        private static void ValidateOrderItems(Order order)
        {
            if (!order.Items.Any())
                throw new InvalidOperationException(
                    "Cannot generate a bill for an empty order.");

            if (order.Items.Any(item =>
                item.Quantity <= 0 || item.UnitPrice < 0))
                throw new InvalidOperationException(
                    "Order contains invalid item values.");
        }


        // SHARED BILLING CALCULATION:
        // Used by US24 Checkout and US32 Bill Generation.
        // Keep all SST and Service Charge calculations here.
        private static BillingAmounts CalculateAmounts(
            decimal subtotal,
            decimal taxRate,
            decimal serviceChargeRate)
        {
            subtotal = RoundMoney(subtotal);

            if (subtotal <= 0)
                throw new InvalidOperationException(
                    "Order subtotal must be positive.");

            decimal taxAmount = RoundMoney(
                subtotal * taxRate / 100m);

            decimal serviceCharge = RoundMoney(
                subtotal * serviceChargeRate / 100m);

            decimal totalAmount =
                subtotal + taxAmount + serviceCharge;

            return new BillingAmounts(
                subtotal,
                taxAmount,
                serviceCharge,
                totalAmount);
        }


        // Apply calculated amounts to an Order object.
        private static void ApplyAmounts(
            Order order,
            BillingAmounts amounts)
        {
            order.Subtotal = amounts.Subtotal;
            order.TaxAmount = amounts.TaxAmount;
            order.ServiceCharge = amounts.ServiceCharge;
            order.TotalAmount = amounts.TotalAmount;
        }

        private static decimal RoundMoney(decimal amount)
        {
            return Math.Round(
                amount, 2, MidpointRounding.AwayFromZero);
        }

        private static BillSummary CreateSummary(
            Order order,
            decimal taxRate,
            decimal serviceChargeRate)
        {
            var items = order.Items
                .OrderBy(item => item.Id)
                .Select(item => new BillItemSummary(
                    item.MenuItemId,
                    item.Quantity,
                    item.UnitPrice,
                    RoundMoney(
                        item.Quantity * item.UnitPrice),
                    item.MenuItem?.Name
                        ?? $"Menu Item #{item.MenuItemId}"))
                .ToList();

            return new BillSummary(
                order.Id,
                order.OrderNo,
                items,
                order.Subtotal,
                taxRate,
                order.TaxAmount,
                serviceChargeRate,
                order.ServiceCharge,
                order.TotalAmount);
        }

        private sealed record BillingAmounts(
            decimal Subtotal,
            decimal TaxAmount,
            decimal ServiceCharge,
            decimal TotalAmount);
    }
}
