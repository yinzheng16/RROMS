// Owner: Member 2 (Tables & Reservations)
using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

public enum TableType
{
    [Display(Name = "Counter seat")] Counter,
    [Display(Name = "Table for two")] Double,
    [Display(Name = "Table (3-6 guests)")] Standard,
    [Display(Name = "VIP room")] Vip,
    [Display(Name = "VVIP room")] Vvip
}
