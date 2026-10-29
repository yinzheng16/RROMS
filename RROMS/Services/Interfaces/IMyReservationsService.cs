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

public interface IMyReservationsService
{
    Task<IReadOnlyList<MyReservationItem>> GetMyUpcomingReservationsAsync(
        string userId, CancellationToken cancellationToken = default);

    Task<MyReservationCancelResult> CancelMyReservationAsync(
        string userId, int reservationId, CancellationToken cancellationToken = default);
}