using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 3 (Menu, Orders & Recommendations)
    // Used by Members 3, 4 and 5.
    // Saved in the database as the WORD (e.g. "Paid"), not a number.
    // Never rename or delete an existing value (old records keep the old word).
    // Adding a new value anywhere in the list is safe.
    public enum OrderStatus
    {
        Pending,
        Preparing,
        [Display(Name = "Ready to Serve")] ReadyToServe,
        Served,
        Completed,
        Cancelled
    }
}
