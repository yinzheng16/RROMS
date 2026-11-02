using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

public sealed partial class CustomerCartService
{
    public async Task<CustomerOrderHistory> GetOrderHistoryAsync(string userId, HistoryFilter input,
        CancellationToken cancellationToken = default)
    {
        var filter = CustomerOrderHistoryRules.Normalize(input);
        if (!await IsCustomerAsync(userId, cancellationToken))
            return new(Array.Empty<HistoryOrderRow>(), 0, 0, 1, filter, Forbidden: true);
        var owned = _db.Orders.AsNoTracking().Where(order => order.UserId == userId);
        int allCount = await owned.CountAsync(cancellationToken);
        var statusRows = await owned.GroupBy(order => order.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() }).ToArrayAsync(cancellationToken);
        int CountStatus(OrderStatus status) => statusRows.FirstOrDefault(row => row.Status == status)?.Count ?? 0;
        var counts = new Dictionary<string, int>
        {
            ["All"] = allCount,
            ["InProgress"] = CountStatus(OrderStatus.Pending) + CountStatus(OrderStatus.Preparing) + CountStatus(OrderStatus.ReadyToServe),
            ["Finished"] = CountStatus(OrderStatus.Served) + CountStatus(OrderStatus.Completed),
            ["Cancelled"] = CountStatus(OrderStatus.Cancelled)
        };
        if (filter.From.HasValue && filter.To.HasValue && filter.From > filter.To)
            return new(Array.Empty<HistoryOrderRow>(), 0, allCount, 1, filter, Error: "From date must be on or before To date.") { StatusCounts = counts };
        var matching = owned;
        string search = filter.Search ?? "";
        if (search.Length > 0)
            matching = matching.Where(order => order.OrderNo.Contains(search) ||
                order.Items.Any(line => line.MenuItem.Name.Contains(search)));
        if (filter.Status == "InProgress") matching = matching.Where(order => order.Status == OrderStatus.Pending || order.Status == OrderStatus.Preparing || order.Status == OrderStatus.ReadyToServe);
        else if (filter.Status == "Finished") matching = matching.Where(order => order.Status == OrderStatus.Served || order.Status == OrderStatus.Completed);
        else if (Enum.TryParse<OrderStatus>(filter.Status, out var status)) matching = matching.Where(order => order.Status == status);
        if (Enum.TryParse<OrderType>(filter.Dining, out var type)) matching = matching.Where(order => order.OrderType == type);
        if (filter.From is DateTime from) matching = matching.Where(order => order.CreatedAt >= from);
        if (filter.To is DateTime to && to.Date < DateTime.MaxValue.Date)
        {
            var exclusiveEnd = to.Date.AddDays(1);
            matching = matching.Where(order => order.CreatedAt < exclusiveEnd);
        }
        int count = await matching.CountAsync(cancellationToken);
        int pages = Math.Max(1, (int)Math.Ceiling(count / (double)CustomerOrderHistory.PageSize));
        int page = filter.ShowAll ? 1 : Math.Clamp(filter.PageNumber, 1, pages); filter.PageNumber = page;
        var ordered = filter.Sort switch
        {
            "oldest" => matching.OrderBy(order => order.CreatedAt).ThenBy(order => order.Id),
            "total-high" => matching.OrderByDescending(order => order.TotalAmount).ThenByDescending(order => order.Id),
            "total-low" => matching.OrderBy(order => order.TotalAmount).ThenByDescending(order => order.Id),
            _ => matching.OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Id)
        };
        var visible = filter.ShowAll ? ordered : ordered.Skip((page - 1) * CustomerOrderHistory.PageSize).Take(CustomerOrderHistory.PageSize);
        var orders = await visible
            .Include(order => order.Items).ThenInclude(line => line.MenuItem).AsSplitQuery().ToArrayAsync(cancellationToken);
        return new(orders.Select(order => new HistoryOrderRow(order.Id, order.OrderNo, order.Status, order.OrderType,
            order.CreatedAt, order.Items.Where(line => line.Quantity > 0).Sum(line => line.Quantity),
            order.Items.Count(line => line.Quantity > 0), order.TotalAmount,
            order.Items.Where(line => line.Quantity > 0).OrderBy(line => line.Id).Take(3)
                .Select(line => new HistoryDishPreview(line.MenuItem?.Name ?? "Dish no longer listed", line.Quantity, line.UnitPrice * line.Quantity)).ToArray())
            { Subtotal = order.Subtotal, ServiceCharge = order.ServiceCharge, Tax = order.TaxAmount }).ToArray(),
            count, allCount, page, filter) { StatusCounts = counts };
    }

    private Task<Order?> HistoryOwnedOrderAsync(string userId, int id, CancellationToken ct)
        => _db.Orders.AsNoTracking().Where(order => order.Id == id && order.UserId == userId)
            .Include(order => order.Reservation).Include(order => order.Items).ThenInclude(line => line.MenuItem)
            .AsSplitQuery().SingleOrDefaultAsync(ct);

    public async Task<HistoryOrderDetails?> GetHistoryOrderAsync(string userId, int orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0 || !await IsCustomerAsync(userId, cancellationToken)) return null;
        var order = await HistoryOwnedOrderAsync(userId, orderId, cancellationToken);
        if (order is null) return null;
        int[] ids = order.Items.Select(line => line.MenuItemId).Distinct().ToArray();
        var discounts = await ActiveDiscounts(ids, Now, cancellationToken);
        var booking = order.Reservation?.UserId == userId ? order.Reservation : null;
        return new(order.Id, order.OrderNo, order.Status, order.OrderType, order.CreatedAt, order.UpdatedAt,
            order.Notes, booking?.ReferenceNo, booking?.ReservedAt, booking?.Guests,
            order.Items.OrderBy(line => line.Id).Select(line => new HistoryOrderLine(line.Id, line.MenuItemId,
                line.MenuItem?.Code ?? "", line.MenuItem?.Name ?? "Dish no longer listed",
                CustomerOrderHistoryRules.SafeImage(line.MenuItem?.ImageUrl), line.Quantity, line.UnitPrice, line.Notes,
                line.MenuItem is not null && line.MenuItem.IsAvailable && line.MenuItem.Price > 0,
                line.MenuItem is null ? null : OrderPricingRules.DiscountedPrice(line.MenuItem.Price,
                    discounts.GetValueOrDefault(line.MenuItemId)))).ToArray(),
            order.Subtotal, order.ServiceCharge, order.TaxAmount, order.TotalAmount);
    }

    public async Task<HistoryReorderPreview> PrepareReorderAsync(string userId, int orderId,
        IReadOnlyList<HistoryReorderChoice> selections, bool copyDishNotes,
        CancellationToken cancellationToken = default)
    {
        HistoryReorderPreview Failure(CartResult result, string message)
            => new(result, message, orderId, "", Array.Empty<HistoryReorderLine>(), 0);
        if (!await IsCustomerAsync(userId, cancellationToken))
            return Failure(CartResult.Forbidden, "Please sign in with an active customer account.");
        string? invalid = CustomerOrderHistoryRules.ValidateChoices(selections);
        if (invalid is not null) return Failure(CartResult.Invalid, invalid);
        var order = await HistoryOwnedOrderAsync(userId, orderId, cancellationToken);
        if (order is null) return Failure(CartResult.Invalid, "That order is no longer available.");
        var selected = selections.Where(line => line.Selected).Select(line => new ReorderSelection(line.OrderItemId, line.Quantity)).ToArray();
        var lines = await BuildReorderLinesAsync(order, selected, copyDishNotes, Now, cancellationToken);
        if (lines is null) return Failure(CartResult.Changed, "The original order's dishes changed. Reopen it and choose again.");
        var cart = await LoadCart(userId, false, cancellationToken);
        int cartQuantity = cart?.Lines.Sum(line => line.Quantity) ?? 0;
        if (!lines.Any(line => line.Available))
            return new(CartResult.Unavailable, "None of the selected dishes are available to reorder right now.", order.Id, order.OrderNo, lines, cartQuantity);
        string? capacity = CustomerOrderHistoryRules.CapacityError(
            cart?.Lines.Select(line => (line.MenuItemId, line.Notes, line.Quantity)) ?? Enumerable.Empty<(int, string?, int)>(), lines);
        if (capacity is not null) return new(CartResult.Invalid, capacity, order.Id, order.OrderNo, lines, cartQuantity);
        var payload = new ReorderPayload(userId, order.Id, cart?.Revision ?? Guid.Empty,
            HistoryOrderStamp(order), ReorderPriceStamp(lines), selected, copyDishNotes, _clock.GetUtcNow().AddMinutes(10));
        string token = _protector.CreateProtector("HistoryReorder.v1").Protect(JsonSerializer.Serialize(payload));
        return new(CartResult.Success, "Review the selected dishes before adding them to your cart.",
            order.Id, order.OrderNo, lines, cartQuantity, token);
    }

    public Task<CartOperation> ConfirmReorderAsync(string userId, string? token,
        CancellationToken cancellationToken = default)
    {
        ReorderPayload? payload;
        try
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 32000)
                return Task.FromResult(new CartOperation(CartResult.Changed, "This reorder review expired. Choose your dishes again."));
            payload = JsonSerializer.Deserialize<ReorderPayload>(_protector.CreateProtector("HistoryReorder.v1").Unprotect(token));
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        { return Task.FromResult(new CartOperation(CartResult.Changed, "This reorder review expired. Choose your dishes again.")); }
        if (payload is null || payload.UserId != userId || payload.ExpiresAt < _clock.GetUtcNow() ||
            payload.Lines is null || payload.Lines.Length is < 1 or > 100 ||
            payload.Lines.Any(line => line.OrderItemId <= 0 || line.Quantity is < 1 or > 100) ||
            payload.Lines.Select(line => line.OrderItemId).Distinct().Count() != payload.Lines.Length)
            return Task.FromResult(new CartOperation(CartResult.Changed, "This reorder review expired. Choose your dishes again."));

        return WriteAsync(userId, async (cart, now, ct) =>
        {
            if (payload.ExpiresAt < _clock.GetUtcNow()) return new(CartResult.Changed, "This reorder review expired. Choose your dishes again.");
            // This also rejects replay/double submission after the successful Touch.
            if (cart.Revision != payload.CartRevision) return Changed();
            var order = await HistoryOwnedOrderAsync(userId, payload.OrderId, ct);
            if (order is null || HistoryOrderStamp(order) != payload.OrderStamp)
                return new(CartResult.Changed, "The original order changed. Reopen it and review your selection again.");
            var lines = await BuildReorderLinesAsync(order, payload.Lines, payload.CopyDishNotes, now, ct);
            if (lines is null || ReorderPriceStamp(lines) != payload.PriceStamp)
                return new(CartResult.Changed, "Prices or availability changed. Review the selected dishes again before adding them.");
            if (!lines.Any(line => line.Available)) return new(CartResult.Unavailable, "The selected dishes are no longer available.");
            string? capacity = CustomerOrderHistoryRules.CapacityError(
                cart.Lines.Select(line => (line.MenuItemId, line.Notes, line.Quantity)), lines);
            if (capacity is not null) return Invalid(capacity);
            foreach (var line in lines.Where(line => line.Available))
            {
                var same = cart.Lines.FirstOrDefault(row => row.MenuItemId == line.MenuItemId &&
                    OrderPricingRules.NormalizeNote(row.Notes) == line.Notes);
                if (same is null)
                    cart.Lines.Add(new CustomerCartLine { UserId = userId, MenuItemId = line.MenuItemId,
                        Quantity = line.Quantity, Notes = line.Notes, AddedAt = now });
                else same.Quantity += line.Quantity;
            }
            // Preserve the CURRENT cart's dining option, booking and remarks.
            // Never copy the source order's reservation, session or overall notes.
            Touch(cart, now);
            int added = lines.Where(line => line.Available).Sum(line => line.Quantity);
            int skipped = lines.Where(line => !line.Available).Sum(line => line.Quantity);
            int prices = lines.Count(line => line.PriceChanged);
            string message = $"Added {added} {(added == 1 ? "portion" : "portions")} to your cart.";
            if (skipped > 0) message += $" Skipped {skipped} unavailable {(skipped == 1 ? "portion" : "portions")}.";
            if (prices > 0) message += " Current prices and promotions apply.";
            return new CartOperation(CartResult.Success, message);
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<HistoryReorderLine>?> BuildReorderLinesAsync(Order order,
        IReadOnlyList<ReorderSelection> requested, bool copyDishNotes, DateTime now, CancellationToken ct)
    {
        var source = order.Items.ToDictionary(line => line.Id);
        if (requested.Any(line => !source.TryGetValue(line.OrderItemId, out var original) || original.Quantity <= 0)) return null;
        int[] ids = requested.Select(line => source[line.OrderItemId].MenuItemId).Distinct().ToArray();
        var items = await _db.MenuItems.AsNoTracking().Where(item => ids.Contains(item.Id)).ToDictionaryAsync(item => item.Id, ct);
        var discounts = await ActiveDiscounts(ids, now, ct);
        return requested.OrderBy(line => line.OrderItemId).Select(line =>
        {
            var original = source[line.OrderItemId]; var current = items.GetValueOrDefault(original.MenuItemId);
            bool available = current is not null && current.IsAvailable && current.Price > 0;
            return new HistoryReorderLine(original.Id, original.MenuItemId,
                current?.Name ?? "Dish no longer listed", line.Quantity, original.UnitPrice,
                current is null ? 0m : OrderPricingRules.DiscountedPrice(current.Price, discounts.GetValueOrDefault(current.Id)),
                copyDishNotes ? OrderPricingRules.NormalizeNote(original.Notes) : null,
                available, available ? null : current is null ? "No longer on the menu" : "Currently unavailable");
        }).ToArray();
    }

    private static string HistoryOrderStamp(Order order) => HistoryHash(JsonSerializer.Serialize(new
    {
        order.Id, order.UserId, order.Status, order.UpdatedAt,
        Lines = order.Items.OrderBy(line => line.Id).Select(line => new { line.Id, line.MenuItemId, line.Quantity, line.UnitPrice, line.Notes })
    }));
    private static string ReorderPriceStamp(IReadOnlyList<HistoryReorderLine> lines)
        => HistoryHash(JsonSerializer.Serialize(lines.OrderBy(line => line.OrderItemId)));
    private static string HistoryHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record ReorderSelection(int OrderItemId, int Quantity);
    private sealed record ReorderPayload(string UserId, int OrderId, Guid CartRevision,
        string OrderStamp, string PriceStamp, ReorderSelection[] Lines, bool CopyDishNotes, DateTimeOffset ExpiresAt);
}
