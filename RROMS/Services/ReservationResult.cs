// Owner: Member 2 (Tables & Reservations)
namespace RROMS.Services;

/// <summary>Result of a Member 2 service action: success, or a message that is shown to the user.</summary>
public record ReservationResult(bool Success, string? Error = null)
{
    public static ReservationResult Ok() => new(true);
    public static ReservationResult Fail(string error) => new(false, error);
}
