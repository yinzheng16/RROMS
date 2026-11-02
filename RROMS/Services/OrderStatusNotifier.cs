using Microsoft.AspNetCore.SignalR;
using RROMS.Hubs;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

// Owner: Member 3 (US27). Sends only the order id and status name; no prices, notes or names.
public sealed class OrderStatusNotifier(IHubContext<OrderStatusHub> hub, ILogger<OrderStatusNotifier> logger)
    : IOrderStatusNotifier
{
    public async Task OrderStatusChangedAsync(int orderId, OrderStatus status)
    {
        try
        {
            await hub.Clients.Group(OrderStatusHub.GroupFor(orderId))
                .SendAsync(OrderStatusHub.StatusChangedEvent, new { orderId, status = status.ToString() });
        }
        catch (Exception ex)
        {
            // The status is already saved; customers still get it from the 15-second fallback check.
            logger.LogWarning(ex, "Could not send the live status update for order {OrderId}.", orderId);
        }
    }
}
