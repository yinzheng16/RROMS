using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer;

[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(131072)]
public class CartModel(ICustomerCartService carts) : PageModel
{
    [BindProperty] public CartUpdate Input { get; set; } = new();
    public CartView Cart { get; private set; } = CartView.Empty;
    private string CustomerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool Ajax => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    public async Task OnGetAsync(CancellationToken cancellationToken)
        => Cart = await carts.GetAsync(CustomerId, cancellationToken);

    public async Task<IActionResult> OnGetCountAsync(CancellationToken cancellationToken)
        => new JsonResult(new { count = await carts.CountAsync(CustomerId, cancellationToken) });

    public async Task<IActionResult> OnPostSaveAsync(Guid? removeLineId, CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid
            ? await carts.UpdateAsync(CustomerId, Input, removeLineId, cancellationToken)
            : new CartOperation(CartResult.Invalid, "Check your cart details and try again.");
        Cart = await carts.GetAsync(CustomerId, cancellationToken);
        if (Ajax)
        {
            Response.Headers["X-Cart-Result"] = result.Status.ToString();
            Response.Headers["X-Cart-Count"] = Cart.Quantity.ToString();
            Response.Headers["X-Cart-Message"] = Uri.EscapeDataString(result.Message);
            Response.StatusCode = result.Succeeded ? 200 : result.Status == CartResult.Changed ? 409 : 422;
            return new PartialViewResult { ViewName = "_CartContent", ViewData =
                new ViewDataDictionary<CartView>(ViewData, Cart) };
        }
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReviewAsync(CancellationToken cancellationToken)
    {
        var saved = ModelState.IsValid ? await carts.UpdateAsync(CustomerId, Input, null, cancellationToken)
            : new CartOperation(CartResult.Invalid, "Check your quantities and notes before continuing.");
        if (!saved.Succeeded) return await ReviewFailure(saved, cancellationToken);
        Cart = await carts.GetAsync(CustomerId, cancellationToken);
        var quote = await carts.PrepareAsync(CustomerId, Cart.Revision, cancellationToken);
        if (!quote.Succeeded) return await ReviewFailure(new(quote.Status, quote.Message), cancellationToken);
        if (Ajax) return new PartialViewResult { ViewName = "_CheckoutReview",
            ViewData = new ViewDataDictionary<CheckoutQuote>(ViewData, quote) };
        return new PartialViewResult { ViewName = "_CheckoutReviewPage",
            ViewData = new ViewDataDictionary<CheckoutQuote>(ViewData, quote) };
    }

    private async Task<IActionResult> ReviewFailure(CartOperation result, CancellationToken ct)
    {
        if (Ajax)
        {
            Cart = await carts.GetAsync(CustomerId, ct);
            Response.StatusCode = result.Status == CartResult.Changed ? 409 : 422;
            return new JsonResult(new { message = result.Message, count = Cart.Quantity, reload = true });
        }
        TempData["ErrorMessage"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPlaceAsync(string? token, CancellationToken cancellationToken)
    {
        var result = await carts.PlaceAsync(CustomerId, token, cancellationToken);
        if (result.Succeeded)
        {
            string receipt = Url.Page("/Customer/OrderConfirmation", new { id = result.OrderId })!;
            if (Ajax) return new JsonResult(new { redirect = receipt,
                count = await carts.CountAsync(CustomerId, cancellationToken) });
            return LocalRedirect(receipt);
        }
        if (Ajax)
        {
            Response.StatusCode = result.Status == CartResult.Changed ? 409 : 422;
            return new JsonResult(new { message = result.Message, reload = true });
        }
        TempData["ErrorMessage"] = result.Message;
        return RedirectToPage();
    }
}
