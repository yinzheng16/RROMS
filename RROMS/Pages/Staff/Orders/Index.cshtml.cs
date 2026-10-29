using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Staff.Orders;

// Owner: Member 3 / US25. Pages call services only.
[Authorize(Policy = "StaffOrAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(IOrderService orders, ILogger<IndexModel> logger, IProfileService profiles) : PageModel
{
    public StaffOrderQueue Queue { get; private set; } = null!;
    public StaffOrderDetails? Details { get; private set; }

    public async Task OnGetAsync(string? search, string? dining, int pageNumber = 1,
        int? orderId = null, string? sort = null, CancellationToken cancellationToken = default, bool showAll = false)
    {
        Queue = await orders.GetStaffOrderQueueAsync(search, dining, pageNumber, cancellationToken, sort, showAll);
        ViewData["QueueSort"] = Queue.Sort;
        ViewData["QueueShowAll"] = Queue.ShowAll;
        ViewData["QueueSearch"] = Queue.Search;
        ViewData["QueueDining"] = Queue.Dining;
        ViewData["QueuePage"] = Queue.PageNumber;
        if (orderId.HasValue) Details = await LoadDetailsAsync(orderId.Value, cancellationToken);
        if (orderId.HasValue && Details is null) TempData["InfoMessage"] = "That order is no longer available.";
    }

    public async Task<IActionResult> OnGetQueueAsync(string? search, string? dining,
        int pageNumber = 1, string? sort = null, CancellationToken cancellationToken = default, bool showAll = false)
        => Partial("_Queue", await orders.GetStaffOrderQueueAsync(search, dining, pageNumber, cancellationToken, sort, showAll));

    public async Task<IActionResult> OnGetSignalAsync(CancellationToken cancellationToken = default)
        => new JsonResult(await orders.GetStaffQueueSignalAsync(cancellationToken));

    public async Task<IActionResult> OnGetDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        var details = await LoadDetailsAsync(id, cancellationToken);
        return details is null ? NotFound() : Partial("_OrderDetails", details);
    }

    private async Task<StaffOrderDetails?> LoadDetailsAsync(int id, CancellationToken ct)
    {
        var details = await orders.GetStaffOrderDetailsAsync(id, ct);
        if (details is null || string.IsNullOrEmpty(details.CustomerId)) return details;

        // The page policy authorizes Staff/Admin. Read the ORDER'S customer,
        // never the logged-in staff member and never a customer ID from the URL.
        string? summary = await profiles.GetDiningSummaryAsync(details.CustomerId);
        return details with { DiningPreferences = summary };
    }

    public async Task<IActionResult> OnPostAcceptAsync(int id, string? search, string? dining,
        int pageNumber = 1, string? sort = null, CancellationToken cancellationToken = default, bool showAll = false)
    {
        sort = StaffQueueRules.Sort(sort);
        bool ajax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        StaffAcceptResult result;
        try
        {
            result = await orders.AcceptStaffOrderAsync(id, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Could not accept staff order {OrderId}", id);
            const string message = "We could not confirm the result. Update the queue and check this order before trying again.";
            if (ajax) return StatusCode(503, new { success = false, message, refresh = true });
            TempData["ErrorMessage"] = message;
            return RedirectToPage(new { search, dining, pageNumber, sort, showAll, orderId = id });
        }
        if (ajax)
        {
            int status = result.Status switch
            {
                StaffAcceptStatus.Success => 200,
                StaffAcceptStatus.Forbidden => 403,
                StaffAcceptStatus.NotFound => 404,
                _ => 409
            };
            return new JsonResult(new
            {
                success = result.Status == StaffAcceptStatus.Success,
                message = result.Message,
                refresh = true
            })
            { StatusCode = status };
        }
        TempData[result.Status == StaffAcceptStatus.Success ? "SuccessMessage" : result.Status == StaffAcceptStatus.Forbidden ? "ErrorMessage" : "InfoMessage"] = result.Message;
        return RedirectToPage(new { search, dining, pageNumber, sort, showAll });
    }
}
