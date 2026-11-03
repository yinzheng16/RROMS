using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 3 (Menu, Orders & Recommendations)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class Promotion
    {
        public int Id { get; set; }

        public int MenuItemId { get; set; }
        public MenuItem MenuItem { get; set; } = null!;

        [Required, StringLength(100)]
        public string Title { get; set; } = string.Empty;

        // Percentage promotions: the discount. Fixed-price promotions: the matching
        // percentage when the promotion was saved (kept for older readers only;
        // prices always come from PromotionRules, which uses PromoPrice).
        [Range(0.01, 100)]
        public decimal DiscountPercent { get; set; }

        // US29: fixed promotional price (e.g. RM 9.90). Null = percentage promotion.
        [Range(0.01, 10000)]
        public decimal? PromoPrice { get; set; }

        // Dates only: the promotion runs from the start of StartDate to the end of EndDate.
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
