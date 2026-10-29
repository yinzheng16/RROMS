using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

// Owner: Member 3 (US24). A customer's draft survives refreshes and app restarts.
public class CustomerCart
{
    [Key, StringLength(450)]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    [ConcurrencyCheck]
    public Guid Revision { get; set; } = Guid.NewGuid();
    public OrderType OrderType { get; set; } = OrderType.DineIn;
    public int? ReservationId { get; set; }
    // Retained for existing drafts/schema; checkout resolves sessions from ReservationId only.
    public int? DiningSessionId { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public ICollection<CustomerCartLine> Lines { get; set; } = new List<CustomerCartLine>();
}
