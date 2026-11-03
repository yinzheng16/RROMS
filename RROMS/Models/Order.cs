using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 3 (Menu, Orders & Recommendations)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class Order
    {
        public int Id { get; set; }

        [Required, StringLength(20)]
        public string OrderNo { get; set; } = string.Empty;

        [Required]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        // Null for takeaway orders that are not linked to a reservation.
        public int? ReservationId { get; set; }
        public Reservation? Reservation { get; set; }

        // Added by Member 2 (dine-in workflow decision, 27 Oct): the dining session this order belongs to.
        // Null for takeaway orders and for dine-in orders placed before the guests are seated.
        public int? DiningSessionId { get; set; }
        public DiningSession? DiningSession { get; set; }

        // US32 (Member 4): Multiple orders can belong to one combined bill.
        public int? BillId { get; set; }
        public Bill? Bill { get; set; }

        public OrderType OrderType { get; set; } = OrderType.DineIn;

        public OrderStatus Status { get; set; } = OrderStatus.Pending;

        public decimal Subtotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal TotalAmount { get; set; }
        // Charge rates used when the order was placed, e.g. 6.00 = 6%.
        // Null for orders placed before these were saved; pages then show amounts without a %.
        [Precision(5, 2)]
        public decimal? TaxRatePercent { get; set; }

        [Precision(5, 2)]
        public decimal? ServiceChargePercent { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
        public Feedback? Feedback { get; set; }
    }
}