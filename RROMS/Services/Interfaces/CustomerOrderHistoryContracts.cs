using RROMS.Models;

namespace RROMS.Services.Interfaces;

// Extension to the existing registered cart service; no new DI registration.
public partial interface ICustomerCartService
{
    Task<CustomerOrderHistory> GetOrderHistoryAsync(string userId, HistoryFilter filter,
        CancellationToken cancellationToken = default);
    Task<HistoryOrderDetails?> GetHistoryOrderAsync(string userId, int orderId,
        CancellationToken cancellationToken = default);
    Task<HistoryReorderPreview> PrepareReorderAsync(string userId, int orderId,
        IReadOnlyList<HistoryReorderChoice> selections, bool copyDishNotes,
        CancellationToken cancellationToken = default);
    Task<CartOperation> ConfirmReorderAsync(string userId, string? token,
        CancellationToken cancellationToken = default);
}

public sealed class HistoryFilter
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Dining { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Sort { get; set; }
    public int PageNumber { get; set; } = 1;
    public bool ShowAll { get; set; }
}
public sealed record HistoryDishPreview(string Name, int Quantity, decimal SavedLineTotal = 0m);
public sealed record HistoryOrderRow(int Id, string OrderNo, OrderStatus Status, OrderType Dining,
    DateTime CreatedAt, int Portions, int DishCount, decimal Total,
    IReadOnlyList<HistoryDishPreview> Dishes)
{
    public decimal Subtotal { get; init; }
    public decimal ServiceCharge { get; init; }
    public decimal Tax { get; init; }
}
public sealed record CustomerOrderHistory(IReadOnlyList<HistoryOrderRow> Orders,
    int MatchingCount, int AllOrdersCount, int PageNumber, HistoryFilter Filter,
    bool Forbidden = false, string? Error = null)
{
    public IReadOnlyDictionary<string, int> StatusCounts { get; init; } = new Dictionary<string, int>();
    public const int PageSize = 6;
    public int TotalPages => Filter.ShowAll ? 1 : Math.Max(1, (int)Math.Ceiling(MatchingCount / (double)PageSize));
    public int First => MatchingCount == 0 ? 0 : Filter.ShowAll ? 1 : (PageNumber - 1) * PageSize + 1;
    public int Last => Filter.ShowAll ? MatchingCount : Math.Min(PageNumber * PageSize, MatchingCount);
}
public sealed record HistoryOrderLine(int Id, int MenuItemId, string Code, string Name,
    string? ImageUrl, int Quantity, decimal SavedUnitPrice, string? Notes,
    bool AvailableNow, decimal? CurrentUnitPrice, decimal? NormalUnitPrice = null)
{
    // US29: promotion saving on this line (0 for orders placed before US29).
    public decimal Saving => NormalUnitPrice is decimal normal && normal > SavedUnitPrice
        ? (normal - SavedUnitPrice) * Quantity : 0m;
    public bool CanReorder => Quantity > 0 && AvailableNow;
    public bool PriceChanged => CurrentUnitPrice.HasValue && CurrentUnitPrice != SavedUnitPrice;
}
public sealed record HistoryOrderDetails(int Id, string OrderNo, OrderStatus Status, OrderType Dining,
    DateTime CreatedAt, DateTime? UpdatedAt, string? Notes, string? ReservationReference,
    DateTime? ReservedAt, int? Guests, IReadOnlyList<HistoryOrderLine> Lines,
    decimal Subtotal, decimal ServiceCharge, decimal Tax, decimal Total)
{
    public decimal Savings => Lines.Sum(line => line.Saving);
}
public sealed class HistoryReorderChoice
{
    public int OrderItemId { get; set; }
    public bool Selected { get; set; }
    public int Quantity { get; set; }
}
public sealed record HistoryReorderLine(int OrderItemId, int MenuItemId, string Name,
    int Quantity, decimal SavedUnitPrice, decimal CurrentUnitPrice, string? Notes,
    bool Available, string? SkipReason)
{
    public bool PriceChanged => Available && SavedUnitPrice != CurrentUnitPrice;
    public decimal NewLineTotal => Available ? CurrentUnitPrice * Quantity : 0m;
}
public sealed record HistoryReorderPreview(CartResult Status, string Message, int OrderId,
    string OrderNo, IReadOnlyList<HistoryReorderLine> Lines, int ExistingCartQuantity,
    string? Token = null)
{
    public bool Ready => Status == CartResult.Success && Token is not null;
    public int AddQuantity => Lines.Where(line => line.Available).Sum(line => line.Quantity);
    public int SkipQuantity => Lines.Where(line => !line.Available).Sum(line => line.Quantity);
    public decimal ItemsSubtotal => Lines.Sum(line => line.NewLineTotal);
}
