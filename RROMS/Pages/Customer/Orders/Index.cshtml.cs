using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer.Orders;

[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(ICustomerCartService carts) : PageModel
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
        }
        return Page();
    }

    public async Task<IActionResult> OnGetDetailsAsync(int id, CancellationToken cancellationToken)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();
        var details = await carts.GetHistoryOrderAsync(userId, id, cancellationToken);
        return details is null ? NotFound() : Partial("_OrderHistoryDetails", details);
    }
}
