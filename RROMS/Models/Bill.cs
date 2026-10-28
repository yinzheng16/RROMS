using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 4 (US32 - Generate Bill)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class Bill
    {
        public int Id { get; set; }

        // Dine-in: the dining session this combined bill belongs to
        // (Member 2's DiningSession; at most one bill per session).
        // Null for takeaway bills.
        public int? DiningSessionId { get; set; }
        public DiningSession? DiningSession { get; set; }

        // One combined bill can contain multiple orders.
        public ICollection<Order> Orders { get; set; }
            = new List<Order>();

        [Range(0, double.MaxValue)]
        public decimal Subtotal { get; set; }

        [Range(0, double.MaxValue)]
        public decimal TaxAmount { get; set; }

        [Range(0, double.MaxValue)]
        public decimal ServiceCharge { get; set; }

        [Range(0, double.MaxValue)]
        public decimal TotalAmount { get; set; }

        public BillStatus Status { get; set; } = BillStatus.Draft;

        public DateTime? ConfirmedAt { get; set; }

        [StringLength(450)]
        public string? ConfirmedByUserId { get; set; }

        // Payments associated with this bill.
        public ICollection<Payment> Payments { get; set; }
            = new List<Payment>();
    }
}
