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
    string? TableNumber = null,
    string? AreaIcon = null);

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

// US18: one step of a booking's life ("Confirmed", at 4:12 PM, by "Our team").
// Note is a short reason shown beside the step, such as "Modified: date, guests".
public sealed record MyStatusStep(string Status, DateTime At, string By, string? Note = null);

// US18: a booking that is over (Completed, Cancelled or No-show), with the steps it went through.
public sealed record MyHistoryItem(
    int Id, string ReferenceNo, DateTime ReservedAt, int Guests, string TableTypeName, string? JapaneseName,
    string? AreaName, string? TableNumber, string Status, decimal MinimumSpend, string? SpecialRequest,
    DateTime BookedAt, IReadOnlyList<MyStatusStep> Steps,
    string? AreaIcon = null);

// US18: the live status of one booking, small enough to ask for every few seconds.
public sealed record MyStatusSnapshot(int Id, string Status, string? TableNumber);

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

    // US18: my finished bookings (Completed, Cancelled, No-show), newest first, each with its status steps.
    Task<IReadOnlyList<MyHistoryItem>> GetMyHistoryAsync(
        string userId, CancellationToken cancellationToken = default);

    // US18: the current status of my bookings, used by the page to refresh the badges without a reload.
    Task<IReadOnlyList<MyStatusSnapshot>> GetMyStatusesAsync(
        string userId, CancellationToken cancellationToken = default);
}
