using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

// Owner: Member 3 (US24). Separate instructions identify separate portions.
public class CustomerCartLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(450)]
    public string UserId { get; set; } = string.Empty;
    public CustomerCart Cart { get; set; } = null!;
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    [Range(1, 100)]
    public int Quantity { get; set; }
    [StringLength(200)]
    public string? Notes { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
}
