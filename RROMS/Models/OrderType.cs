using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 3 (Menu, Orders & Recommendations)
    // Saved in the database as the WORD (e.g. "Paid"), not a number.
    // Never rename or delete an existing value (old records keep the old word).
    // Adding a new value anywhere in the list is safe.
    public enum OrderType
    {
        [Display(Name = "Dine-in")] DineIn,
        Takeaway
    }
}
