using Microsoft.AspNetCore.SignalR;
using RROMS.Hubs;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

// Owner: Member 3 (US27). Called once, after a staff status change and its history row are saved.
//   1. US27: live update for the customer's open order page (only the order id and status name).
//   2. US37 / US38 (Member 4): bell notification, plus the email for the important moments
//      (takeaway ready for pickup, cancelled). The wording and the emails are decided by NotificationService.
public sealed class OrderStatusNotifier(
    IHubContext<OrderStatusHub> hub,
    INotificationService notifications,
    ILogger<OrderStatusNotifier> logger) : IOrderStatusNotifier
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

        // Never throws (Member 4 logs any failure), so the saved status change is never affected.
        await notifications.NotifyOrderStatusAsync(orderId, status);
    }
}
