using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace RROMS.Models;

public class TableTypeDefinition
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    // New system-generated table type code.
    [StringLength(6)]
    public string? Code { get; set; }

    // Legacy fields: keep temporarily until Service and database migration are updated.
    [Required, StringLength(10)]
    public string Prefix { get; set; } = string.Empty;

    public int StartingNumber { get; set; } = 1;

    [Range(1, 4)]
    public int NumberPadding { get; set; } = 3;

    public int DefaultDiningAreaId { get; set; }

    public DiningArea DefaultDiningArea { get; set; } = null!;

    [Range(1, 100)]
    public int DefaultMinGuests { get; set; } = 1;

    [Range(1, 100)]
    public int DefaultMaxGuests { get; set; } = 2;

    [Precision(10, 2)]
    public decimal? DefaultMinimumSpend { get; set; }

    [StringLength(200)]
    public string? PhotoPath { get; set; }

    [StringLength(300)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<RestaurantTable> Tables { get; set; }
        = new List<RestaurantTable>();

    public bool AllowBelowMinGuests { get; set; }

    [StringLength(50)] public string? JapaneseName { get; set; }
}