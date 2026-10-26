using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 5 (Feedback, Reports & Dashboard)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    // Single-row table holding restaurant-wide settings.
    public class RestaurantSetting
    {
        public int Id { get; set; }

        // ---------- Name ----------
        // Line 1 = official restaurant name. Also used on receipts / billing (Member 4),
        // the browser tab title and image alt text, so it is required in both branding modes.
        [Required(ErrorMessage = "Restaurant name is required.")]
        [StringLength(30, ErrorMessage = "Restaurant name can be at most 30 characters.")]
        public string RestaurantName { get; set; } = string.Empty;

        // Optional subtitle under line 1 (only shown in LogoWithName mode).
        [StringLength(40, ErrorMessage = "Line 2 can be at most 40 characters.")]
        public string? RestaurantNameLine2 { get; set; }

        // Text size presets for the navbar name: S / M / L.
        [Required, StringLength(1)]
        [RegularExpression("^[SML]$", ErrorMessage = "Please choose S, M or L.")]
        public string NameLine1Size { get; set; } = "M";

        [Required, StringLength(1)]
        [RegularExpression("^[SML]$", ErrorMessage = "Please choose S, M or L.")]
        public string NameLine2Size { get; set; } = "S";

        // ---------- Contact ----------
        // ISO country code chosen in the phone dropdown, e.g. "MY".
        [Required, StringLength(2)]
        public string PhoneCountry { get; set; } = "MY";

        // Stored in international format, e.g. +60123456789 (validated in the PageModel).
        [StringLength(20)]
        public string? Phone { get; set; }

        [StringLength(100)]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string? Email { get; set; }

        [StringLength(200, ErrorMessage = "Address can be at most 200 characters.")]
        public string? Address { get; set; }

        // ---------- Charges ----------
        [Range(typeof(decimal), "0", "100",
            ErrorMessage = "Tax rate must be between 0% and 100%.")]
        public decimal TaxRatePercent { get; set; }

        [Range(typeof(decimal), "0", "100",
            ErrorMessage = "Service charge must be between 0% and 100%.")]
        public decimal ServiceChargePercent { get; set; }

        // ---------- Branding images ----------
        // Navbar image: a logo (LogoWithName) or the full brand image (FullBrandImage).
        // Kept as LogoPath so existing data and teammates' code keep working.
        [StringLength(300)]
        public string? LogoPath { get; set; }

        // Footer image. Null = use the navbar image.
        [StringLength(300)]
        public string? FooterImagePath { get; set; }

        // Browser tab icon. Null = default site favicon.
        [StringLength(300)]
        public string? FaviconPath { get; set; }

        [Required, StringLength(30)]
        public string BrandingMode { get; set; } = "LogoWithName";
    }
}