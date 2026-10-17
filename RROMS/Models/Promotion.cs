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

        [Range(1, 100)]
        public decimal DiscountPercent { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
