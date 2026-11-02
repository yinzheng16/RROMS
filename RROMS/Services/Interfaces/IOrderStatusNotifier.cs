using RROMS.Models;

namespace RROMS.Services.Interfaces;

// Owner: Member 3 (US27). Tells customers watching an order that its status changed.
public interface IOrderStatusNotifier
{
    // Call only after the status change has been saved. Never throws.
    Task OrderStatusChangedAsync(int orderId, OrderStatus status);
}
