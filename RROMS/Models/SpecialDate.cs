using System;
using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    public class SpecialDate
    {
        public int Id { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsClosed { get; set; }

        [DataType(DataType.Time)]
        public TimeSpan? OpenTime { get; set; }

        [DataType(DataType.Time)]
        public TimeSpan? CloseTime { get; set; }
    }
}