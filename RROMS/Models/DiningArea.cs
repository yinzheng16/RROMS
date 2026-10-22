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

        // Auto = calculate from tables
        // Manual = use ManualStatus
        public DiningAreaStatusMode StatusMode { get; set; }
            = DiningAreaStatusMode.Auto;

        public DiningAreaManualStatus? ManualStatus { get; set; }
        public ICollection<RestaurantTable> Tables { get; set; } = new List<RestaurantTable>();
    }
    public enum DiningAreaStatusMode
    {
        Auto = 0,
        Manual = 1
    }

    public enum DiningAreaManualStatus
    {
        Available = 0,
        Occupied = 1
    }
}
