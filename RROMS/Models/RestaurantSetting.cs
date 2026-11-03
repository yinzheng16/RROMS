using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 5 (Feedback, Reports & Dashboard) · US49 Restaurant Settings
    // Single-row table holding restaurant-wide settings.
    // The restaurant name and logo are fixed in Models/Brand.cs (not editable here).
    public class RestaurantSetting
    {
        public int Id { get; set; }

        // ---------- Contact ----------
        // ISO country code chosen in the phone dropdown, e.g. "MY".
        [Required, StringLength(2)]
        public string PhoneCountry { get; set; } = "MY";

        // Stored in international format, e.g. +60321426688 (validated in the PageModel).
        // Show it with PhoneDisplay.Pretty(...) -> "+60 3-2142 6688".
        [StringLength(20)]
        public string? Phone { get; set; }

        [StringLength(100)]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string? Email { get; set; }

        // May contain line breaks. Show it with CSS white-space: pre-line.
        [StringLength(200, ErrorMessage = "Address can be at most 200 characters.")]
        public string? Address { get; set; }

        // ---------- Charges ----------
        // Use IReportService.CalculateChargesAsync(...) instead of these directly,
        // so every page works out the bill the same way.
        [Range(typeof(decimal), "0", "100",
            ErrorMessage = "Tax rate must be between 0% and 100%.")]
        public decimal TaxRatePercent { get; set; }

        [Range(typeof(decimal), "0", "100",
            ErrorMessage = "Service charge must be between 0% and 100%.")]
        public decimal ServiceChargePercent { get; set; }

        // false (default) = takeaway orders get no service charge.
        public bool ServiceChargeOnTakeaway { get; set; }

        // ---------- Receipt ----------
        // US49 follow-up: the registered (legal) business name, as on the SSM / SST registration,
        // e.g. "Yoru Izakaya Sdn. Bhd.". Printed on bills and receipts (Member 4) under Brand.FullName.
        // Optional: leave it out of the receipt when empty. The brand name itself stays fixed in Brand.cs.
        [StringLength(100, ErrorMessage = "Business name can be at most 100 characters.")]
        [RegularExpression(@"^[A-Za-z0-9 .,&()'/\-]*$",
            ErrorMessage = "Use letters, numbers, spaces and . , & ( ) ' / - only.")]
        public string? BusinessName { get; set; }

        // Printed on bills and receipts (Member 4), e.g. "W10-1808-31000123".
        [StringLength(30, ErrorMessage = "SST registration number can be at most 30 characters.")]
        [RegularExpression(@"^[A-Za-z0-9\- ]*$",
            ErrorMessage = "Use letters, numbers and dashes only.")]
        public string? SstRegistrationNo { get; set; }

        // Last line of the bill, e.g. "Thank you, see you again!"
        [StringLength(120, ErrorMessage = "Receipt note can be at most 120 characters.")]
        public string? ReceiptFooterNote { get; set; }
    }
}