// Owner: Member 2 (Tables & Reservations) - US12 "set your own time"
namespace RROMS.Services;

public static class BookingTimeRules
{
    // The time buttons stay every 30 minutes (BookingRules.SlotMinutes),
    // but a customer may type or step to any time in 5-minute steps.
    public const int ExactStepMinutes = 5;

    // Besides the total limit (BookingRules.MaxActiveBookings), a customer may hold at most
    // MaxPerWindow upcoming bookings inside any RollingWindowDays days in a row.
    public const int RollingWindowDays = 7;
    public const int MaxPerWindow = 3;

    // The kitchen stops taking orders this many minutes before closing, or before a break.
    public const int LastOrderMinutes = 30;
}

// Kind: "earlier" and "later" (same sitting) or "nextday".
public record TimeSuggestion(string Date, string Time, string Label, string Kind);

// Valid = the time itself is allowed (open, in the future, not in a break...). Message explains why not.
public record TimeCheckResult(bool Valid, bool Available, string? Message, List<TimeSuggestion> Suggestions);