using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Staff.Billing
{
    // Owner: Member 4 (US32 - Generate Bill, counter payment)
    //
    // Staff billing list (today's work). Staff pick a table (dine-in) or a
    // takeaway order from the list instead of typing an order number, then
    // the bill opens on its own page (Bill.cshtml) for confirming, payment
    // and printing. Shown: everything not paid yet, plus bills paid today.
    // Older bills open by order or bill number; history by date is US40.
    // Order status (kitchen) and bill / payment status stay separate.
    public class IndexModel : PageModel
    {
        private readonly IBillingService _billingService;

        public IndexModel(IBillingService billingService)
        {
            _billingService = billingService;
        }

        // Older links (/Staff/Billing?sessionId=.. or ?orderNo=..) still work:
        // they are sent on to the bill page.
        [BindProperty(SupportsGet = true)]
        public int? SessionId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? OrderNo { get; set; }

        // "dinein" (default) or "takeaway": which tab is open.
        [BindProperty(SupportsGet = true)]
        public string? Tab { get; set; }

        public IReadOnlyList<SessionBillingRow> DineInRows { get; private set; }
            = Array.Empty<SessionBillingRow>();

        public IReadOnlyList<TakeawayBillingRow> TakeawayRows { get; private set; }
            = Array.Empty<TakeawayBillingRow>();

        public bool ShowTakeaway =>
            string.Equals(Tab, "takeaway", StringComparison.OrdinalIgnoreCase);

        public async Task<IActionResult> OnGetAsync()
        {
            if (SessionId is int sessionId)
                return RedirectToPage("/Staff/Billing/Bill", new { sessionId });

            if (!string.IsNullOrWhiteSpace(OrderNo))
                return RedirectToPage("/Staff/Billing/Bill", new { orderNo = OrderNo.Trim() });

            DineInRows = await _billingService.GetSessionBillingRowsAsync();
            TakeawayRows = await _billingService.GetTakeawayBillingRowsAsync();

            return Page();
        }

        // ---------- Display helpers (shared with Bill.cshtml) ----------

        // Bill number shown to staff and printed: YB-000012.
        public static string FormatBillNo(int billId) => $"YB-{billId:D6}";

        // Filter key used by the list filters.
        public static string BillStateKey(BillStatus? status) => status switch
        {
            BillStatus.Confirmed => "awaiting",
            BillStatus.Paid => "paid",
            _ => "nobill" // no bill yet, or an unconfirmed draft
        };

        public static string BillStateLabel(BillStatus? status) => status switch
        {
            BillStatus.Confirmed => "Awaiting payment",
            BillStatus.Paid => "Paid",
            BillStatus.Draft => "Draft",
            _ => "No bill"
        };

        public static string OrderStatusLabel(OrderStatus status) =>
            status == OrderStatus.ReadyToServe ? "Ready" : status.ToString();

        // True for a time before today (local time, like the rest of billing).
        public static bool IsBeforeToday(DateTime value) => value < DateTime.Today;
    }
}
