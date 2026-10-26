using RROMS.Models;

namespace RROMS.Services.Interfaces;

// Owner: Member 3 / US25. DTOs and partial interface only; no database changes.
public partial interface IOrderService
{
    Task<StaffOrderQueue> GetStaffOrderQueueAsync(string? search = null,
        string? dining = null, int page = 1, CancellationToken cancellationToken = default, string? sort = null, bool showAll = false);
    Task<StaffQueueSignal> GetStaffQueueSignalAsync(CancellationToken cancellationToken = default);
    Task<StaffOrderDetails?> GetStaffOrderDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<StaffAcceptResult> AcceptStaffOrderAsync(int id, string staffUserId,
        CancellationToken cancellationToken = default);
}

public sealed record StaffOrderRow(int Id, string OrderNo, string CustomerName,
    OrderType Dining, DateTime CreatedAt, int Portions, decimal Total, DateTime? UpdatedAt,
    IReadOnlyList<StaffOrderPreview>? DishPreview = null, bool HasNotes = false, int DishLineCount = 0);

public sealed record StaffOrderPreview(string Name, int Quantity);

public sealed record StaffQueueSignal(string Stamp, int Pending, int DineIn, int Takeaway, int NewestId);

public sealed record StaffOrderQueue(IReadOnlyList<StaffOrderRow> Rows, StaffQueueSignal Signal,
    int MatchingCount, int PageNumber, string Search, string Dining, DateTime ServerNow, string Sort = "newest", bool ShowAll = false)
{
    public const int PageSize = 6;
    public int TotalPages => ShowAll ? 1 : Math.Max(1, (int)Math.Ceiling(MatchingCount / (double)PageSize));
    public int First => MatchingCount == 0 ? 0 : ShowAll ? 1 : (PageNumber - 1) * PageSize + 1;
    public int Last => ShowAll ? MatchingCount : Math.Min(PageNumber * PageSize, MatchingCount);
}

public sealed record StaffOrderDish(int Id, int MenuItemId, string Code, string Name,
    string? ImageUrl, int Quantity, decimal UnitPrice, string? Notes);
public sealed record StaffOrderBooking(string Reference, DateTime ReservedAt, int Guests,
    string Status, string? TableNumber);
public sealed record StaffOrderDetails(int Id, string OrderNo, string CustomerName,
    OrderType Dining, OrderStatus Status, DateTime CreatedAt, DateTime ServerNow,
    IReadOnlyList<StaffOrderDish> Dishes, string? Notes, StaffOrderBooking? Booking,
    decimal Subtotal, decimal Tax, decimal ServiceCharge, decimal Total)
{
    public int Portions => Dishes.Sum(dish => dish.Quantity);
}

public enum StaffAcceptStatus { Success, NotFound, AlreadyChanged, Forbidden }
public sealed record StaffAcceptResult(StaffAcceptStatus Status, string Message);

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
