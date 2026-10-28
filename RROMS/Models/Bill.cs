using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 4 (US32 - Generate Bill)
    public class Bill
    {
        public int Id { get; set; }

        // Null for takeaway bills.
        public int? DiningSessionId { get; set; }

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

        public string? ConfirmedByUserId { get; set; }

        // Payments associated with this bill.
        public ICollection<Payment> Payments { get; set; }
            = new List<Payment>();
    }
}