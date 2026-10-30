using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer.Orders;

[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(131072)]
public class ReorderModel(ICustomerCartService carts, ILogger<ReorderModel> logger) : PageModel
{
    public HistoryOrderDetails Details { get; private set; } = null!;
    public HistoryReorderPreview? Quote { get; private set; }
    [BindProperty] public List<HistoryReorderChoice> Selections { get; set; } = new();
    [BindProperty] public bool CopyDishNotes { get; set; }
    private string CustomerId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    private async Task<bool> LoadAsync(int id, CancellationToken ct)
    {
        var details = await carts.GetHistoryOrderAsync(CustomerId, id, ct);
        if (details is null) return false;
        Details = details; return true;
    }
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (CustomerId.Length == 0) return Challenge();
        if (!await LoadAsync(id, cancellationToken)) return NotFound();
        Selections = Details.Lines.Select(line => new HistoryReorderChoice
        { OrderItemId = line.Id, Selected = line.CanReorder, Quantity = Math.Clamp(line.Quantity, 1, 100) }).ToList();
        return Page();
    }
    // Razor Pages validates antiforgery for every POST; services validate actual ownership.
    public async Task<IActionResult> OnPostReviewAsync(int id, CancellationToken cancellationToken)
    {
        if (CustomerId.Length == 0) return Challenge();
        if (!await LoadAsync(id, cancellationToken)) return NotFound();
        if (!ModelState.IsValid) return Page();
        Quote = await carts.PrepareReorderAsync(CustomerId, id, Selections, CopyDishNotes, cancellationToken);
        if (Quote.Status == CartResult.Forbidden) return Forbid();
        if (!Quote.Ready) ModelState.AddModelError(string.Empty, Quote.Message);
        return Page();
    }
    public async Task<IActionResult> OnPostEditAsync(int id, CancellationToken cancellationToken)
    {
        if (CustomerId.Length == 0) return Challenge();
        if (!await LoadAsync(id, cancellationToken)) return NotFound();
        return Page();
    }
    public async Task<IActionResult> OnPostConfirmAsync(int id, string? token, CancellationToken cancellationToken)
    {
        if (CustomerId.Length == 0) return Challenge();
        CartOperation result;
        try { result = await carts.ConfirmReorderAsync(CustomerId, token, cancellationToken); }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(error, "Could not confirm reorder for customer {CustomerId}", CustomerId);
            TempData["ErrorMessage"] = "We could not confirm the result. Check your cart before trying to add these dishes again.";
            return RedirectToPage("/Customer/Cart");
        }
        if (result.Status == CartResult.Forbidden) return Forbid();
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        // A changed cart may already contain a successful reorder from another tab.
        // Always show the actual cart instead of inviting an automatic retry.
        return RedirectToPage("/Customer/Cart");
    }
}
