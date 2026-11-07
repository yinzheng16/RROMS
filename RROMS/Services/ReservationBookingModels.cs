// Owner: Member 2 (Tables & Reservations) - US12 / US14
namespace RROMS.Services;

// One place for every booking rule, so the page, the service and the tests agree.
public static class BookingRules
{
    public const int DurationMinutes = 90;      // how long a table is held (same default as Reservation.DurationMinutes)
    public const int SlotMinutes = 30;          // time choices every 30 minutes
    public const int MinLeadMinutes = 60;       // book at least 1 hour ahead
    public const int MaxAdvanceDays = 60;       // book at most 60 days ahead
    public const int MaxGuests = 100;           // largest party in one booking: new, changed by the customer, changed by staff
    public const int TurnoverMinutes = 15;      // gap kept before and after another booking on the same table
    public const int MaxActiveBookings = 10;     // upcoming Pending / Confirmed bookings per customer
    public const int NoShowGraceMinutes = 15;   // held for 15 minutes from the booking time
    public const int SeatEarlyMinutes = 60;     // staff can seat a party at most 1 hour before the booking time
}

public enum TypeAvailability { Available, FullyBooked, NotSuitable, Unavailable }

public record BookingSlot(string Time, string Label);

public record TypeOption(
    int Id,
    string Name,
    string? JapaneseName,
    string? Description,
    string? PhotoPath,
    int MinGuests,
    int MaxGuests,
    decimal MinimumSpend,
    bool BelowMinimum,
    TypeAvailability Status,
    string? Reason,
    int TablesLeft,
    string? AreaName = null,
    List<TimeSuggestion>? Suggestions = null,
    string? AreaIcon = null);

public record TypeOptionsResult(string? Error, List<TypeOption> Options);

public record CreateReservationRequest(
    string UserId,
    DateTime ReservedAt,
    int Guests,
    int TableTypeId,
    string? SpecialRequest,
    bool AcceptedMinimumSpend,
    decimal ExpectedMinimumSpend,
    string? Token);

public record ReservationTicket(
    string ReferenceNo,
    DateTime ReservedAt,
    int Guests,
    string TypeName,
    string? TypeJapaneseName,
    string? AreaName,
    string? SpecialRequest,
    decimal MinimumSpend,
    string Status,
    string? AreaIcon = null,
    int ReservationId = 0);

// Code lets the page decide where to send the customer back to:
// slot / guests -> step 1, type / full / spend -> step 2, others stay on the review step.
public record BookingResult(bool Success, string? Code, string? Error, ReservationTicket? Ticket)
{
    public static BookingResult Ok(ReservationTicket ticket) => new(true, null, null, ticket);
    public static BookingResult Fail(string code, string error) => new(false, code, error, null);
}
