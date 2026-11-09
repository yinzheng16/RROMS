using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;
using RROMS.Models;

namespace RROMS.Pages.Customer;

[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class OrderConfirmationModel(ICustomerCartService carts, IOnlinePaymentService online,
    ILogger<OrderConfirmationModel> logger) : PageModel
{
    public OrderReceipt Receipt { get; private set; } = null!;
    public TakeawayPayView PayView { get; private set; } = null!;   // Pay now panel (US33 hook-up)
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var receipt = await carts.ReceiptAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id, cancellationToken);
        if (receipt is null) return NotFound();
        Receipt = receipt;
        PayView = await TakeawayPayView.LoadAsync(online, logger, receipt.Id, receipt.OrderType,
            receipt.Status, receipt.Total, dark: true);
        return Page();
    }

    public async Task<IActionResult> OnGetStatusAsync(int id, CancellationToken cancellationToken)
    {
        var status = await carts.GetOrderStatusAsync(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!, id, cancellationToken);
        if (!status.HasValue) return NotFound();
        return new JsonResult(new
        {
            orderId = id,
            status = status.Value.ToString(),
            label = StatusLabel(status.Value),
            message = StatusMessage(status.Value)
        });
    }

    public static string StatusLabel(OrderStatus status) => status == OrderStatus.ReadyToServe
        ? "Ready to serve" : status.ToString();

    public static string StatusMessage(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Waiting for the restaurant to accept your order.",
        OrderStatus.Preparing => "The kitchen is preparing your order.",
        OrderStatus.ReadyToServe => "Your order is ready.",
        OrderStatus.Served => "Your order has been served.",
        OrderStatus.Completed => "Your order is complete.",
        OrderStatus.Cancelled => "Your order has been cancelled.",
        _ => "Your current order status is shown here."
    };
}
