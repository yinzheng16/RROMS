using RROMS.Models;
namespace RROMS.Services.Interfaces;

public partial interface IOrderService
{
    Task<StaffWorkflowBoard> GetStaffWorkflowBoardAsync(string? status = null, string? search = null,
        string? dining = null, string? sort = null, int page = 1, CancellationToken cancellationToken = default);
    Task<StaffWorkflowSignal> GetStaffWorkflowSignalAsync(CancellationToken cancellationToken = default);
    Task<StaffWorkflowDetails?> GetStaffWorkflowDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<StaffStatusResult> ChangeStaffOrderStatusAsync(int id, OrderStatus expectedStatus,
        OrderStatus targetStatus, string? reason, string staffUserId, CancellationToken cancellationToken = default);
}
public sealed record StaffWorkflowSignal(string Stamp, int Active, int DineIn, int Takeaway, int Pending);
public sealed record StaffWorkflowBoard(OrderStatus Status, string Search, string Dining, string Sort,
    int PageNumber, int RemainingCount, IReadOnlyDictionary<OrderStatus, int> Counts,
    IReadOnlyList<StaffWorkflowCard> Rows,
    StaffWorkflowSignal Signal, DateTime ServerNow)
{
    // 12 fills complete rows in both layouts: 3 columns (drawer closed) and 2 columns (drawer open).
    public const int PageSize = 12;
    // US26 UI: page 0 shows every remaining order of the selected stage on one page.
    public const int ShowAllPage = 0;
    public bool ShowAll => PageNumber == ShowAllPage;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(RemainingCount / (double)PageSize));
    public int MatchingCount => Counts.GetValueOrDefault(Status);
    // Set when the search text is a complete order number (any status), so the page can jump to that order.
    public int? ExactOrderId { get; init; }
    public OrderStatus? ExactOrderStatus { get; init; }
}
public sealed record StaffWorkflowCard(StaffOrderRow Order, OrderStatus Status);
public sealed record StaffWorkflowEvent(OrderStatus From, OrderStatus To, DateTime ChangedAt,
    string StaffName, string StaffRole, string? Reason);
public sealed record StaffWorkflowDetails(StaffOrderDetails Order,
    IReadOnlyList<StaffWorkflowEvent> History, string? CancellationBlocked)
{
    public bool HasEarlierHistoryGap => History.Count == 0 ? Order.Status != OrderStatus.Pending
        : History[0].From != OrderStatus.Pending;
}
public enum StaffStatusOutcome { Success, NotFound, Conflict, Invalid, Forbidden, Locked }
public sealed record StaffStatusResult(StaffStatusOutcome Outcome, string Message);
