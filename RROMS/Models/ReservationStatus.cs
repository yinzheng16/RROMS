using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 2 (Tables & Reservations)
    // Used by Members 2, 3 and 5.
    // Saved in the database as the WORD (e.g. "Paid"), not a number.
    // Never rename or delete an existing value (old records keep the old word).
    // Adding a new value anywhere in the list is safe.
    public enum ReservationStatus
    {
        Pending,
        Confirmed,
        Seated,
        Completed,
        Cancelled,
        [Display(Name = "No-show")] NoShow
    }
}
