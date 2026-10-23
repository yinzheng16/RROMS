// Owner: Member 2 (Tables & Reservations)
using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

/// <summary>Live state of a table. Used by the table board in Sprint 2 (US17).</summary>
public enum TableStatus
{
    Available,
    Occupied,
    Cleaning,
    [Display(Name = "Out of service")] OutOfService
}
