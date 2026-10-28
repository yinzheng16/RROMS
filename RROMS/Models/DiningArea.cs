using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 2 (Tables & Reservations)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class DiningArea
    {
        public int Id { get; set; }

        [Required, StringLength(50)]
        public string Name { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Description { get; set; }
        [StringLength(40)]
        public string IconName { get; set; } = "layout-grid";
        public bool IsActive { get; set; } = true;

        public bool IsOccupied { get; set; }

        public ICollection<RestaurantTable> Tables { get; set; } = new List<RestaurantTable>();
    }
}
