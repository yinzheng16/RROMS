using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 3 (Menu, Orders & Recommendations)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class OrderItem
    {
        public int Id { get; set; }

        public int OrderId { get; set; }
        public Order Order { get; set; } = null!;

        public int MenuItemId { get; set; }
        public MenuItem MenuItem { get; set; } = null!;

        [Range(1, 100)]
        public int Quantity { get; set; }

        // Price at the time of ordering, so later price changes do not alter past bills.
        public decimal UnitPrice { get; set; }

        [StringLength(200)]
        public string? Notes { get; set; }
    }
}
