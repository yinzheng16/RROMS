using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;
public partial class OrderService
{
    public async Task<StaffWorkflowSignal> GetStaffWorkflowSignalAsync(CancellationToken cancellationToken = default)
    {
        var groups = await _context.Orders.AsNoTracking().GroupBy(o => new { o.Status, o.OrderType })
            .Select(g => new { g.Key.Status, g.Key.OrderType, Count = g.Count(), LastId = g.Max(o => o.Id),
                Updated = g.Max(o => o.UpdatedAt ?? o.CreatedAt) }).ToListAsync(cancellationToken);
        var source = string.Join("|", groups.OrderBy(g => g.Status).ThenBy(g => g.OrderType)
            .Select(g => $"{g.Status}:{g.OrderType}:{g.Count}:{g.LastId}:{g.Updated.Ticks}"));
        var active = groups.Where(g => OrderStatusRules.Active(g.Status)).ToArray();
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            active.Sum(g => g.Count), active.Where(g => g.OrderType == OrderType.DineIn).Sum(g => g.Count),
            active.Where(g => g.OrderType == OrderType.Takeaway).Sum(g => g.Count),
            groups.Where(g => g.Status == OrderStatus.Pending).Sum(g => g.Count));
    }

    public async Task<StaffWorkflowBoard> GetStaffWorkflowBoardAsync(string? status = null, string? search = null,
        string? dining = null, string? sort = null, int page = 1, CancellationToken cancellationToken = default)
    {
        var stage = OrderStatusRules.View(status);
        string term = StaffQueueRules.Search(search), type = StaffQueueRules.Dining(dining);
        string sorting = sort == "oldest" ? "oldest" : "newest"; // Default: newest first.
        var query = _context.Orders.AsNoTracking().AsQueryable();
        if (term.Length > 0) query = query.Where(o => o.OrderNo.Contains(term) || o.User.FullName.Contains(term));
        if (type != "All") { var kind = type == "DineIn" ? OrderType.DineIn : OrderType.Takeaway; query = query.Where(o => o.OrderType == kind); }
        var counts = await query.GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
        // A full order number typed in search: report which status it is in, so the page can open it there.
        var exact = term.Length == 0 ? null : await query.Where(o => o.OrderNo == term)
            .Select(o => new { o.Id, o.Status }).FirstOrDefaultAsync(cancellationToken);
        var stageQuery = query.Where(o => o.Status == stage);
        // 31 Oct: the separate "Longest waiting" section was removed; every order of the stage is in one list.
        int remaining = counts.GetValueOrDefault(stage);
        int pages = Math.Max(1, (int)Math.Ceiling(remaining / (double)StaffWorkflowBoard.PageSize));
        // Display-only paging: page 0 = show all; any other value is clamped as before.
        bool showAll = page == StaffWorkflowBoard.ShowAllPage;
        page = showAll ? StaffWorkflowBoard.ShowAllPage : Math.Clamp(page, 1, pages);
        var sorted = sorting == "newest" ? stageQuery.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            : stageQuery.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id);
        var visible = showAll ? sorted : sorted.Skip((page - 1) * StaffWorkflowBoard.PageSize).Take(StaffWorkflowBoard.PageSize);
        var ids = await visible.Select(o => o.Id).ToArrayAsync(cancellationToken);
        var rows = await _context.Orders.AsNoTracking().Where(o => ids.Contains(o.Id))
            .Select(o => new StaffOrderRow(o.Id, o.OrderNo, o.User.FullName == "" ? "Customer" : o.User.FullName,
                o.OrderType, o.CreatedAt, o.Items.Sum(i => (int?)i.Quantity) ?? 0, o.TotalAmount, o.UpdatedAt, null,
                (o.Notes != null && o.Notes != "") || o.Items.Any(i => i.Notes != null && i.Notes != ""),
                o.Items.Count, o.DiningSessionId, o.OrderType == OrderType.DineIn
                    ? (o.DiningSession != null ? o.DiningSession.Table.TableNumber : o.Reservation != null && o.Reservation.Table != null
                        ? o.Reservation.Table.TableNumber : null) : null)).ToListAsync(cancellationToken);
        var lines = await _context.OrderItems.AsNoTracking().Where(i => ids.Contains(i.OrderId)).OrderBy(i => i.Id)
            .Select(i => new { i.OrderId, i.Quantity, i.MenuItem.Name }).ToListAsync(cancellationToken);
        var lookup = rows.Select(o => o with { DishPreview = lines.Where(i => i.OrderId == o.Id).Take(2)
            .Select(i => new StaffOrderPreview(i.Name, i.Quantity)).ToArray() }).ToDictionary(o => o.Id);
        // Retain IDs selected for this snapshot; an intervening update is checked again on every action.
        return new(stage, term, type, sorting, page, remaining, counts,
            ids.Where(lookup.ContainsKey).Select(id => new StaffWorkflowCard(lookup[id], stage)).ToArray(),
            await GetStaffWorkflowSignalAsync(cancellationToken), DateTime.Now)
        { ExactOrderId = exact?.Id, ExactOrderStatus = exact?.Status };
    }

    public async Task<StaffWorkflowDetails?> GetStaffWorkflowDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await GetStaffOrderDetailsAsync(id, cancellationToken);
        if (order is null) return null;
        var history = await _context.OrderStatusLogs.AsNoTracking().Where(l => l.OrderId == id)
            .OrderBy(l => l.ChangedAt).ThenBy(l => l.Id).Select(l => new StaffWorkflowEvent(l.FromStatus, l.ToStatus,
                l.ChangedAt, l.ChangedByName, l.ChangedByRole, l.Reason)).ToListAsync(cancellationToken);
        var billing = await _context.Orders.AsNoTracking().Where(o => o.Id == id)
            .Select(o => new { o.BillId, o.DiningSessionId }).SingleAsync(cancellationToken);
        return new(order, history, await CancellationBlockAsync(id, billing.BillId, billing.DiningSessionId, cancellationToken));
    }

    private async Task<string?> CancellationBlockAsync(int id, int? billId, int? sessionId, CancellationToken ct)
    {
        // Session bills may be confirmed before every Order.BillId association is read by this request.
        var bills = _context.Bills.AsNoTracking().Where(b => b.Id == billId
            || b.Orders.Any(o => o.Id == id) || sessionId != null && b.DiningSessionId == sessionId);
        if (await bills.AnyAsync(b => b.Status == BillStatus.Confirmed || b.Status == BillStatus.Paid, ct))
            return "Cancellation is locked because the bill is confirmed or paid. Contact the billing staff.";
        var billIds = bills.Select(b => b.Id);
        if (await _context.Payments.AsNoTracking().AnyAsync(p => (p.OrderId == id
                || p.BillId != null && billIds.Contains(p.BillId.Value))
                && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Paid), ct))
            return "Cancellation is locked while payment is pending or has been paid.";
        return null;
    }

    public async Task<StaffStatusResult> ChangeStaffOrderStatusAsync(int id, OrderStatus expectedStatus,
        OrderStatus targetStatus, string? reason, string staffUserId, CancellationToken cancellationToken = default)
    {
        // Check the persisted active account/role; client-side controls are never authorization.
        var actor = await _context.Users.AsNoTracking().Where(u => u.Id == staffUserId && u.IsActive)
            .Select(u => new { u.FullName, Admin = (from m in _context.UserRoles join r in _context.Roles on m.RoleId equals r.Id
                where m.UserId == u.Id && r.Name == Roles.Admin select m).Any(),
                Staff = (from m in _context.UserRoles join r in _context.Roles on m.RoleId equals r.Id
                where m.UserId == u.Id && r.Name == Roles.Staff select m).Any() }).SingleOrDefaultAsync(cancellationToken);
        if (actor is null || !actor.Admin && !actor.Staff)
            return new(StaffStatusOutcome.Forbidden, "Only active staff or admin accounts can update orders.");
        string? invalid = OrderStatusRules.Validate(expectedStatus, targetStatus, reason);
        if (invalid is not null) return new(StaffStatusOutcome.Invalid, invalid);
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var location = await _context.Orders.AsNoTracking().Where(o => o.Id == id)
            .Select(o => new { o.DiningSessionId }).SingleOrDefaultAsync(cancellationToken);
        if (location is null) return new(StaffStatusOutcome.NotFound, "This order is no longer available.");

        async Task<StaffStatusResult> Apply()
        {
            // Re-read AFTER acquiring the same per-order/session lock as bill confirmation and legacy payment.
            var current = await _context.Orders.AsNoTracking().Where(o => o.Id == id)
                .Select(o => new { o.OrderNo, o.Status, o.BillId, o.DiningSessionId }).SingleOrDefaultAsync(cancellationToken);
            if (current is null) return new(StaffStatusOutcome.NotFound, "This order is no longer available.");
            if (current.Status != expectedStatus || current.DiningSessionId != location.DiningSessionId)
                return new(StaffStatusOutcome.Conflict, "This order changed while you were reviewing it. Refresh its details before trying again.");
            if (targetStatus == OrderStatus.Cancelled)
            {
                string? blocked = await CancellationBlockAsync(id, current.BillId, current.DiningSessionId, cancellationToken);
                if (blocked is not null) return new(StaffStatusOutcome.Locked, blocked);
            }
            DateTime now = DateTime.Now;
            int changed = await _context.Orders.Where(o => o.Id == id && o.Status == expectedStatus
                    && o.DiningSessionId == location.DiningSessionId)
                .ExecuteUpdateAsync(set => set.SetProperty(o => o.Status, targetStatus).SetProperty(o => o.UpdatedAt, now), cancellationToken);
            if (changed != 1) return new(StaffStatusOutcome.Conflict, "Another staff member updated this order. Refresh its details.");
            var log = new OrderStatusLog { OrderId = id, FromStatus = expectedStatus, ToStatus = targetStatus,
                ChangedAt = now, ChangedByUserId = staffUserId, ChangedByName = string.IsNullOrWhiteSpace(actor.FullName) ? "Staff" : actor.FullName,
                ChangedByRole = actor.Admin ? Roles.Admin : Roles.Staff, Reason = targetStatus == OrderStatus.Cancelled ? reason : null };
            _context.OrderStatusLogs.Add(log);
            // A failed history insert throws and rolls back the conditional update in the repository transaction.
            await _context.SaveChangesAsync(cancellationToken);
            return new(StaffStatusOutcome.Success, $"Order {current.OrderNo} is now {OrderStatusRules.Label(targetStatus)}.");
        }
        return location.DiningSessionId is int sessionId
            ? await _orderTransactions.ExecuteForSessionBillAsync(sessionId, Apply)
            : await _orderTransactions.ExecuteForTakeawayOrderAsync(id, Apply);
    }
}
