using RROMS.Models;

namespace RROMS.Services.Interfaces;

// Owner: Member 3 / US25-US26. Shared order-card and order-detail DTOs for the staff order board.
public partial interface IOrderService
{
    Task<StaffOrderDetails?> GetStaffOrderDetailsAsync(int id, CancellationToken cancellationToken = default);
}

public sealed record StaffOrderRow(int Id, string OrderNo, string CustomerName,
    OrderType Dining, DateTime CreatedAt, int Portions, decimal Total, DateTime? UpdatedAt,
    IReadOnlyList<StaffOrderPreview>? DishPreview = null, bool HasNotes = false, int DishLineCount = 0,
    int? DiningSessionId = null, string? TableNumber = null);

public sealed record StaffOrderPreview(string Name, int Quantity);

public sealed record StaffOrderDish(int Id, int MenuItemId, string Code, string Name,
    string? ImageUrl, int Quantity, decimal UnitPrice, string? Notes);
public sealed record StaffOrderBooking(string Reference, DateTime ReservedAt, int Guests,
    string Status, string? TableNumber);
public sealed record StaffOrderDetails(int Id, string OrderNo, string CustomerName,
    OrderType Dining, OrderStatus Status, DateTime CreatedAt, DateTime ServerNow,
    IReadOnlyList<StaffOrderDish> Dishes, string? Notes, StaffOrderBooking? Booking,
    decimal Subtotal, decimal Tax, decimal ServiceCharge, decimal Total,
    string? CustomerId = null, string? DiningPreferences = null,
    int? DiningSessionId = null, string? TableNumber = null)
{
    public int Portions => Dishes.Sum(dish => dish.Quantity);
    public decimal? TaxRatePercent { get; init; }
    public decimal? ServiceChargePercent { get; init; }
}

public static class StaffQueueRules
{
    public static string Search(string? value)
    {
        string term = value?.Trim() ?? "";
        return term.Length <= 80 ? term : term[..80];
    }
    public static string Sort(string? value) => value == "oldest" ? "oldest" : "newest";
    public static string Dining(string? value) => value is "DineIn" or "Takeaway" ? value : "All";
    public static string DiningLabel(OrderType type) => type == OrderType.Takeaway ? "Takeaway" : "Dine-in";
    public static long WaitSeconds(DateTime created, DateTime now)
        => Math.Max(0, (long)(now - created).TotalSeconds);
    public static string WaitLabel(long seconds)
    {
        long minutes = Math.Max(0, seconds / 60);
        if (minutes < 1) return "Just received";
        if (minutes < 60) return $"{minutes} min";
        long hours = minutes / 60, remainder = minutes % 60;
        return remainder == 0 ? $"{hours} hr" : $"{hours} hr {remainder} min";
    }
    public static string WaitClass(long seconds) => seconds >= 1200 ? "long" : seconds >= 600 ? "warn" : "normal";
}
