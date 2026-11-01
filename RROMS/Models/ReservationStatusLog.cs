// Owner: Member 2 — US17 (status history) / used by US13 cancel.
// One row per status change. Never edited, never deleted: it is the timestamped record US17 asks for.
namespace RROMS.Models;

public class ReservationStatusLog
{
    public int Id { get; set; }

    public int ReservationId { get; set; }
    public Reservation? Reservation { get; set; }

    public ReservationStatus FromStatus { get; set; }
    public ReservationStatus ToStatus { get; set; }

    // Who did it. Null when the system did it (e.g. automatic No-show).
    public string? ChangedByUserId { get; set; }

    // "Customer", "Staff", "Admin" or "System"
    public string ChangedByRole { get; set; } = "System";

    public DateTime ChangedAt { get; set; }

    public string? Note { get; set; }
}
