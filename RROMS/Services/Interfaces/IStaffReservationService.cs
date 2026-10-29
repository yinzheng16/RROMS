// Owner: Member 2 — US16 (staff "Today's reservations" page).
using RROMS.Models;

namespace RROMS.Services.Interfaces;

// One booking as the staff page shows it.
public record StaffReservationRow(
    int Id,
    string ReferenceNo,
    ReservationStatus Status,
    DateTime ReservedAt,
    DateTime EndsAt,
    int Guests,
    string GuestName,
    string? Phone,
    int? TableId,
    string? TableNumber,
    string? AreaName,
    string? TableTypeName,
    string? SpecialRequest,
    decimal MinimumSpend,
    bool MinimumSpendAccepted,
    DateTime CreatedAt,
    // The moment an unconfirmed / unseated booking is released (ReservedAt + grace).
    DateTime AutoCancelAt,
    // Pending or Confirmed, and the booking time has already passed.
    bool IsLate,
    int MinutesLate,
    // Pending only: whole minutes until the automatic cancel (never negative).
    int? AutoCancelMinutes,
    bool CanConfirm);

public record StaffTableLane(int TableId, string TableNumber, int Capacity, string? TypeName = null);

public record StaffAreaLanes(string AreaName, IReadOnlyList<StaffTableLane> Tables);

// Opening hours of the chosen day (used to draw the timeline).
public record StaffDayHours(
    bool IsClosed,
    string? ClosedReason,
    DateTime? Open,
    DateTime? Close,
    DateTime? BreakStart,
    DateTime? BreakEnd);

public record StaffDayCounts(
    int All, int Pending, int Confirmed, int Seated, int Late, int Completed, int Cancelled,
    int ActiveBookings, int ActiveGuests);

// A day that still has unconfirmed bookings (shown in the banner whichever day is open).
public record StaffPendingDay(DateTime Date, int Count);

public record StaffDayBoard(
    DateTime Date,
    DateTime Now,
    bool IsToday,
    bool IsPast,
    StaffDayHours Hours,
    IReadOnlyList<StaffReservationRow> Rows,
    IReadOnlyList<StaffAreaLanes> Areas,
    StaffDayCounts Counts,
    // Earliest upcoming automatic cancel among Pending bookings (today only).
    DateTime? NextAutoCancel,
    // Every upcoming day that has Pending bookings, earliest first.
    IReadOnlyList<StaffPendingDay> PendingDays);

public record StaffStatusLogItem(DateTime At, string From, string To, string Role, string? Note);

public record StaffReservationDetails(
    StaffReservationRow Row,
    string? Email,
    // Read-only summary from the customer's profile (allergies, diet). Not the booking's own request.
    string? DiningPreferences,
    DateTime? ConfirmedAt,
    IReadOnlyList<StaffStatusLogItem> History);

public record StaffActionResult(bool Success, string Code, string Message);

public interface IStaffReservationService
{
    // Releases overdue bookings first, then returns everything for one day.
    Task<StaffDayBoard> GetDayBoardAsync(DateTime date, CancellationToken cancellationToken = default);

    Task<StaffReservationDetails?> GetDetailsAsync(int reservationId, CancellationToken cancellationToken = default);

    // Pending -> Confirmed. role is "Staff" or "Admin" (written to the status history).
    Task<StaffActionResult> ConfirmAsync(int reservationId, string staffUserId, string role,
        CancellationToken cancellationToken = default);
}
