using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Staff.Billing
{
    // Owner: Member 4 (US32 - Generate Bill, counter payment)
    //
    // One bill on its own page, opened from the billing list (Index).
    //   Left:  the printable bill.
    //   Right: the current step only - confirm the bill, record the
    //          counter payment, or the payment details once Paid.
    // Dine-in: /Staff/Billing/Bill?sessionId=2   (combined bill per session)
    // Takeaway: /Staff/Billing/Bill?orderNo=YR... (one order, one bill)
    // Any bill:  /Staff/Billing/Bill?billNo=YB-000012 (sent on to one of the above)
    // Order status (kitchen) and bill / payment status stay separate.
    public class BillModel : PageModel
    {
        // After a POST the browser jumps straight to the action panel.
        private const string ActionsFragment = "billActions";
        private const string ThisPage = "/Staff/Billing/Bill";

        private readonly IBillingService _billingService;
        private readonly IPaymentService _paymentService;

        // US49 (Member 5): restaurant settings for the printed bill.
        private readonly IReportService _reportService;

        private readonly UserManager<ApplicationUser> _userManager;

        public BillModel(
            IBillingService billingService,
            IPaymentService paymentService,
            IReportService reportService,
            UserManager<ApplicationUser> userManager)
        {
            _billingService = billingService;
            _paymentService = paymentService;
            _reportService = reportService;
            _userManager = userManager;
        }

        // ---------- Query string ----------

        // Dine-in: the dining session (table visit) being billed.
        [BindProperty(SupportsGet = true)]
        public int? SessionId { get; set; }

        // Takeaway: the order number being billed.
        [BindProperty(SupportsGet = true)]
        public string? OrderNo { get; set; }

        // Any bill by its bill number (YB-000012, YB12 or 12).
        [BindProperty(SupportsGet = true, Name = "billNo")]
        public string? BillNumber { get; set; }

        // ---------- Page data ----------

        // Dine-in bill (preview or confirmed).
        public SessionBillPreview? SessionBill { get; private set; }

        // Takeaway bill (preview or confirmed).
        public OrderBillingInfo? TakeawayOrder { get; private set; }
        public BillSummary? TakeawaySummary { get; private set; }

        // The saved bill, once it has been confirmed (null = preview only).
        public Bill? CurrentBill { get; private set; }

        // Payments for the current bill (and older order payments), newest first.
        public IReadOnlyList<Payment> Payments { get; private set; }
            = Array.Empty<Payment>();

        // Set when a confirmation was refused because orders are
        // still in the kitchen and the warning was not acknowledged.
        public IReadOnlyList<string> KitchenWarningOrders { get; private set; }
            = Array.Empty<string>();

        public string? ConfirmedByName { get; private set; }
        public string? LatestPaymentRecordedByName { get; private set; }

        public string? ErrorMessage { get; private set; }
        public string? SuccessMessage { get; private set; }

        // US32 / US49: Receipt details printed on the bill.
        public string? SstRegistrationNo { get; private set; }
        public string? ReceiptFooterNote { get; private set; }

        // ---------- Display helpers ----------

        public bool HasBill => SessionBill != null || TakeawaySummary != null;
        public bool IsDineIn => SessionBill != null;
        public bool IsPreview => CurrentBill == null || CurrentBill.Status == BillStatus.Draft;
        public bool IsConfirmed => CurrentBill?.Status == BillStatus.Confirmed;
        public bool IsPaid => CurrentBill?.Status == BillStatus.Paid;
        public Payment? LatestPayment => Payments.FirstOrDefault();

        // Orders still Pending / Preparing / ReadyToServe (dine-in only).
        public IReadOnlyList<string> OrdersInKitchen =>
            KitchenWarningOrders.Count > 0
                ? KitchenWarningOrders
                : SessionBill?.OrdersInKitchen ?? Array.Empty<string>();

        public bool CanConfirmDineIn =>
            SessionBill != null && SessionBill.CanConfirm;

        public bool CanConfirmTakeaway =>
            TakeawaySummary != null && IsPreview;

        // 1 = review and confirm, 2 = payment, 3 = paid.
        public int CurrentStep => IsPaid ? 3 : IsConfirmed ? 2 : 1;

        // Which list tab "Back to list" returns to.
        public string ListTab =>
            IsDineIn || SessionId.HasValue ? "dinein" : "takeaway";

        public string BillNo(int billId) => IndexModel.FormatBillNo(billId);

        // Runs before every handler (GET and POST).
        public override async Task OnPageHandlerExecutionAsync(
            PageHandlerExecutingContext context,
            PageHandlerExecutionDelegate next)
        {
            await LoadReceiptSettingsAsync();

            if (TempData["BillingSuccess"] is string message)
                SuccessMessage = message;

            await next();
        }

        // ---------- GET ----------

        public async Task<IActionResult> OnGetAsync()
        {
            if (SessionId is int sessionId)
            {
                await LoadSessionAsync(sessionId);
                return Page();
            }

            if (!string.IsNullOrWhiteSpace(BillNumber))
                return await OpenByBillNumberAsync(BillNumber);

            if (string.IsNullOrWhiteSpace(OrderNo))
                return RedirectToPage("/Staff/Billing/Index");

            OrderBillingInfo? info;

            try
            {
                info = await _billingService.GetOrderBillingInfoAsync(OrderNo);
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
                return Page();
            }

            if (info == null)
            {
                ErrorMessage = $"Order {OrderNo.Trim()} does not exist.";
                return Page();
            }

            // A dine-in order is billed with its table (combined bill).
            if (info.OrderType == OrderType.DineIn)
            {
                if (info.DiningSessionId is int orderSessionId)
                    return RedirectToPage(ThisPage, new { sessionId = orderSessionId });

                ErrorMessage =
                    "This dine-in order is not linked to a table yet. " +
                    "The guests must be seated before the bill can be prepared.";
                return Page();
            }

            await LoadTakeawayAsync(info);
            return Page();
        }

        // A bill number opens the bill through its table or takeaway order.
        private async Task<IActionResult> OpenByBillNumberAsync(string billNumber)
        {
            var match = Regex.Match(
                billNumber.Trim(),
                @"^(?:YB-?)?0*(\d{1,9})$",
                RegexOptions.IgnoreCase);

            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int billId))
            {
                ErrorMessage = $"{billNumber.Trim()} is not a valid bill number.";
                return Page();
            }

            var location = await _billingService.FindBillLocationAsync(billId);

            if (location?.DiningSessionId is int sessionId)
                return RedirectToPage(ThisPage, new { sessionId });

            if (!string.IsNullOrWhiteSpace(location?.TakeawayOrderNo))
                return RedirectToPage(ThisPage, new { orderNo = location.TakeawayOrderNo });

            ErrorMessage = $"Bill {IndexModel.FormatBillNo(billId)} does not exist.";
            return Page();
        }

        // ---------- POST: confirm dine-in (combined) bill ----------

        public async Task<IActionResult> OnPostConfirmSessionAsync(
            int sessionId,
            string orderSnapshot,
            bool acknowledgeKitchenWarning)
        {
            string? staffUserId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(staffUserId))
                return Forbid();

            try
            {
                var result = await _billingService.ConfirmSessionBillAsync(
                    sessionId,
                    staffUserId,
                    orderSnapshot,
                    acknowledgeKitchenWarning);

                if (result.Status == SessionBillConfirmStatus.KitchenWarningRequired)
                {
                    await ReloadAsync(sessionId, null);
                    KitchenWarningOrders = result.OrdersInKitchen;
                    return Page();
                }

                TempData["BillingSuccess"] =
                    $"Bill {BillNo(result.Bill!.Id)} confirmed. " +
                    "Ordering for this table is now locked.";

                return RedirectToPage(ThisPage, null, new { sessionId }, ActionsFragment);
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
            }

            await ReloadAsync(sessionId, null);
            return Page();
        }

        // ---------- POST: confirm takeaway bill ----------

        public async Task<IActionResult> OnPostConfirmTakeawayAsync(string orderNo)
        {
            string? staffUserId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(staffUserId))
                return Forbid();

            try
            {
                var info = await _billingService.GetOrderBillingInfoAsync(orderNo);

                if (info == null)
                    throw new InvalidOperationException("Order does not exist.");

                if (info.OrderType != OrderType.Takeaway)
                    throw new InvalidOperationException(
                        "Dine-in orders are billed with their table.");

                var bill = await _billingService.CreateTakeawayBillAsync(info.OrderId);

                if (bill.Status == BillStatus.Draft)
                    bill = await _billingService.ConfirmBillAsync(bill.Id, staffUserId);

                TempData["BillingSuccess"] =
                    $"Bill {BillNo(bill.Id)} confirmed.";

                return RedirectToPage(ThisPage, null, new { orderNo = info.OrderNo }, ActionsFragment);
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
            }

            await ReloadAsync(null, orderNo);
            return Page();
        }

        // ---------- POST: counter payment (Cash / Card) ----------

        public async Task<IActionResult> OnPostRecordPaymentAsync(
            int billId,
            PaymentMethod method,
            string? transactionRef,
            int? sessionId,
            string? orderNo)
        {
            string? staffUserId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(staffUserId))
                return Forbid();

            try
            {
                var payment = await _paymentService.RecordCounterPaymentAsync(
                    billId,
                    method,
                    staffUserId,
                    transactionRef);

                TempData["BillingSuccess"] =
                    $"{payment.Method} payment of RM {payment.Amount:F2} received. " +
                    $"Bill {BillNo(billId)} is now Paid.";

                return sessionId.HasValue
                    ? RedirectToPage(ThisPage, null, new { sessionId }, ActionsFragment)
                    : RedirectToPage(ThisPage, null, new { orderNo }, ActionsFragment);
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
            }

            await ReloadAsync(sessionId, orderNo);
            return Page();
        }

        // ---------- Loading ----------

        private async Task ReloadAsync(int? sessionId, string? orderNo)
        {
            SessionId = sessionId;
            OrderNo = orderNo;

            if (sessionId is int id)
            {
                await LoadSessionAsync(id);
            }
            else if (!string.IsNullOrWhiteSpace(orderNo))
            {
                var info = await _billingService.GetOrderBillingInfoAsync(orderNo);

                if (info != null && info.OrderType == OrderType.Takeaway)
                    await LoadTakeawayAsync(info);
            }
        }

        private async Task LoadSessionAsync(int sessionId)
        {
            try
            {
                SessionBill = await _billingService.PreviewSessionBillAsync(sessionId);

                if (SessionBill.ExistingBillId is int billId)
                {
                    CurrentBill = await _billingService.GetBillByIdAsync(billId);
                    Payments = await _paymentService.GetPaymentsByBillIdAsync(billId);
                }

                await LoadStaffNamesAsync();
            }
            catch (ArgumentException ex)
            {
                SessionBill = null;
                ErrorMessage ??= ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                SessionBill = null;
                ErrorMessage ??= ex.Message;
            }
        }

        private async Task LoadTakeawayAsync(OrderBillingInfo info)
        {
            TakeawayOrder = info;

            try
            {
                var payments = new List<Payment>();

                if (info.BillId is int billId)
                {
                    CurrentBill = await _billingService.GetBillByIdAsync(billId);
                    TakeawaySummary = await _billingService.GetBillAsync(info.OrderId);
                    payments.AddRange(await _paymentService.GetPaymentsByBillIdAsync(billId));
                }
                else
                {
                    TakeawaySummary = await _billingService.PreviewBillAsync(info.OrderId);
                }

                // Older (US31) payments recorded against the order itself.
                payments.AddRange(await _paymentService.GetPaymentsByOrderIdAsync(info.OrderId));

                Payments = payments
                    .GroupBy(p => p.Id)
                    .Select(g => g.First())
                    .OrderByDescending(p => p.CreatedAt)
                    .ThenByDescending(p => p.Id)
                    .ToList();

                await LoadStaffNamesAsync();
            }
            catch (ArgumentException ex)
            {
                TakeawaySummary = null;
                ErrorMessage ??= ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                TakeawaySummary = null;
                ErrorMessage ??= ex.Message;
            }
        }

        private async Task LoadStaffNamesAsync()
        {
            ConfirmedByName = await GetUserDisplayNameAsync(CurrentBill?.ConfirmedByUserId);
            LatestPaymentRecordedByName = await GetUserDisplayNameAsync(LatestPayment?.RecordedByUserId);
        }

        private async Task<string?> GetUserDisplayNameAsync(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
                return null;

            return string.IsNullOrWhiteSpace(user.FullName)
                ? user.Email
                : user.FullName;
        }

        private async Task LoadReceiptSettingsAsync()
        {
            var settings = await _reportService.GetRestaurantSettingsAsync();

            SstRegistrationNo = string.IsNullOrWhiteSpace(settings.SstRegistrationNo)
                ? null
                : settings.SstRegistrationNo.Trim();

            ReceiptFooterNote = string.IsNullOrWhiteSpace(settings.ReceiptFooterNote)
                ? null
                : settings.ReceiptFooterNote.Trim();
        }
    }
}
