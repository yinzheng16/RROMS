using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Staff.Orders;
[Authorize(Policy = "StaffOrAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(IOrderService orders, ILogger<IndexModel> logger, IProfileService profiles) : PageModel
{
    public StaffWorkflowBoard Board { get; private set; } = null!;
    public StaffWorkflowDetails? Details { get; private set; }
    public async Task OnGetAsync(string? status, string? search, string? dining, string? sort,
        int pageNumber = 1, int? orderId = null, CancellationToken cancellationToken = default)
    {
        Board = await orders.GetStaffWorkflowBoardAsync(status, search, dining, sort, pageNumber, cancellationToken);
        if (orderId.HasValue) Details = await LoadDetailsAsync(orderId.Value, cancellationToken);
    }
    public async Task<IActionResult> OnGetBoardAsync(string? status, string? search, string? dining, string? sort,
        int pageNumber = 1, CancellationToken cancellationToken = default)
        => Partial("_WorkflowBoard", await orders.GetStaffWorkflowBoardAsync(status, search, dining, sort, pageNumber, cancellationToken));
    public async Task<IActionResult> OnGetSignalAsync(CancellationToken cancellationToken = default)
        => new JsonResult(await orders.GetStaffWorkflowSignalAsync(cancellationToken));
    public async Task<IActionResult> OnGetDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        var details = await LoadDetailsAsync(id, cancellationToken);
        return details is null ? NotFound() : Partial("_WorkflowDetails", details);
    }
    private async Task<StaffWorkflowDetails?> LoadDetailsAsync(int id, CancellationToken ct)
    {
        var details = await orders.GetStaffWorkflowDetailsAsync(id, ct);
        if (details is null || string.IsNullOrEmpty(details.Order.CustomerId)) return details;
        // Always load the order's customer, never a customer ID supplied by the browser.
        string? preferences = await profiles.GetDiningSummaryAsync(details.Order.CustomerId);
        return details with { Order = details.Order with { DiningPreferences = preferences } };
    }
    public async Task<IActionResult> OnPostChangeStatusAsync(int id, OrderStatus? expectedStatus, OrderStatus? targetStatus,
        string? reason, string? status, string? search, string? dining, string? sort,
        int pageNumber = 1, CancellationToken cancellationToken = default)
    {
        bool ajax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        StaffStatusResult result;
        if (!ModelState.IsValid || !expectedStatus.HasValue || !targetStatus.HasValue)
            result = new(StaffStatusOutcome.Invalid, "Choose a valid status change and try again.");
        else
        {
            try { result = await orders.ChangeStaffOrderStatusAsync(id, expectedStatus.Value, targetStatus.Value, reason,
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", cancellationToken); }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Could not update staff order {OrderId}", id);
                const string message = "We could not confirm the result. Refresh this order before trying again.";
                if (ajax) return StatusCode(503, new { success = false, message, refresh = true });
                TempData["ErrorMessage"] = message;
                return RedirectToPage(new { status, search, dining, sort, pageNumber, orderId = id });
            }
        }
        if (ajax)
            return new JsonResult(new { success = result.Outcome == StaffStatusOutcome.Success, message = result.Message, refresh = true })
            { StatusCode = result.Outcome switch { StaffStatusOutcome.Success => 200, StaffStatusOutcome.Invalid => 400,
                StaffStatusOutcome.Forbidden => 403, StaffStatusOutcome.NotFound => 404, _ => 409 } };
        TempData[result.Outcome == StaffStatusOutcome.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return RedirectToPage(new { status, search, dining, sort, pageNumber, orderId = id });
    }
    // Preserve US25 callers while recording acceptance through the US26 service.
    public async Task<IActionResult> OnPostAcceptAsync(int id, CancellationToken cancellationToken = default)
        => await OnPostChangeStatusAsync(id, OrderStatus.Pending, OrderStatus.Preparing, null, null, null, null, null, 1, cancellationToken);
}
