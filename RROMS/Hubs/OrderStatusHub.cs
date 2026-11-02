using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RROMS.Services.Interfaces;

namespace RROMS.Hubs;

// Owner: Member 3 (US27). Live order status for the signed-in customer's own order.
// The hub only sends a short "status changed" signal; the page then reads the full
// status from OrderConfirmation?handler=Status, which re-checks the owner.
[Authorize(Policy = "CustomerOnly")]
public sealed class OrderStatusHub(ICustomerCartService carts) : Hub
{
    public const string Path = "/hubs/order-status";
    public const string StatusChangedEvent = "OrderStatusChanged";

    public static string GroupFor(int orderId) => $"order-{orderId}";

    // Joins the order's group only if the order belongs to the signed-in customer.
    public async Task<bool> WatchOrder(int orderId)
    {
        string? userId = Context.UserIdentifier;
        if (orderId <= 0 || string.IsNullOrEmpty(userId)) return false;
        var status = await carts.GetOrderStatusAsync(userId, orderId, Context.ConnectionAborted);
        if (!status.HasValue) return false;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(orderId), Context.ConnectionAborted);
        return true;
    }
}
