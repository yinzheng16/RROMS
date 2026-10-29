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
    private Task<List<StaffOrderRow>> StaffPendingRowsAsync(CancellationToken ct)
        => _context.Orders.AsNoTracking().Where(order => order.Status == OrderStatus.Pending)
            .OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Id)
            .Select(order => new StaffOrderRow(order.Id, order.OrderNo,
                order.User.FullName == "" ? "Customer" : order.User.FullName,
                order.OrderType, order.CreatedAt,
                order.Items.Sum(item => (int?)item.Quantity) ?? 0,
                order.TotalAmount, order.UpdatedAt, null,
                (order.Notes != null && order.Notes != "") || order.Items.Any(item => item.Notes != null && item.Notes != ""),
                order.Items.Count, order.DiningSessionId,
                order.OrderType == OrderType.DineIn
                    ? (order.DiningSession != null ? order.DiningSession.Table.TableNumber
                        : order.Reservation != null && order.Reservation.Table != null ? order.Reservation.Table.TableNumber : null)
                    : null))
            .ToListAsync(ct);

    private static StaffQueueSignal StaffSignal(IReadOnlyList<StaffOrderRow> rows)
    {
        // One materialized pending snapshot produces the rows, counts and stamp.
        // A replacement order with the same total count must still trigger notice.
        string text = string.Join("\n", rows.Select(row => FormattableString.Invariant(
            $"{row.Id}|{row.CreatedAt.Ticks}|{row.UpdatedAt?.Ticks}|{(int)row.Dining}|{row.Portions}|{row.Total}|{row.CustomerName}|{row.HasNotes}|{row.DishLineCount}|{row.DiningSessionId}|{row.TableNumber}")));
        string stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return new(stamp, rows.Count, rows.Count(row => row.Dining == OrderType.DineIn),
            rows.Count(row => row.Dining == OrderType.Takeaway), rows.Count == 0 ? 0 : rows.Max(row => row.Id));
    }

    public async Task<StaffOrderQueue> GetStaffOrderQueueAsync(string? search = null,
        string? dining = null, int page = 1, CancellationToken cancellationToken = default, string? sort = null, bool showAll = false)
    {
        var all = await StaffPendingRowsAsync(cancellationToken);
        string term = StaffQueueRules.Search(search), type = StaffQueueRules.Dining(dining);
        var matching = all.Where(row => (term.Length == 0 || row.OrderNo.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase))
            && (type == "All" || row.Dining.ToString() == type)).ToList();
        string sortOrder = StaffQueueRules.Sort(sort);
        // Sort the complete filtered snapshot before taking a page; the global signal stays independent of the view.
        if (sortOrder == "oldest") matching = matching.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToList();
        int pages = Math.Max(1, (int)Math.Ceiling(matching.Count / (double)StaffOrderQueue.PageSize));
        page = showAll ? 1 : Math.Clamp(page, 1, pages);
        var pageRows = showAll ? matching.ToArray()
            : matching.Skip((page - 1) * StaffOrderQueue.PageSize).Take(StaffOrderQueue.PageSize).ToArray();
        var ids = pageRows.Select(row => row.Id).ToArray();
        // Fetch preview lines only for displayed orders (one page or all matching rows).
        // Live signal checks still fetch no preview lines.
        // Keep note combinations separate, just as the full kitchen detail does.
        if (ids.Length > 0)
        {
            var lines = await _context.OrderItems.AsNoTracking().Where(item => ids.Contains(item.OrderId))
                .OrderBy(item => item.Id).Select(item => new { item.OrderId, item.Quantity, item.MenuItem.Name })
                .ToListAsync(cancellationToken);
            var previews = lines.GroupBy(item => item.OrderId).ToDictionary(group => group.Key,
                group => (IReadOnlyList<StaffOrderPreview>)group.Take(3)
                    .Select(item => new StaffOrderPreview(item.Name, item.Quantity)).ToArray());
            pageRows = pageRows.Select(row => row with
            {
                DishPreview = previews.TryGetValue(row.Id, out var preview) ? preview : Array.Empty<StaffOrderPreview>()
            }).ToArray();
        }
        return new(pageRows,
            StaffSignal(all), matching.Count, page, term, type, DateTime.Now, sortOrder, showAll);
    }

    public async Task<StaffQueueSignal> GetStaffQueueSignalAsync(CancellationToken cancellationToken = default)
        => StaffSignal(await StaffPendingRowsAsync(cancellationToken));

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
                ? order.DiningSession?.Table.TableNumber ?? booking?.TableNumber : null);
    }

    private static string? StaffQueueImage(string? path)
        => path is not null && (path.StartsWith("~/", StringComparison.Ordinal)
            || (path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal))) ? path : null;

    public async Task<StaffAcceptResult> AcceptStaffOrderAsync(int id, string staffUserId,
        CancellationToken cancellationToken = default)
    {
        // Check the actual active account and role as well as the page policy.
        bool allowed = !string.IsNullOrEmpty(staffUserId) && await _context.Users.AsNoTracking()
            .AnyAsync(user => user.Id == staffUserId && user.IsActive &&
                (from membership in _context.UserRoles
                 join role in _context.Roles on membership.RoleId equals role.Id
                 where membership.UserId == user.Id && (role.Name == Roles.Staff || role.Name == Roles.Admin)
                 select membership).Any(), cancellationToken);
        if (!allowed) return new(StaffAcceptStatus.Forbidden, "Only active staff or admin accounts can accept orders.");

        // Atomic compare-and-set. Two staff cannot both win; unrelated fields,
        // original prices, payment data and reservations are never overwritten.
        int changed = await _context.Orders.Where(order => order.Id == id && order.Status == OrderStatus.Pending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.Status, OrderStatus.Preparing)
                .SetProperty(order => order.UpdatedAt, DateTime.Now), cancellationToken);
        if (changed == 1)
        {
            string number = await _context.Orders.AsNoTracking().Where(order => order.Id == id)
                .Select(order => order.OrderNo).SingleAsync(cancellationToken);
            return new(StaffAcceptStatus.Success, $"Order {number} accepted and sent to preparation.");
        }
        bool exists = await _context.Orders.AsNoTracking().AnyAsync(order => order.Id == id, cancellationToken);
        return exists ? new(StaffAcceptStatus.AlreadyChanged, "This order has already been accepted or its status has changed. The queue has been refreshed.")
            : new(StaffAcceptStatus.NotFound, "This order is no longer available. The queue has been refreshed.");
    }
}
