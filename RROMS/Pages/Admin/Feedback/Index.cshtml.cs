using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin.Feedback;

// US43 View and reply to feedback (Member 5). Admin only (the /Admin folder is protected in Program.cs).
public class Index : PageModel
{
    private readonly IAdminFeedbackService _feedbackService;

    public Index(IAdminFeedbackService feedbackService)
    {
        _feedbackService = feedbackService;
    }

    // ---------- Filters (from the query string, e.g. ?rating=2&status=needs) ----------

    [BindProperty(SupportsGet = true)]
    public int? Rating { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    // ---------- Reply form ----------

    [BindProperty]
    public int FeedbackId { get; set; }

    [BindProperty]
    public string? ReplyText { get; set; }

    // The feedback whose reply failed validation (its reply box stays open with the error).
    public int? ErrorFeedbackId { get; private set; }

    // ---------- Data for the page ----------

    public FeedbackSummary Summary { get; private set; } = FeedbackSummary.Empty;

    public List<RROMS.Models.Feedback> Items { get; private set; } = new();

    public bool HasFilters =>
        Rating.HasValue || !string.IsNullOrEmpty(Status) || !string.IsNullOrWhiteSpace(Q);

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostReplyAsync()
    {
        try
        {
            var feedback = await _feedbackService.ReplyAsync(FeedbackId, ReplyText);

            TempData["SuccessMessage"] = $"Reply sent for order {feedback.Order.OrderNo}.";

            // Back to the same filters, scrolled to the review that was answered.
            return RedirectToPage(null, null, new { Rating, Status, Q, Sort }, $"fb-{FeedbackId}");
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(ReplyText), ex.Message);
            ErrorFeedbackId = FeedbackId;
            await LoadAsync();
            return Page();
        }
    }

    private async Task LoadAsync()
    {
        if (Rating is < 1 or > 5)
        {
            Rating = null;
        }

        if (Status != "needs" && Status != "replied")
        {
            Status = null;
        }

        Summary = await _feedbackService.GetSummaryAsync();
        Items = await _feedbackService.GetFeedbackAsync(new FeedbackFilter(Rating, Status, Q, Sort));
    }

    // Builds a link that keeps the current filters but changes one of them.
    public string FilterUrl(int? rating = -1, string? status = "-", string? sort = "-")
    {
        var values = new Dictionary<string, string?>
        {
            ["rating"] = rating == -1 ? Rating?.ToString() : rating?.ToString(),
            ["status"] = status == "-" ? Status : status,
            ["q"] = Q,
            ["sort"] = sort == "-" ? Sort : sort
        };

        var query = string.Join("&", values
            .Where(v => !string.IsNullOrEmpty(v.Value))
            .Select(v => $"{v.Key}={Uri.EscapeDataString(v.Value!)}"));

        return string.IsNullOrEmpty(query) ? "/Admin/Feedback" : $"/Admin/Feedback?{query}";
    }
}
