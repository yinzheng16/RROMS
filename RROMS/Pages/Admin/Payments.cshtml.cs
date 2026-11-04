// Owner: Member 4 — US40 Administrative payment transaction monitoring.
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin;

// The /Admin folder already requires AdminOnly (Program.cs);
// the attribute repeats it so the page stays protected if moved.
[Authorize(Policy = "AdminOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PaymentsModel(IAdminPaymentService adminPayments, IReceiptService receipts) : PageModel
{
    private readonly IAdminPaymentService _adminPayments = adminPayments;
    private readonly IReceiptService _receipts = receipts;

    // ---------- Query string (GET form, so the URL can be bookmarked) ----------

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    // today | 7d | month | custom. Empty = today.
    [BindProperty(SupportsGet = true)]
    public string? Range { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    // Read as text, so a wrong value (?status=abc) just means "all".
    [BindProperty(SupportsGet = true, Name = "status")]
    public string? StatusText { get; set; }

    [BindProperty(SupportsGet = true, Name = "method")]
    public string? MethodText { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNo { get; set; } = 1;

    // ---------- Page data ----------

    public AdminPaymentMonitor Data { get; private set; } = new();

    public PaymentStatus? Status => ParseEnum<PaymentStatus>(StatusText);
    public PaymentMethod? Method => ParseEnum<PaymentMethod>(MethodText);

    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);

    // Latest date the date pickers allow (today, server time).
    public string TodayIso => Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public bool HasError => !ModelState.IsValid;

    public async Task<IActionResult> OnGetAsync()
    {
        // A date that cannot be read (e.g. ?from=abc) is a model binding error.
        if (!ModelState.IsValid)
        {
            ModelState.Clear();
            ModelState.AddModelError(string.Empty, "Please enter valid dates.");
            From = null;
            To = null;
            Range = "today";
        }

        ApplyRange();

        // Server-side check (the date pickers check this in the browser too).
        if (From is DateOnly f && To is DateOnly t && f > t)
        {
            ModelState.AddModelError(nameof(To), "The To date cannot be before the From date.");
            return Page();   // keep the entered dates, show no list
        }

        Data = await _adminPayments.GetPaymentsAsync(new AdminPaymentFilter
        {
            Search = Search,
            From = From,
            To = To,
            Status = Status,
            Method = Method,
            Page = PageNo
        });

        return Page();
    }

    // /Admin/Payments?handler=Receipt&id=12               -> shown in the browser (preview pop-up)
    // /Admin/Payments?handler=Receipt&id=12&download=true -> saved as a file
    // US35 receipt PDF of a Paid payment. Not found or not Paid: 404.
    public async Task<IActionResult> OnGetReceiptAsync(int id, bool download = false)
    {
        var receipt = await _receipts.GetReceiptAsync(id);
        if (receipt == null) return NotFound();

        byte[] pdf = _receipts.CreatePdf(receipt);
        string fileName = $"YORU-receipt-{receipt.ReceiptNo}.pdf";

        if (download)
            return File(pdf, "application/pdf", fileName);

        // No download name -> the browser shows it instead of saving it.
        Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
        return File(pdf, "application/pdf");
    }

    // Quick buttons set the dates; typed dates mean "custom".
    private void ApplyRange()
    {
        string range = (Range ?? "").Trim().ToLowerInvariant();

        if (range == "" && (From.HasValue || To.HasValue))
            range = "custom";

        switch (range)
        {
            case "7d":
                From = Today.AddDays(-6);
                To = Today;
                break;
            case "month":
                From = new DateOnly(Today.Year, Today.Month, 1);
                To = Today;
                break;
            case "custom":
                // Typed dates that equal a quick button light that button up.
                if (From == Today && To == Today)
                    range = "today";
                else if (From == Today.AddDays(-6) && To == Today)
                    range = "7d";
                else if (From == new DateOnly(Today.Year, Today.Month, 1) && To == Today)
                    range = "month";
                break;
            default:
                range = "today";
                From = Today;
                To = Today;
                break;
        }

        Range = range;
    }

    private static T? ParseEnum<T>(string? text) where T : struct, Enum =>
        Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : null;

    // Link that keeps the current filters and changes some of them.
    // Changing a filter always goes back to page 1.
    public string FilterUrl(
        string? status = null, bool keepStatus = true,
        string? range = null,
        int? page = null)
    {
        string useRange = range ?? Range ?? "today";
        bool custom = useRange == "custom";

        return Url.Page("/Admin/Payments", new
        {
            q = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            range = useRange == "today" ? null : useRange,
            from = custom ? From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            to = custom ? To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            status = keepStatus ? Status?.ToString() : status,
            method = Method?.ToString(),
            p = page is > 1 ? page : null
        })!;
    }
}
