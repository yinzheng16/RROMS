using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

// Owner: Member 3 / US25. Reuses the existing IOrderService registration.
public partial class OrderService
{
    public async Task<StaffOrderDetails?> GetStaffOrderDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        // No prices are recalculated: show the saved order/item amounts.
        var order = await _context.Orders.AsNoTracking().Where(row => row.Id == id)
            .Include(row => row.User).Include(row => row.Reservation).ThenInclude(booking => booking!.Table)
            .Include(row => row.DiningSession).ThenInclude(session => session!.Table)
            .Include(row => row.Items).ThenInclude(item => item.MenuItem)
            .AsSplitQuery().SingleOrDefaultAsync(cancellationToken);
        if (order is null) return null;
        var booking = order.OrderType == OrderType.DineIn && order.Reservation is not null
            ? new StaffOrderBooking(order.Reservation.ReferenceNo, order.Reservation.ReservedAt,
                order.Reservation.Guests, order.Reservation.Status.ToString(), order.Reservation.Table?.TableNumber) : null;
        return new(order.Id, order.OrderNo, string.IsNullOrWhiteSpace(order.User?.FullName) ? "Customer" : order.User.FullName,
            order.OrderType, order.Status, order.CreatedAt, DateTime.Now,
            order.Items.OrderBy(item => item.Id).Select(item => new StaffOrderDish(item.Id, item.MenuItemId,
                item.MenuItem?.Code ?? "", item.MenuItem?.Name ?? "Dish no longer listed", StaffQueueImage(item.MenuItem?.ImageUrl),
                item.Quantity, item.UnitPrice, item.Notes)).ToArray(), order.Notes, booking,
            order.Subtotal, order.TaxAmount, order.ServiceCharge, order.TotalAmount, order.UserId,
            DiningSessionId: order.DiningSessionId,
            TableNumber: order.OrderType == OrderType.DineIn
                ? order.DiningSession?.Table.TableNumber ?? booking?.TableNumber : null)
        { TaxRatePercent = order.TaxRatePercent, ServiceChargePercent = order.ServiceChargePercent };
    }

    private static string? StaffQueueImage(string? path)
        => path is not null && (path.StartsWith("~/", StringComparison.Ordinal)
            || (path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal))) ? path : null;

}
