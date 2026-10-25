using RROMS.Models;

namespace RROMS.Services.Interfaces;

// Owner: Member 3. All cart, reservation and order database work stays in services.
public interface ICustomerCartService
{
    Task<CartView> GetAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> CountAsync(string userId, CancellationToken cancellationToken = default);
    Task<CartOperation> AddAsync(string userId, int menuItemId, int quantity, string? notes,
        CancellationToken cancellationToken = default);
    Task<CartOperation> UpdateAsync(string userId, CartUpdate input, Guid? removeLineId = null,
        CancellationToken cancellationToken = default);
    Task<CheckoutQuote> PrepareAsync(string userId, Guid revision,
        CancellationToken cancellationToken = default);
    Task<CartOperation> PlaceAsync(string userId, string? token,
        CancellationToken cancellationToken = default);
    Task<OrderReceipt?> ReceiptAsync(string userId, int orderId,
        CancellationToken cancellationToken = default);
}

public sealed record CartLineView(Guid Id, int MenuItemId, string Code, string Name,
    string? ImageUrl, int Quantity, string? Notes, decimal UnitPrice, bool IsAvailable)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

public sealed record CartReservation(int Id, string ReferenceNo, DateTime ReservedAt,
    int Guests, ReservationStatus Status);
public sealed record CartTotals(decimal Subtotal, decimal TaxRate, decimal Tax,
    decimal ServiceRate, decimal ServiceCharge, decimal Total);
public sealed record CartView(Guid Revision, IReadOnlyList<CartLineView> Lines,
    OrderType OrderType, int? ReservationId, string? Notes, CartTotals Totals,
    IReadOnlyList<CartReservation> Reservations)
{
    public int Quantity => Lines.Sum(line => line.Quantity);
    public bool HasUnavailable => Lines.Any(line => !line.IsAvailable);
    public bool ReservationValid => OrderType == OrderType.Takeaway || !ReservationId.HasValue ||
        Reservations.Any(reservation => reservation.Id == ReservationId);
    public static CartView Empty { get; } = new(Guid.Empty, Array.Empty<CartLineView>(),
        OrderType.DineIn, null, null, new(0, 0, 0, 0, 0, 0), Array.Empty<CartReservation>());
}

// Input DTOs deliberately contain no price, user ID, status or total fields.
public sealed class CartUpdate
{
    public Guid Revision { get; set; }
    public List<CartLineInput> Lines { get; set; } = new();
    public OrderType OrderType { get; set; } = OrderType.DineIn;
    public int? ReservationId { get; set; }
    public string? Notes { get; set; }
}
public sealed class CartLineInput
{
    public Guid Id { get; set; }
    public int Quantity { get; set; }
    public string? Notes { get; set; }
}

public enum CartResult { Success, Forbidden, Invalid, Unavailable, Changed, Busy }
public sealed record CartOperation(CartResult Status, string Message, int? OrderId = null)
{
    public bool Succeeded => Status == CartResult.Success;
}
public sealed record CheckoutQuote(CartResult Status, string Message, CartView Cart,
    string? Token = null)
{
    public bool Succeeded => Status == CartResult.Success;
}
public sealed record OrderReceipt(int Id, string OrderNo, OrderStatus Status,
    OrderType OrderType, DateTime CreatedAt, string? Notes,
    string? ReservationReference, DateTime? ReservedAt, int? Guests,
    IReadOnlyList<CartLineView> Lines, decimal Subtotal, decimal Tax,
    decimal ServiceCharge, decimal Total);
