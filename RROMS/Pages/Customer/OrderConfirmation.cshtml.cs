using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer;

[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class OrderConfirmationModel(ICustomerCartService carts) : PageModel
{
    public OrderReceipt Receipt { get; private set; } = null!;
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var receipt = await carts.ReceiptAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id, cancellationToken);
        if (receipt is null) return NotFound();
        Receipt = receipt;
        return Page();
    }
}
