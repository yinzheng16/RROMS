using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace RROMS.Models;

public class TableTypeDefinition
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(10)]
    public string Prefix { get; set; } = string.Empty;

    public int StartingNumber { get; set; } = 1;

    public int NumberPadding { get; set; } = 1;

    public int DefaultDiningAreaId { get; set; }

    public DiningArea DefaultDiningArea { get; set; } = null!;

    [Range(1, 20)]
    public int DefaultMinGuests { get; set; } = 1;

    [Range(1, 20)]
    public int DefaultMaxGuests { get; set; } = 2;

    [Precision(10, 2)]
    public decimal DefaultMinimumSpend { get; set; }

    [StringLength(200)]
    public string? PhotoPath { get; set; }

    [StringLength(300)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<RestaurantTable> Tables { get; set; }
        = new List<RestaurantTable>();
}