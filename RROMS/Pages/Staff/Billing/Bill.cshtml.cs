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

        // US35: the same PDF receipt the customer downloads.
        private readonly IReceiptService _receiptService;

        // US33: online payments (Stripe) of the bill shown on this page.
        private readonly IOnlinePaymentService _onlinePayments;

        public BillModel(
            IBillingService billingService,
            IPaymentService paymentService,
            IReportService reportService,
            UserManager<ApplicationUser> userManager,
            IReceiptService receiptService,
            IOnlinePaymentService onlinePayments)
        {
            _billingService = billingService;
            _paymentService = paymentService;
            _reportService = reportService;
            _userManager = userManager;
            _receiptService = receiptService;
            _onlinePayments = onlinePayments;
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

        // US33: online payment running for this bill, and whether a takeaway
        // bill was confirmed by its customer when choosing to pay online.
        public OnlineBillInfo? OnlineInfo { get; private set; }
        public bool OnlinePaymentInProgress => OnlineInfo?.InProgress == true;
        public bool ConfirmedForOnlinePayment => OnlineInfo?.ConfirmedByCustomer == true;
        public bool OnlinePaymentAvailable => _onlinePayments.IsOnlinePaymentAvailable;

        // US33: QR code (PNG data URI) of the open Stripe page, when staff
        // started the online payment at the counter.
        public string? QrImageDataUri { get; private set; }

        // US33: short fingerprint of the bill / payment state. The page asks
        // OnGetStatusAsync every 5 seconds and reloads when it changes (e.g.
        // the customer paid online while staff had this page open).
        public string? StatusStamp { get; private set; }
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

        // One line of the bill. All ordering rounds are combined: the same
        // dish at the same unit price is added up into one line. Amounts are
        // the saved order item amounts; nothing is recalculated.
        public sealed record ReceiptLine(
            string Name,
            int Quantity,
            decimal UnitPrice,
            decimal LineTotal);

        public IReadOnlyList<ReceiptLine> ReceiptLines
        {
            get
            {
                IEnumerable<BillItemSummary> items = SessionBill != null
                    ? SessionBill.Orders.SelectMany(o => o.Items)
                    : TakeawaySummary?.Items ?? Enumerable.Empty<BillItemSummary>();

                // GroupBy keeps the order in which dishes first appear.
                return items
                    .GroupBy(i => (
                        Name: string.IsNullOrWhiteSpace(i.MenuItemName)
                            ? $"Item #{i.MenuItemId}"
                            : i.MenuItemName,
                        i.UnitPrice))
                    .Select(g => new ReceiptLine(
                        g.Key.Name,
                        g.Sum(i => i.Quantity),
                        g.Key.UnitPrice,
                        g.Sum(i => i.LineTotal)))
                    .ToList();
            }
        }

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

        // ---------- GET: receipt PDF (US35) ----------

        // /Staff/Billing/Bill?handler=Receipt&paymentId=12
        // Opens the customer's receipt PDF in the browser (inline) so staff
        // can preview it and print it from the PDF viewer.
        // Not found or not Paid: 404.
        public async Task<IActionResult> OnGetReceiptAsync(int paymentId)
        {
            var receipt = await _receiptService.GetReceiptAsync(paymentId);

            if (receipt == null)
                return NotFound();

            byte[] pdf = _receiptService.CreatePdf(receipt);

            // No download name -> the browser shows it instead of saving it.
            Response.Headers.ContentDisposition =
                $"inline; filename=\"YORU-receipt-{receipt.ReceiptNo}.pdf\"";

            return File(pdf, "application/pdf");
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

            // US33: Online = show a Stripe QR code for the customer's phone.
            if (method == PaymentMethod.Online)
            {
                try
                {
                    await _onlinePayments.StartStaffQrCheckoutAsync(billId, staffUserId, QrCheckoutUrls());

                    return sessionId.HasValue
                        ? RedirectToPage(ThisPage, null, new { sessionId }, ActionsFragment)
                        : RedirectToPage(ThisPage, null, new { orderNo }, ActionsFragment);
                }
                catch (InvalidOperationException ex)
                {
                    ErrorMessage = ex.Message;
                }
                catch (Exception ex) when (ex is Stripe.StripeException or HttpRequestException)
                {
                    ErrorMessage = "Could not open a Stripe payment page. Nothing was charged. " +
                                   "Please try again, or take Cash or Card.";
                }

                await ReloadAsync(sessionId, orderNo);
                return Page();
            }

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

        // ---------- POST: cancel the customer's online payment (US33) ----------
        // The customer pays at the counter instead. Stripe is checked first.

        public async Task<IActionResult> OnPostCancelOnlineAsync(
            int billId,
            int? sessionId,
            string? orderNo)
        {
            if (string.IsNullOrEmpty(_userManager.GetUserId(User)))
                return Forbid();

            try
            {
                var state = await _onlinePayments.CancelForCounterPaymentAsync(billId);

                TempData["BillingSuccess"] = state switch
                {
                    OnlinePaymentState.Paid =>
                        $"The customer had already paid online. Bill {BillNo(billId)} is now Paid.",
                    OnlinePaymentState.StillPending =>
                        "Stripe has not finished this payment yet. Check again in a moment.",
                    _ =>
                        "Online payment cancelled. Nothing was charged. You can record the counter payment now."
                };

                return sessionId.HasValue
                    ? RedirectToPage(ThisPage, new { sessionId })
                    : RedirectToPage(ThisPage, new { orderNo });
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (Exception ex) when (ex is Stripe.StripeException or HttpRequestException)
            {
                ErrorMessage = "Could not reach Stripe. Nothing was changed. Please try again.";
            }

            await ReloadAsync(sessionId, orderNo);
            return Page();
        }

        // ---------- GET: status check for the open page (US33) ----------
        // /Staff/Billing/Bill?handler=Status&orderNo=..  or  &sessionId=..
        // Checks unfinished online payments with Stripe, then returns the stamp.

        public async Task<IActionResult> OnGetStatusAsync(int? sessionId, string? orderNo)
        {
            string stamp = await GetStatusStampAsync(sessionId, orderNo, checkStripe: true);
            return new JsonResult(new { stamp });
        }

        private async Task<string> GetStatusStampAsync(
            int? sessionId, string? orderNo, bool checkStripe)
        {
            try
            {
                int? billId = null;

                if (sessionId is int sid)
                    billId = (await _billingService.GetBillForSessionAsync(sid))?.Id;
                else if (!string.IsNullOrWhiteSpace(orderNo))
                    billId = (await _billingService.GetOrderBillingInfoAsync(orderNo))?.BillId;

                if (billId is not int id)
                    return "none";

                if (checkStripe)
                    await _onlinePayments.ReconcileBillAsync(id);

                var bill = await _billingService.GetBillByIdAsync(id);
                var online = await _onlinePayments.GetOnlineBillInfoAsync(id);

                return $"{id}|{bill?.Status}|{online.PendingPaymentId}";
            }
            catch (ArgumentException)
            {
                return "none";
            }
            catch (InvalidOperationException)
            {
                return "none";
            }
        }

        // ---------- Loading ----------

        // US33: ask Stripe about unfinished online payments of this bill
        // (the customer may have paid and closed the browser), then read
        // what staff need to see. A Stripe problem never breaks the page.
        private async Task SyncOnlinePaymentAsync(int billId)
        {
            await _onlinePayments.ReconcileBillAsync(billId);
            OnlineInfo = await _onlinePayments.GetOnlineBillInfoAsync(billId);

            if (OnlineInfo.InProgress && OnlineInfo.StartedByStaff &&
                await _onlinePayments.GetOpenCheckoutUrlAsync(billId) is string stripeUrl)
            {
                QrImageDataUri = MakeQrDataUri(stripeUrl);
            }
        }

        // QR code as a PNG data URI (QRCoder), so no image file or CDN is needed.
        private static string MakeQrDataUri(string text)
        {
            using var generator = new QRCoder.QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
            byte[] png = new QRCoder.PngByteQRCode(data).GetGraphic(6);
            return "data:image/png;base64," + Convert.ToBase64String(png);
        }

        // Where Stripe sends the customer's PHONE after a QR payment: a public
        // page (no login). Stripe fills in the session id.
        private OnlineCheckoutUrls QrCheckoutUrls()
        {
            string thanks = Url.Page("/Payments/Thanks", null, null, Request.Scheme)!;
            return new OnlineCheckoutUrls(
                thanks + "?session_id=" + OnlineCheckoutUrls.SessionIdToken,
                thanks + "?cancelled=true&payment=" + OnlineCheckoutUrls.PaymentIdToken);
        }

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
                    await SyncOnlinePaymentAsync(billId);
                    CurrentBill = await _billingService.GetBillByIdAsync(billId);
                    Payments = await _paymentService.GetPaymentsByBillIdAsync(billId);
                }

                await LoadStaffNamesAsync();
                StatusStamp = await GetStatusStampAsync(sessionId, null, checkStripe: false);
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
                    await SyncOnlinePaymentAsync(billId);
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
                StatusStamp = await GetStatusStampAsync(null, info.OrderNo, checkStripe: false);
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
