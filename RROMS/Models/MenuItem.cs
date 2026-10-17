using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 3 (Menu, Orders & Recommendations)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class MenuItem
    {
        public int Id { get; set; }

        public int CategoryId { get; set; }
        public MenuCategory Category { get; set; } = null!;

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Range(0.01, 10000)]
        public decimal Price { get; set; }

        [StringLength(300)]
        public string? ImageUrl { get; set; }

        public bool IsAvailable { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ICollection<Promotion> Promotions { get; set; } = new List<Promotion>();
    }
}
