using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

// Owner: Member 3 / US24. Prices, ownership, roles and checkout are server-validated.
public sealed class CustomerCartService : ICustomerCartService
{
    private readonly ApplicationDbContext _db;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _clock;
    private readonly ILogger<CustomerCartService> _logger;
    private readonly IProfileService _profiles;
    private readonly IReservationService _reservations;
    private readonly IDiningSessionService _sessions;
    private readonly IReportService _reportService;
    private const int MaxLines = 100;

    public CustomerCartService(ApplicationDbContext db, IDataProtectionProvider protection,
        ILogger<CustomerCartService> logger, IProfileService profiles, IReportService reportService,
        IReservationService reservations, IDiningSessionService sessions, TimeProvider? clock = null)
    {
        _db = db;
        _protector = protection.CreateProtector("RROMS.US24.Checkout.v1");
        _logger = logger;
        _profiles = profiles;
        _reservations = reservations;
        _sessions = sessions;
        _reportService = reportService;
        _clock = clock ?? TimeProvider.System;
    }

    private DateTime Now => _clock.GetLocalNow().DateTime;

    private Task<bool> IsCustomerAsync(string userId, CancellationToken ct)
        => _db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.IsActive &&
            (from membership in _db.UserRoles
             join role in _db.Roles on membership.RoleId equals role.Id
             where membership.UserId == user.Id && role.Name == Roles.Customer
             select membership).Any(), ct);

    public async Task<int> CountAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(userId, cancellationToken)) return 0;
        return await _db.CustomerCartLines.AsNoTracking().Where(line => line.UserId == userId)
            .SumAsync(line => (int?)line.Quantity, cancellationToken) ?? 0;
    }

    public async Task<CartView> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(userId, cancellationToken)) return CartView.Empty;
        var cart = await LoadCart(userId, false, cancellationToken);
        return await BuildView(cart, userId, Now, cancellationToken);
    }

    public Task<CartOperation> AddAsync(string userId, int menuItemId, int quantity, string? notes,
        CancellationToken cancellationToken = default)
    {
        notes = OrderPricingRules.NormalizeNote(notes);
        if (quantity is < 1 or > 100 || notes?.Length > 200)
            return Task.FromResult(Invalid("Use a quantity from 1 to 100 and a dish note of up to 200 characters."));
        return WriteAsync(userId, async (cart, now, ct) =>
        {
            var item = await _db.MenuItems.AsNoTracking().SingleOrDefaultAsync(m => m.Id == menuItemId, ct);
            if (item is null || !item.IsAvailable || item.Price <= 0)
                return new(CartResult.Unavailable, "That dish is currently unavailable. Please choose another favourite.");
            var same = cart.Lines.FirstOrDefault(line => line.MenuItemId == menuItemId && line.Notes == notes);
            if (same is not null)
            {
                if (same.Quantity + quantity > 100) return Invalid("Each dish and note combination can have up to 100 portions.");
                same.Quantity += quantity;
            }
            else
            {
                if (cart.Lines.Count >= MaxLines) return Invalid("Your cart can hold up to 100 different dish and note combinations.");
                cart.Lines.Add(new()
                {
                    UserId = userId,
                    MenuItemId = menuItemId,
                    Quantity = quantity,
                    Notes = notes,
                    AddedAt = now
                });
            }
            Touch(cart, now);
            return new(CartResult.Success, quantity + " × " + item.Name + " added to your cart.");
        }, cancellationToken);
    }

    public Task<CartOperation> UpdateAsync(string userId, CartUpdate input, Guid? removeLineId = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(input.OrderType) || input.Lines is null || input.Lines.Count > MaxLines ||
            input.Lines.Any(line => line.Quantity is < 1 or > 100 ||
                OrderPricingRules.NormalizeNote(line.Notes)?.Length > 200) ||
            OrderPricingRules.NormalizeNote(input.Notes)?.Length > 500)
            return Task.FromResult(Invalid("Check quantities and keep dish notes within 200 characters and order remarks within 500."));

        return WriteAsync(userId, async (cart, now, ct) =>
        {
            if (cart.Revision != input.Revision ||
                input.Lines.Select(line => line.Id).Distinct().Count() != input.Lines.Count ||
                !cart.Lines.Select(line => line.Id).ToHashSet().SetEquals(input.Lines.Select(line => line.Id)))
                return Changed();
            if (removeLineId.HasValue && !cart.Lines.Any(line => line.Id == removeLineId))
                return Invalid("That cart row no longer exists.");

            // Drafts may be saved before a booking is selected. Both review and
            // final placement require an owned, eligible reservation for dine-in.
            int? reservation = input.OrderType == OrderType.DineIn ? input.ReservationId : null;
            if (reservation.HasValue && !await EligibleReservations(userId, now)
                .AnyAsync(row => row.Id == reservation.Value, ct))
                return Invalid("That reservation is no longer available. Choose another reservation or switch to Takeaway.");

            // Sessions come only from the selected reservation, never a posted table ID.
            int? sessionId = null;

            string? removalMessage = null;
            if (removeLineId.HasValue)
            {
                var removedLine = cart.Lines.Single(line => line.Id == removeLineId.Value);
                string? dishName = await _db.MenuItems.AsNoTracking()
                    .Where(item => item.Id == removedLine.MenuItemId)
                    .Select(item => item.Name).SingleOrDefaultAsync(ct);
                removalMessage = removedLine.Quantity + " × " +
                    (string.IsNullOrWhiteSpace(dishName) ? "Unavailable dish" : dishName) +
                    " removed from your cart.";
            }

            bool changed = false;
            foreach (var line in cart.Lines.ToList())
            {
                if (line.Id == removeLineId) { cart.Lines.Remove(line); changed = true; continue; }
                var requested = input.Lines.Single(row => row.Id == line.Id);
                string? note = OrderPricingRules.NormalizeNote(requested.Notes);
                changed |= line.Quantity != requested.Quantity || line.Notes != note;
                line.Quantity = requested.Quantity;
                line.Notes = note;
            }
            foreach (var group in cart.Lines.GroupBy(line => (line.MenuItemId, line.Notes)).ToArray())
            {
                int sum = group.Sum(line => line.Quantity);
                if (sum > 100) return Invalid("Combining those identical notes would exceed 100 portions.");
                var keep = group.OrderBy(line => line.AddedAt).ThenBy(line => line.Id).First();
                foreach (var extra in group.Where(line => line != keep).ToArray())
                { cart.Lines.Remove(extra); changed = true; }
                keep.Quantity = sum;
            }
            // Empty string means this draft's remarks were explicitly submitted
            // empty. Keep it distinct from null (not filled in yet), so clearing
            // the suggested profile text survives refreshes and later cart edits.
            string remarks = OrderPricingRules.NormalizeNote(input.Notes) ?? string.Empty;
            changed |= cart.Notes != remarks || cart.OrderType != input.OrderType ||
                cart.ReservationId != reservation || cart.DiningSessionId != sessionId;
            cart.Notes = remarks;
            cart.OrderType = input.OrderType;
            cart.ReservationId = reservation;
            cart.DiningSessionId = sessionId;
            if (changed) Touch(cart, now);
            return new CartOperation(CartResult.Success, removalMessage ?? "Cart saved.");
        }, cancellationToken);
    }

    public async Task<CheckoutQuote> PrepareAsync(string userId, Guid revision,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(userId, cancellationToken))
            return new(CartResult.Forbidden, "Please sign in with an active customer account.", CartView.Empty);
        var view = await GetAsync(userId, cancellationToken);
        var validation = ValidateForCheckout(view);
        if (view.Revision != revision) validation = Changed();
        if (!validation.Succeeded) return new(validation.Status, validation.Message, view);
        string? orderingError = await GetOrderingHoursError(cancellationToken);
        if (orderingError is not null) return new(CartResult.Invalid, orderingError, view);
        // 20-character order number, independently protected from client editing.
        string orderNo = "YR" + Now.ToString("yyMMdd", CultureInfo.InvariantCulture) +
            Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var quote = new QuotePayload(userId, view.Revision, orderNo, Stamp(view),
            _clock.GetUtcNow().AddMinutes(10));
        return new(CartResult.Success, "Review your order before confirming.", view,
            _protector.Protect(JsonSerializer.Serialize(quote)));
    }

    public Task<CartOperation> PlaceAsync(string userId, string? token,
        CancellationToken cancellationToken = default)
    {
        QuotePayload? quote;
        try
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 8000) return Task.FromResult(Expired());
            quote = JsonSerializer.Deserialize<QuotePayload>(_protector.Unprotect(token));
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        { return Task.FromResult(Expired()); }
        if (quote is null || quote.UserId != userId) return Task.FromResult(Expired());

        return WriteAsync(userId, async (cart, now, ct) =>
        {
            // A retry of the SAME confirmation returns the existing order, even if
            // the customer has since added new dishes. It never clears that new cart.
            var existing = await _db.Orders.AsNoTracking()
                .Where(order => order.OrderNo == quote.OrderNo && order.UserId == userId)
                .Select(order => (int?)order.Id).SingleOrDefaultAsync(ct);
            if (existing.HasValue) return new(CartResult.Success, "Your order has already been received.", existing);
            if (quote.ExpiresAt < _clock.GetUtcNow()) return Expired();
            if (cart.Revision != quote.Revision) return Changed();

            // Serializable reads prevent price, settings and reservation changes
            // between the final validation and the atomic order/cart save.
            var view = await BuildView(cart, userId, now, ct);
            var validation = ValidateForCheckout(view);
            if (!validation.Succeeded) return validation;
            if (Stamp(view) != quote.Stamp)
                return new(CartResult.Changed, "Prices, charges or reservation details changed. Review the updated cart and confirm again.");

            // A quote prepared before last orders does not bypass the cutoff.
            // Check fresh restaurant hours and the current clock inside the save transaction.
            string? orderingError = await GetOrderingHoursError(ct);
            if (orderingError is not null) return Invalid(orderingError);

            if (view.DiningSessionId.HasValue)
            {
                // The shared scoped DbContext puts this immediate UPDATE inside
                // WriteAsync's transaction. A later failure rolls the claim back too.
                ct.ThrowIfCancellationRequested();
                var claim = await _sessions.ClaimOrValidateSessionAsync(view.DiningSessionId.Value, userId);
                if (!claim.Success) return Invalid(claim.Message);
                bool stillValid = await _db.DiningSessions.AsNoTracking().AnyAsync(session =>
                    session.Id == view.DiningSessionId.Value && session.Status == DiningSessionStatus.Open &&
                    session.CustomerId == userId && session.ReservationId == view.ReservationId, ct);
                if (!stillValid) return Invalid("Your reservation or dining session changed. Please review your reservation again.");
            }

            var order = new Order
            {
                OrderNo = quote.OrderNo,
                UserId = userId,
                OrderType = view.OrderType,
                ReservationId = view.OrderType == OrderType.DineIn ? view.ReservationId : null,
                DiningSessionId = view.OrderType == OrderType.DineIn ? view.DiningSessionId : null,
                Status = OrderStatus.Pending,
                Subtotal = view.Totals.Subtotal,
                TaxAmount = view.Totals.Tax,
                ServiceCharge = view.Totals.ServiceCharge,
                TotalAmount = view.Totals.Total,
                Notes = view.Notes,
                CreatedAt = now
            };
            foreach (var line in view.Lines)
                order.Items.Add(new()
                {
                    MenuItemId = line.MenuItemId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Notes = line.Notes
                });
            _db.Orders.Add(order);
            cart.Lines.Clear();
            cart.Notes = null;
            cart.ReservationId = null;
            cart.DiningSessionId = null;
            cart.OrderType = OrderType.DineIn;
            Touch(cart, now);
            // Save together so a failure leaves BOTH the cart and order unchanged.
            await _db.SaveChangesAsync(ct);
            return new(CartResult.Success, "Your order has been received.", order.Id);
        }, cancellationToken);
    }

    // Lightweight, read-only status check. Never expose another customer's order.
    public async Task<OrderStatus?> GetOrderStatusAsync(string userId, int orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0 || !await IsCustomerAsync(userId, cancellationToken)) return null;
        return await _db.Orders.AsNoTracking()
            .Where(order => order.Id == orderId && order.UserId == userId)
            .Select(order => (OrderStatus?)order.Status)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<OrderReceipt?> ReceiptAsync(string userId, int orderId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(userId, cancellationToken)) return null;
        var order = await _db.Orders.AsNoTracking().Include(order => order.Reservation)
            .Include(order => order.Items).ThenInclude(line => line.MenuItem)
            .SingleOrDefaultAsync(order => order.Id == orderId && order.UserId == userId, cancellationToken);
        if (order is null) return null;
        return new(order.Id, order.OrderNo, order.Status, order.OrderType, order.CreatedAt, order.Notes,
            order.Reservation?.ReferenceNo, order.Reservation?.ReservedAt, order.Reservation?.Guests,
            order.Items.OrderBy(line => line.Id).Select(line => new CartLineView(Guid.Empty,
                line.MenuItemId, line.MenuItem.Code, line.MenuItem.Name, line.MenuItem.ImageUrl,
                line.Quantity, line.Notes, line.UnitPrice, true)).ToArray(),
            order.Subtotal, order.TaxAmount, order.ServiceCharge, order.TotalAmount);
    }

    private IQueryable<Reservation> EligibleReservations(string userId, DateTime now)
        => _db.Reservations.AsNoTracking().Where(reservation => reservation.UserId == userId &&
            (reservation.Status == ReservationStatus.Pending || reservation.Status == ReservationStatus.Confirmed ||
             reservation.Status == ReservationStatus.Seated) &&
            reservation.ReservedAt.AddMinutes(reservation.DurationMinutes) > now);

    private async Task<CartView> BuildView(CustomerCart? cart, string userId, DateTime now, CancellationToken ct)
    {
        var reservations = await EligibleReservations(userId, now).OrderBy(row => row.ReservedAt)
            .Select(row => new CartReservation(row.Id, row.ReferenceNo, row.ReservedAt, row.Guests, row.Status)).ToArrayAsync(ct);
        var settings = await _db.RestaurantSettings.AsNoTracking().OrderBy(row => row.Id).FirstOrDefaultAsync(ct);
        int[] ids = cart?.Lines.Select(line => line.MenuItemId).Distinct().ToArray() ?? Array.Empty<int>();
        // Always read fresh menu values, including after an execution-strategy
        // retry. Tracked entities from an earlier attempt must not supply prices.
        var items = await _db.MenuItems.AsNoTracking().Where(item => ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);
        var discounts = await ActiveDiscounts(ids, now, ct);
        var lines = cart?.Lines.OrderBy(line => line.AddedAt).ThenBy(line => line.Id)
            .Select(line =>
            {
                var item = items.GetValueOrDefault(line.MenuItemId);
                return new CartLineView(line.Id, line.MenuItemId, item?.Code ?? "—",
                    item?.Name ?? "Dish no longer on the menu", item?.ImageUrl, line.Quantity, line.Notes,
                    item is null ? 0 : OrderPricingRules.DiscountedPrice(item.Price, discounts.GetValueOrDefault(line.MenuItemId)),
                    item?.IsAvailable == true && item.Price > 0);
            }).ToArray() ?? Array.Empty<CartLineView>();
        var orderType = cart?.OrderType ?? OrderType.DineIn;
        // US49 owns the charge formula. Use its loaded-settings overload so this
        // fresh, untracked read also stays inside PlaceAsync's save transaction.
        // If settings are absent, preserve the existing zero-charge fallback.
        var charges = _reportService.CalculateCharges(settings ?? new RestaurantSetting
        {
            TaxRatePercent = 0m,
            ServiceChargePercent = 0m,
            ServiceChargeOnTakeaway = false
        }, lines.Sum(line => line.LineTotal), orderType == OrderType.Takeaway);
        var totals = new CartTotals(
            Subtotal: charges.Subtotal,
            TaxRate: charges.TaxRatePercent,
            Tax: charges.Tax,
            ServiceRate: charges.ServiceChargePercent,
            ServiceCharge: charges.ServiceCharge,
            Total: charges.Total);

        string? remarks = cart?.Notes;
        bool fromProfile = false;
        string? separatePreferences = null;
        if (lines.Length > 0)
        {
            // userId has already been validated as the current customer. Never
            // use a client-supplied profile ID or overwrite an existing draft.
            string? summary = await _profiles.GetDiningSummaryAsync(userId);
            string? suggested = OrderDiningPreferencesRules.RemarksDefault(summary);
            if (remarks is null && suggested is not null)
            {
                remarks = suggested;
            }
            fromProfile = OrderDiningPreferencesRules.IsProfileCopy(remarks, summary);
            if (!string.IsNullOrWhiteSpace(summary) && suggested is null)
                separatePreferences = summary;
        }
        var selectedBooking = reservations.FirstOrDefault(row => row.Id == cart?.ReservationId);
        var selection = orderType == OrderType.DineIn
            ? await ResolveDiningSessionAsync(userId, selectedBooking, ct)
            : new DiningSelection(null, null);
        return new(cart?.Revision ?? Guid.Empty, lines, orderType,
            cart?.ReservationId, remarks, totals, reservations, fromProfile, separatePreferences,
            selection.Session?.Id, Array.Empty<CartDiningSession>(), selection.Session, selection.Error);
    }

    private sealed record DiningSelection(CartDiningSession? Session, string? Error);

    private async Task<DiningSelection> ResolveDiningSessionAsync(string userId, CartReservation? booking,
        CancellationToken ct)
    {
        if (booking is null) return new(null, null); // Incomplete draft; checkout rejects it.
        // Query all statuses: a closed visit must never become a preorder again.
        var session = await _db.DiningSessions.AsNoTracking().Include(row => row.Table)
            .SingleOrDefaultAsync(row => row.ReservationId == booking.Id, ct);
        if (session is null)
            return booking.Status == ReservationStatus.Seated
                ? new(null, "Your table session is not ready. Please ask staff for help.")
                : new(null, null); // Pending/Confirmed reservation, before seating.
        if (session.Status != DiningSessionStatus.Open)
            return new(null, "This table session has ended. Please ask staff for help.");
        if (session.CustomerId is not null && session.CustomerId != userId)
            return new(null, "This reservation's dining session is unavailable. Please ask staff for help.");
        return new(new CartDiningSession(session.Id, session.TableId, session.Table.TableNumber, session.ReservationId), null);
    }

    private async Task<string?> GetOrderingHoursError(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // The shared schedule expects restaurant-local wall time. Keep the existing
        // Malaysian time conversion independent of the server's local time zone.
        DateTime restaurantNow = _clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).DateTime;
        bool canOrder = await _reservations.CanOrderAtAsync(restaurantNow);
        ct.ThrowIfCancellationRequested();
        return canOrder ? null
            : "Online ordering is currently closed. You can still browse the menu and keep items in your cart. Please try again during ordering hours.";
    }

    private Task<Dictionary<int, decimal>> ActiveDiscounts(int[] ids, DateTime now, CancellationToken ct)
        => _db.Promotions.AsNoTracking().Where(p => ids.Contains(p.MenuItemId) && p.IsActive &&
            p.StartDate <= now && p.EndDate >= now.Date)
            .GroupBy(p => p.MenuItemId).Select(group => new { Id = group.Key, Percent = group.Max(p => p.DiscountPercent) })
            .ToDictionaryAsync(row => row.Id, row => row.Percent, ct);

    private Task<CustomerCart?> LoadCart(string userId, bool tracking, CancellationToken ct)
    {
        var query = _db.CustomerCarts.Include(cart => cart.Lines);
        return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(cart => cart.UserId == userId, ct);
    }

    private async Task<CartOperation> WriteAsync(string userId,
        Func<CustomerCart, DateTime, CancellationToken, Task<CartOperation>> action, CancellationToken ct)
    {
        if (!await IsCustomerAsync(userId, ct))
            return new(CartResult.Forbidden, "Please sign in with an active customer account.");
        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                    var cart = await LoadCart(userId, true, ct);
                    if (cart is null)
                    {
                        cart = new() { UserId = userId, Revision = Guid.Empty, UpdatedAt = Now };
                        _db.CustomerCarts.Add(cart);
                    }
                    var result = await action(cart, Now, ct);
                    if (!result.Succeeded) return result;
                    if (cart.Revision == Guid.Empty) Touch(cart, Now);
                    await _db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return result;
                }
                catch (DbUpdateConcurrencyException) { return Changed(); }
                catch (Exception error) when (error.GetBaseException() is SqlException sql &&
                    sql.Number is 1205 or 2601 or 2627)
                {
                    if (attempt == 2) _logger.LogWarning(error, "Concurrent cart update could not be retried.");
                    else await Task.Delay(40 * (attempt + 1), ct);
                }
                finally
                {
                    // One scoped context; don't retain failed draft/order entities for a retry.
                    foreach (var entry in _db.ChangeTracker.Entries().Where(entry =>
                        entry.Entity is CustomerCart or CustomerCartLine or Order or OrderItem).ToList())
                        entry.State = EntityState.Detached;
                }
            }
            return new(CartResult.Busy, "Your cart is busy. Please try again in a moment.");
        });
    }

    private static void Touch(CustomerCart cart, DateTime now)
    { cart.Revision = Guid.NewGuid(); cart.UpdatedAt = now; }
    private static CartOperation Invalid(string text) => new(CartResult.Invalid, text);
    private static CartOperation Changed() => new(CartResult.Changed,
        "Your cart changed in another tab. Review the latest cart before continuing.");
    private static CartOperation Expired() => new(CartResult.Changed,
        "That confirmation expired. Review your cart and click Place order again.");

    private static CartOperation ValidateForCheckout(CartView view)
    {
        if (view.Lines.Count == 0) return Invalid("Add a dish before placing your order.");
        if (view.HasUnavailable) return new(CartResult.Unavailable, "Remove unavailable dishes before placing your order.");
        if (view.OrderType == OrderType.DineIn && !view.ReservationId.HasValue)
            return Invalid("Dine-in needs a reservation. Book a table or choose Takeaway.");
        if (!view.ReservationValid) return Invalid("The selected reservation is no longer eligible. Choose another reservation or switch to Takeaway.");
        if (!view.SessionValid) return Invalid(view.SessionError ??
            "Your reservation's dining session is not ready. Please ask staff for help.");
        if (view.Totals.Total > 99999999.99m) return Invalid("This order exceeds the supported total. Please reduce the quantities.");
        return new(CartResult.Success, "Ready to confirm.");
    }

    private static string Stamp(CartView view)
    {
        var reservation = view.OrderType == OrderType.DineIn
            ? view.Reservations.SingleOrDefault(row => row.Id == view.ReservationId) : null;
        string json = JsonSerializer.Serialize(new
        {
            view.Revision,
            view.OrderType,
            view.ReservationId,
            view.DiningSessionId,
            Session = view.SelectedSession,
            view.Notes,
            view.Totals,
            Reservation = reservation,
            Lines = view.Lines.Select(line => new {
                line.Id,
                line.MenuItemId,
                line.Quantity,
                line.Notes,
                line.UnitPrice,
                line.IsAvailable
            }).OrderBy(line => line.Id)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private sealed record QuotePayload(string UserId, Guid Revision, string OrderNo,
        string Stamp, DateTimeOffset ExpiresAt);
}
