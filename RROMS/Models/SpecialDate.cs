using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    public class SpecialDate : IValidatableObject
    {
        public int Id { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Start Date")]
        public DateTime Date { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "End Date")]
        public DateTime? EndDate { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsClosed { get; set; }

        [DataType(DataType.Time)]
        public TimeSpan? OpenTime { get; set; }

        [DataType(DataType.Time)]
        public TimeSpan? CloseTime { get; set; }

        public IEnumerable<ValidationResult> Validate(
            ValidationContext validationContext)
        {
            if (EndDate.HasValue &&
                EndDate.Value.Date < Date.Date)
            {
                yield return new ValidationResult(
                    "End date must be on or after start date.",
                    new[] { nameof(EndDate) });
            }
        }
    }
}