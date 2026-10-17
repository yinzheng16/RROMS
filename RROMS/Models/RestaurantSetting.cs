using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 5 (Feedback, Reports & Dashboard)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    // Single-row table holding restaurant-wide settings.
    public class RestaurantSetting
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string RestaurantName { get; set; } = string.Empty;

        [StringLength(20)]
        public string? Phone { get; set; }

        [StringLength(100)]
        public string? Email { get; set; }

        [StringLength(300)]
        public string? Address { get; set; }

        [Range(0, 100)]
        public decimal TaxRatePercent { get; set; }

        [Range(0, 100)]
        public decimal ServiceChargePercent { get; set; }
    }
}
