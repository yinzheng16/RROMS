
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

        public BillingService(
            IRepository<Order> orders,
            IRepository<RestaurantSetting> settings,
            IRepository<Payment> payments)
        {
            _orders = orders;
            _settings = settings;
            _payments = payments;
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
                order.Status == OrderStatus.Cancelled)
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

        // Preview only. No database changes.
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

            var settings = await GetSettingsAsync();

            var amounts = CalculateAmounts(
                order.Items.Sum(item =>
                    item.Quantity * item.UnitPrice),
                settings.TaxRatePercent,
                settings.ServiceChargePercent);

            // The order is not tracked, so this is preview-only.
            ApplyAmounts(order, amounts);

            return CreateSummary(
                order,
                settings.TaxRatePercent,
                settings.ServiceChargePercent);
        }

        // Read previously saved amounts.
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

            // Temporary until an explicit billing state is added.
            if (order.TotalAmount <= 0)
                throw new InvalidOperationException(
                    "Bill has not been generated yet.");

            // Temporary: historical rates are not yet stored.
            var settings = await GetSettingsAsync();

            return CreateSummary(
                order,
                settings.TaxRatePercent,
                settings.ServiceChargePercent);
        }

        // Calculate and persist amounts for an unpaid order.
        public async Task<BillSummary> GenerateBillAsync(int orderId)
        {
            ValidateOrderId(orderId);

            var order = await _orders.Query()
                .Include(o => o.Items)
                    .ThenInclude(item => item.MenuItem)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new InvalidOperationException(
                    "Order does not exist.");

            if (order.Status == OrderStatus.Cancelled)
                throw new InvalidOperationException(
                    "Cannot generate a bill for a cancelled order.");

            bool hasPaidPayment = await _payments.Query()
                .AsNoTracking()
                .AnyAsync(p =>
                    p.OrderId == orderId &&
                    p.Status == PaymentStatus.Paid);

            if (hasPaidPayment)
                throw new InvalidOperationException(
                    "Cannot recalculate a paid bill.");

            bool hasPendingPayment = await _payments.Query()
                .AsNoTracking()
                .AnyAsync(p =>
                    p.OrderId == orderId &&
                    p.Status == PaymentStatus.Pending);

            if (hasPendingPayment)
                throw new InvalidOperationException(
                    "Cannot recalculate a bill with a pending payment.");

            ValidateOrderItems(order);

            var settings = await GetSettingsAsync();

            var amounts = CalculateAmounts(
                order.Items.Sum(item =>
                    item.Quantity * item.UnitPrice),
                settings.TaxRatePercent,
                settings.ServiceChargePercent);

            ApplyAmounts(order, amounts);
            order.UpdatedAt = DateTime.UtcNow;

            await _orders.SaveChangesAsync();

            return CreateSummary(
                order,
                settings.TaxRatePercent,
                settings.ServiceChargePercent);
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
