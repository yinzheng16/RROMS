using System.ComponentModel.DataAnnotations;
namespace RROMS.Models;

// Member 3 / US26. Append-only: an actual, successful status change, never an attempted action.
public class OrderStatusLog
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public OrderStatus FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    [Required, StringLength(450)] public string ChangedByUserId { get; set; } = "";
    [Required, StringLength(256)] public string ChangedByName { get; set; } = "";
    [Required, StringLength(20)] public string ChangedByRole { get; set; } = "";
    public DateTime ChangedAt { get; set; }
    [StringLength(250)] public string? Reason { get; set; }
}
