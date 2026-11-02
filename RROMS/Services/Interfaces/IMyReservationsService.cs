// Owner: Member 2 — US13. New interface; existing IReservationService stays intact.
namespace RROMS.Services.Interfaces;

public sealed record MyReservationItem(
    int Id,
    string ReferenceNo,
    DateTime ReservedAt,
    int Guests,
    string TableTypeName,
    string? JapaneseName,
    string? SpecialRequest,
    decimal MinimumSpend,
    bool MinimumSpendWasAccepted,
    string Status,
    bool CanCancel,
    string? AreaName = null,
    string? TableNumber = null);

public sealed record MyReservationCancelResult(bool Success, string Code, string Message);

// US15: another table type that can seat the new party at the chosen time.
public sealed record MyChangeTypeOption(
    int Id, string Name, string? JapaneseName, int MinGuests, int MaxGuests, decimal MinimumSpend, int TablesLeft);

// US15: the answer to "can I change my booking like this?". Nothing is saved.
// BackToPending = the change needs staff to confirm again. TypeOptions is set when the party no longer
// fits the booked table type and the customer must pick another one.
public sealed record MyChangeCheck(
    bool Valid, bool Available, string? Message, List<TimeSuggestion> Suggestions,
    bool BackToPending = false, List<MyChangeTypeOption>? TypeOptions = null);

// US15: Code is "changed", "unchanged", "not_found", "too_late", "status_changed", "slot", "guests",
// "type", "overlap", "limit", "full", "spend" or "busy". Message is safe to show to the customer.
public sealed record MyReservationChangeResult(
    bool Success, string Code, string Message,
    string? ReferenceNo = null, DateTime? NewReservedAt = null, int? NewGuests = null, bool BackToPending = false,
    bool RequestChanged = false);

public interface IMyReservationsService
{
    Task<IReadOnlyList<MyReservationItem>> GetMyUpcomingReservationsAsync(
        string userId, CancellationToken cancellationToken = default);

    Task<MyReservationCancelResult> CancelMyReservationAsync(
        string userId, int reservationId, CancellationToken cancellationToken = default);

    // US15: can this booking be changed to the new date, time, guests (and table type)? Nothing is saved.
    Task<MyChangeCheck> CheckMyChangeAsync(
        string userId, int reservationId, DateTime start, int guests, int? tableTypeId,
        CancellationToken cancellationToken = default);

    // US15: change the date, time, guests, table type or special request of my booking. The reference number stays.
    // specialRequest = the full text the customer wants to keep (null = leave it as it is).
    Task<MyReservationChangeResult> ChangeMyReservationAsync(
        string userId, int reservationId, DateTime start, int guests, int? tableTypeId,
        bool acceptedMinimumSpend, decimal expectedMinimumSpend, string? specialRequest = null,
        CancellationToken cancellationToken = default);
}