using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;
using RROMS.Models;

namespace RROMS.Pages.Staff.Billing
{
    public class IndexModel : PageModel
    {
        private readonly IBillingService _billingService;
        private readonly IPaymentService _paymentService;

        public IndexModel(
            IBillingService billingService,
            IPaymentService paymentService)
        {
            _billingService = billingService;
            _paymentService = paymentService;
        }

        [BindProperty(SupportsGet = true)]
        public string? OrderNo { get; set; }

        public int? FoundOrderId { get; private set; }

        public BillSummary? Bill { get; private set; }

        public IReadOnlyList<Payment> Payments { get; private set; }
            = Array.Empty<Payment>();

        public Payment? LatestPayment =>
            Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault();

        public string? ErrorMessage { get; private set; }

        public string? SuccessMessage { get; private set; }

        public bool OrderFound => FoundOrderId.HasValue;

        public bool CanGenerateBill { get; private set; }

        // True when displaying a preview instead of a saved bill.
        public bool IsPreview { get; private set; }

        public async Task OnGetAsync()
        {
            if (string.IsNullOrWhiteSpace(OrderNo))
                return;

            await LoadOrderAsync();
        }

        public async Task<IActionResult> OnPostGenerateAsync(
            string orderNo)
        {
            OrderNo = orderNo;

            if (string.IsNullOrWhiteSpace(OrderNo))
            {
                ErrorMessage = "Please enter an order number.";
                return Page();
            }

            try
            {
                FoundOrderId =
                    await _billingService.FindOrderIdByOrderNoAsync(
                        OrderNo);

                if (!FoundOrderId.HasValue)
                {
                    ErrorMessage = "Order does not exist.";
                    return Page();
                }

                CanGenerateBill =
                    await _billingService.CanGenerateBillAsync(
                        FoundOrderId.Value);

                if (!CanGenerateBill)
                {
                    ErrorMessage =
                        "This order is not eligible for bill generation.";

                    await TryLoadExistingBillAsync();
                    return Page();
                }

                // Confirm and save the bill.
                await _billingService.GenerateBillAsync(
                    FoundOrderId.Value);

                TempData["BillingSuccess"] =
                    "Bill confirmed and saved successfully.";

                // Redirect to avoid resubmission on refresh.
                return RedirectToPage(new { OrderNo });
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
            }

            // On failure, reload the current order safely.
            await LoadOrderAsync();

            return Page();
        }

        // Record a new pending payment for a confirmed bill.
        public async Task<IActionResult> OnPostRecordPaymentAsync(
            string orderNo,
            PaymentMethod method)
        {
            OrderNo = orderNo;

            try
            {
                if (string.IsNullOrWhiteSpace(orderNo))
                    throw new ArgumentException(
                        "Please enter an order number.");

                if (!Enum.IsDefined(typeof(PaymentMethod), method))
                    throw new ArgumentException(
                        "Invalid payment method.");

                // Online payments are handled by US33.
                if (method == PaymentMethod.Online)
                    throw new InvalidOperationException(
                        "Online payments must be processed through online checkout.");

                var orderId =
                    await _billingService.FindOrderIdByOrderNoAsync(
                        orderNo);

                if (!orderId.HasValue)
                    throw new InvalidOperationException(
                        "Order does not exist.");

                var bill = await _billingService.GetBillAsync(
                    orderId.Value);

                await _paymentService.RecordPaymentAsync(
                    orderId.Value,
                    bill.TotalAmount,
                    method);

                TempData["BillingSuccess"] =
                    "Payment recorded as Pending.";

                return RedirectToPage(new { OrderNo });
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
            }

            await LoadOrderAsync();
            return Page();
        }

        // Confirm an existing pending payment.
        public async Task<IActionResult> OnPostConfirmPaymentAsync(
            string orderNo,
            int paymentId)
        {
            OrderNo = orderNo;

            try
            {
                var payment =
                    await _paymentService.GetPaymentByIdAsync(
                        paymentId);

                if (payment == null)
                    throw new InvalidOperationException(
                        "Payment does not exist.");

                var orderId =
                    await _billingService.FindOrderIdByOrderNoAsync(
                        orderNo);

                if (!orderId.HasValue ||
                    payment.OrderId != orderId.Value)
                    throw new InvalidOperationException(
                        "Payment does not belong to this order.");

                if (payment.Method == PaymentMethod.Online)
                    throw new InvalidOperationException(
                        "Online payment confirmation must be handled by the payment gateway.");

                await _paymentService.ConfirmPaymentAsync(
                    paymentId);

                TempData["BillingSuccess"] =
                    "Payment confirmed successfully.";

                return RedirectToPage(new { OrderNo });
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
            }

            await LoadOrderAsync();
            return Page();
        }

        // Mark an existing pending payment as failed.
        public async Task<IActionResult> OnPostMarkPaymentFailedAsync(
            string orderNo,
            int paymentId)
        {
            OrderNo = orderNo;

            try
            {
                var payment =
                    await _paymentService.GetPaymentByIdAsync(
                        paymentId);

                if (payment == null)
                    throw new InvalidOperationException(
                        "Payment does not exist.");

                var orderId =
                    await _billingService.FindOrderIdByOrderNoAsync(
                        orderNo);

                if (!orderId.HasValue ||
                    payment.OrderId != orderId.Value)
                    throw new InvalidOperationException(
                        "Payment does not belong to this order.");

                if (payment.Method == PaymentMethod.Online)
                    throw new InvalidOperationException(
                        "Online payment failure must be handled by the payment gateway.");

                await _paymentService.MarkPaymentFailedAsync(
                    paymentId);

                TempData["BillingSuccess"] =
                    "Payment marked as Failed.";

                return RedirectToPage(new { OrderNo });
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
            }

            await LoadOrderAsync();
            return Page();
        }

        private async Task LoadOrderAsync()
        {
            try
            {
                FoundOrderId =
                    await _billingService.FindOrderIdByOrderNoAsync(
                        OrderNo!);

                if (!FoundOrderId.HasValue)
                {
                    ErrorMessage = "Order does not exist.";
                    return;
                }

                // Load the saved bill first, if one exists.
                await TryLoadExistingBillAsync();

                if (Bill != null)
                {
                    // A saved bill is shown as Bill Details.
                    IsPreview = false;
                    CanGenerateBill = false;
                }
                else
                {
                    // No saved bill: check whether a preview can be generated.
                    CanGenerateBill =
                        await _billingService.CanGenerateBillAsync(
                            FoundOrderId.Value);

                    if (CanGenerateBill)
                    {
                        Bill = await _billingService.PreviewBillAsync(
                            FoundOrderId.Value);

                        IsPreview = true;
                    }
                }

                Payments = await _paymentService.GetPaymentsByOrderIdAsync(
                    FoundOrderId.Value);

                if (TempData["BillingSuccess"] is string message)
                {
                    SuccessMessage = message;
                }
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
            }
        }

        private async Task TryLoadExistingBillAsync()
        {
            if (!FoundOrderId.HasValue)
                return;

            Payments = await _paymentService.GetPaymentsByOrderIdAsync(
                FoundOrderId.Value);

            try
            {
                Bill = await _billingService.GetBillAsync(
                    FoundOrderId.Value);

                IsPreview = false;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage ??= ex.Message;
            }
        }
    }
}
