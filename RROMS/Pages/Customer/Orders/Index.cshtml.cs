using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer.Orders;

[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(ICustomerCartService carts, IOnlinePaymentService online,
    ILogger<IndexModel> logger) : PageModel
{
    public CustomerOrderHistory History { get; private set; } = null!;
    public HistoryOrderDetails? Details { get; private set; }
    public async Task<IActionResult> OnGetAsync(string? search, string? status, string? dining,
        DateTime? from, DateTime? to, string? sort, int pageNumber = 1, int? orderId = null, bool showAll = false, string? statusGroup = null,
        CancellationToken cancellationToken = default)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();
        // Group tabs are kept separate from the exact-status dropdown.
        // An exact choice overrides the group; All statuses keeps the active tab.
        if ((string.IsNullOrWhiteSpace(status) || status == "All") && statusGroup is "InProgress" or "Finished")
            status = statusGroup;
        History = await carts.GetOrderHistoryAsync(userId, new HistoryFilter
        { Search = search, Status = status, Dining = dining, From = from, To = to, Sort = sort, PageNumber = pageNumber, ShowAll = showAll }, cancellationToken);
        if (History.Forbidden) return Forbid();
        if (orderId.HasValue)
        {
            Details = await carts.GetHistoryOrderAsync(userId, orderId.Value, cancellationToken);
            if (Details is null) return NotFound();
            ViewData[TakeawayPayView.ViewDataKey] = await PayViewAsync(Details);
        }
        return Page();
    }

    public async Task<IActionResult> OnGetDetailsAsync(int id, CancellationToken cancellationToken)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();
        var details = await carts.GetHistoryOrderAsync(userId, id, cancellationToken);
        if (details is null) return NotFound();
        var result = Partial("_OrderHistoryDetails", details);
        result.ViewData[TakeawayPayView.ViewDataKey] = await PayViewAsync(details);
        return result;
    }

    // Pay now panel in the order details (US33 hook-up, Member 4's payment status).
    private Task<TakeawayPayView> PayViewAsync(HistoryOrderDetails details)
        => TakeawayPayView.LoadAsync(online, logger, details.Id, details.Dining, details.Status, details.Total);
}
