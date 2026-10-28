using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    public class BillingService : IBillingService
    {
        private readonly IRepository<Order> _orders;
        private readonly IRepository<Payment> _payments;
        private readonly IRepository<Bill> _bills;

        private readonly IBillingTransactionRepository _billingTransaction;

        public BillingService(
            IRepository<Order> orders,
            IRepository<Payment> payments,
            IRepository<Bill> bills,
            IBillingTransactionRepository billingTransaction)
        {
            _orders = orders;
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

                    // Save the Bill first so it gets an Id.
                    // This runs inside the same transaction and lock as the
                    // link below, so if the link fails the Bill is rolled back.
                    await _bills.AddAsync(bill);
                    await _bills.SaveChangesAsync();

                    int billId = bill.Id;
                    decimal subtotal = bill.Subtotal;
                    decimal taxAmount = bill.TaxAmount;
                    decimal serviceCharge = bill.ServiceCharge;
                    decimal totalAmount = bill.TotalAmount;

                    // US32: Conditional update (compare-and-set).
                    // Link the Order only if it is still a takeaway order,
                    // not cancelled, not billed yet, and its saved amounts
                    // are exactly the amounts copied into the Bill.
                    // Same pattern as Member 3's AcceptStaffOrderAsync, so
                    // other modules never get a concurrency exception.
                    int linked = await _orders.Query()
                        .Where(o =>
                            o.Id == orderId &&
                            o.OrderType == OrderType.Takeaway &&
                            o.BillId == null &&
                            o.Status != OrderStatus.Cancelled &&
                            o.Subtotal == subtotal &&
                            o.TaxAmount == taxAmount &&
                            o.ServiceCharge == serviceCharge &&
                            o.TotalAmount == totalAmount)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(o => o.BillId, (int?)billId));

                    // 0 rows = the Order changed after it was read
                    // (cancelled, amounts updated or billed elsewhere).
                    // Throwing here skips the commit, so the new Bill
                    // is rolled back together with the failed link.
                    if (linked != 1)
                    {
                        throw new InvalidOperationException(
                            "The order changed while the bill was being created. " +
                            "Please refresh and try again.");
                    }

                    return bill;
                });
        }



        // US32: Confirm a Draft Bill (used by takeaway bills created by
        // CreateTakeawayBillAsync). Dine-in session bills will use a
        // separate session confirmation method after Member 2's
        // DiningSession (PR #96) is merged.
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

            // Read-only copy for validation. The status change below is a
            // conditional update, so nothing is saved through this entity.
            var bill = await _bills.Query()
                .AsNoTracking()
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

            DateTime confirmedAt = DateTime.Now;
            string confirmedBy = staffUserId.Trim();

            // US32: Conditional update (compare-and-set).
            // Only one request can move this Bill from Draft to Confirmed.
            // A double click or a second staff member gets a clear message
            // instead of silently overwriting ConfirmedAt / ConfirmedByUserId.
            int confirmed = await _bills.Query()
                .Where(b => b.Id == billId && b.Status == BillStatus.Draft)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(b => b.Status, BillStatus.Confirmed)
                    .SetProperty(b => b.ConfirmedAt, (DateTime?)confirmedAt)
                    .SetProperty(b => b.ConfirmedByUserId, confirmedBy));

            if (confirmed != 1)
            {
                throw new InvalidOperationException(
                    "This bill has already been confirmed. Please refresh the page.");
            }

            // Return the confirmed values to the caller.
            bill.Status = BillStatus.Confirmed;
            bill.ConfirmedAt = confirmedAt;
            bill.ConfirmedByUserId = confirmedBy;

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
            var (taxRate, serviceChargeRate) = DeriveDisplayRates(order);

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
            var (taxRate, serviceChargeRate) = DeriveDisplayRates(order);

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
            var (taxRate, serviceChargeRate) = DeriveDisplayRates(order);

            return CreateSummary(
                order,
                taxRate,
                serviceChargeRate);
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


        // US32 / US49: Display-only percentages derived from saved amounts.
        // Orders do not store their historical rates, so derive them here.
        // Team rule (leader, US49): service = subtotal x service%;
        //                           SST     = (subtotal + service) x tax%.
        // Example: RM100 dine-in, 10% / 6% -> service RM10.00, SST RM6.60.
        private static (decimal TaxRate, decimal ServiceChargeRate)
            DeriveDisplayRates(Order order)
        {
            decimal serviceChargeRate = order.Subtotal > 0
                ? Math.Round(order.ServiceCharge / order.Subtotal * 100m, 2)
                : 0m;

            decimal taxBase = order.Subtotal + order.ServiceCharge;

            decimal taxRate = taxBase > 0
                ? Math.Round(order.TaxAmount / taxBase * 100m, 2)
                : 0m;

            return (taxRate, serviceChargeRate);
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
    }
}