using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

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

        // Maximum number of guests.
        [Range(1, 100)]
        public int Capacity { get; set; }

        // Admin switch: an inactive table is never offered for booking.
        public bool IsActive { get; set; } = true;

        // ---- Added for US11 (YORU table types, minimum spend, photo, live status) ----
        public TableType Type { get; set; } = TableType.Standard;
        public int? TableTypeDefinitionId { get; set; }

        public TableTypeDefinition? TableTypeDefinition { get; set; }

        [Range(1, 100)]
        public int MinGuests { get; set; } = 1;

        // 0 = no minimum spend. VIP rooms RM 500, VVIP room RM 1,000.
        [Precision(10, 2)]
        public decimal MinimumSpend { get; set; }

        [StringLength(200)]
        public string? PhotoPath { get; set; }

        [StringLength(300)]
        public string? Description { get; set; }

        // Live state shown on the table board (Sprint 2).
        public TableStatus Status { get; set; } = TableStatus.Available;

        // Remembers the status this table had before its dining area was
        // marked Occupied, so it can be restored when the area opens again.
        // Null when the area is not Occupied.
        public TableStatus? StatusBeforeAreaOccupied { get; set; }
        public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    }
}
