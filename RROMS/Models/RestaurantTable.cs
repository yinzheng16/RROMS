using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 2 (Tables & Reservations)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class RestaurantTable
    {
        public int Id { get; set; }

        public int DiningAreaId { get; set; }
        public DiningArea DiningArea { get; set; } = null!;

        [Required, StringLength(10)]
        public string TableNumber { get; set; } = string.Empty;

        [Range(1, 20)]
        public int Capacity { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    }
}
