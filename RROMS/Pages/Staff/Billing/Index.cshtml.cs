using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Staff.Billing
{
    public class IndexModel : PageModel
    {
        private readonly IBillingService _billingService;

        public IndexModel(IBillingService billingService)
        {
            _billingService = billingService;
        }

        [BindProperty(SupportsGet = true)]
        public string? OrderNo { get; set; }

        public int? FoundOrderId { get; private set; }

        public BillSummary? Bill { get; private set; }

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

                CanGenerateBill =
                    await _billingService.CanGenerateBillAsync(
                        FoundOrderId.Value);

                if (CanGenerateBill)
                {
                    // Search immediately shows calculated amounts.
                    // PreviewBillAsync does NOT save changes.
                    Bill = await _billingService.PreviewBillAsync(
                        FoundOrderId.Value);

                    IsPreview = true;
                }
                else
                {
                    // Display an existing saved bill if available.
                    await TryLoadExistingBillAsync();
                }

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
