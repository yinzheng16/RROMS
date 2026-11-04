using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 4 (US35 - PDF payment receipt)
    // Separate from PaymentService so the PaymentService constructor
    // (and the US31 tests that build it) stay unchanged.
    public class ReceiptService : IReceiptService
    {
        private readonly IRepository<Payment> _payments;
        private readonly IRepository<ApplicationUser> _users;

        // US49 (Member 5): restaurant details for the receipt.
        private readonly IReportService _reportService;

        private readonly IWebHostEnvironment _env;

        public ReceiptService(
            IRepository<Payment> payments,
            IRepository<ApplicationUser> users,
            IReportService reportService,
            IWebHostEnvironment env)
        {
            _payments = payments;
            _users = users;
            _reportService = reportService;
            _env = env;
        }

        public async Task<PaymentReceipt?> GetCustomerReceiptAsync(string customerId, int paymentId)
        {
            if (string.IsNullOrWhiteSpace(customerId) || paymentId <= 0)
                return null;

            string id = customerId.Trim();

            // Same ownership rule as My Payments (US34):
            // dine-in bill -> the session's customer; takeaway bill -> the order's
            // customer; older payment without a bill -> the order's customer.
            var payment = await PaidPaymentQuery(paymentId)
                .Where(p =>
                    (p.Bill != null &&
                     p.Bill.DiningSessionId != null &&
                     p.Bill.DiningSession!.CustomerId == id) ||
                    (p.Bill != null &&
                     p.Bill.DiningSessionId == null &&
                     p.Bill.Orders.Any(o => o.UserId == id)) ||
                    (p.BillId == null &&
                     p.Order != null &&
                     p.Order.UserId == id))
                .FirstOrDefaultAsync();

            return payment == null ? null : await BuildAsync(payment);
        }

        public async Task<PaymentReceipt?> GetReceiptAsync(int paymentId)
        {
            if (paymentId <= 0)
                return null;

            var payment = await PaidPaymentQuery(paymentId).FirstOrDefaultAsync();

            return payment == null ? null : await BuildAsync(payment);
        }

        // One Paid payment with everything the receipt prints.
        private IQueryable<Payment> PaidPaymentQuery(int paymentId) =>
            _payments.Query()
                .AsNoTracking()
                .AsSplitQuery()
                .Include(p => p.Bill!).ThenInclude(b => b.DiningSession!).ThenInclude(s => s.Table)
                .Include(p => p.Bill!).ThenInclude(b => b.Orders).ThenInclude(o => o.Items).ThenInclude(i => i.MenuItem)
                .Include(p => p.Order!).ThenInclude(o => o.Items).ThenInclude(i => i.MenuItem)
                .Include(p => p.Order!).ThenInclude(o => o.DiningSession!).ThenInclude(s => s.Table)
                .Where(p => p.Id == paymentId && p.Status == PaymentStatus.Paid);

        private async Task<PaymentReceipt> BuildAsync(Payment payment)
        {
            // The orders on the receipt (cancelled orders are not charged).
            List<Order> orders = payment.Bill != null
                ? payment.Bill.Orders
                    .Where(o => o.Status != OrderStatus.Cancelled)
                    .OrderBy(o => o.Id)
                    .ToList()
                : new List<Order> { payment.Order! };

            bool isDineIn = payment.Bill != null
                ? payment.Bill.DiningSessionId != null
                : payment.Order!.OrderType == OrderType.DineIn;

            string? tableNumber = payment.Bill != null
                ? payment.Bill.DiningSession?.Table?.TableNumber
                : payment.Order?.DiningSession?.Table?.TableNumber;

            // Totals: the saved bill amounts (or the saved order amounts).
            decimal subtotal = payment.Bill?.Subtotal ?? payment.Order!.Subtotal;
            decimal service = payment.Bill?.ServiceCharge ?? payment.Order!.ServiceCharge;
            decimal tax = payment.Bill?.TaxAmount ?? payment.Order!.TaxAmount;
            decimal total = payment.Bill?.TotalAmount ?? payment.Order!.TotalAmount;

            var settings = await _reportService.GetRestaurantSettingsAsync();

            return new PaymentReceipt
            {
                PaymentId = payment.Id,
                BillId = payment.BillId,
                OrderType = isDineIn ? OrderType.DineIn : OrderType.Takeaway,
                TableNumber = tableNumber,
                OrderCount = orders.Count,
                OrderNo = orders.FirstOrDefault()?.OrderNo,
                Lines = CombineLines(orders),
                Subtotal = subtotal,
                ServiceCharge = service,
                TaxAmount = tax,
                TotalAmount = total,
                Method = payment.Method,
                AmountPaid = payment.Amount,
                PaidAt = payment.PaidAt ?? payment.CreatedAt,
                TransactionRef = string.IsNullOrWhiteSpace(payment.TransactionRef)
                    ? null
                    : payment.TransactionRef.Trim(),
                CashierName = await GetStaffNameAsync(payment.RecordedByUserId),
                Address = Blank(settings.Address),
                Phone = Blank(PhoneDisplay.Pretty(settings.Phone)),
                SstRegistrationNo = Blank(settings.SstRegistrationNo),
                ReceiptFooterNote = Blank(settings.ReceiptFooterNote)
            };
        }

        public byte[] CreatePdf(PaymentReceipt receipt)
        {
            ArgumentNullException.ThrowIfNull(receipt);

            // The same logo the staff printed bill uses.
            string logoPath = Path.Combine(_env.WebRootPath, "images", "yoru-icon.png");
            byte[]? logo = File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;

            ReceiptPdfDocument.EnsureFonts(_env.ContentRootPath);
            return ReceiptPdfDocument.Create(receipt, logo);
        }

        // All ordering rounds in one list; the same dish at the same
        // unit price is added up (keeps the order dishes first appear).
        private static List<ReceiptItem> CombineLines(IEnumerable<Order> orders) =>
            orders
                .SelectMany(o => o.Items)
                .GroupBy(i => (
                    Name: string.IsNullOrWhiteSpace(i.MenuItem?.Name)
                        ? $"Item #{i.MenuItemId}"
                        : i.MenuItem.Name,
                    i.UnitPrice))
                .Select(g => new ReceiptItem(
                    g.Key.Name,
                    g.Sum(i => i.Quantity),
                    g.Key.UnitPrice,
                    g.Sum(i => i.Quantity * i.UnitPrice)))
                .ToList();

        // Staff name for counter payments; online payments have none.
        private async Task<string?> GetStaffNameAsync(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            var name = await _users.Query()
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync();

            return Blank(name);
        }

        private static string? Blank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
