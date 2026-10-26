using System.Security.Claims;

namespace RROMS.Services.Interfaces
{
    // The chips on the profile page. Only these values are saved, so other pages can trust them.
    public static class DiningOptions
    {
        public static readonly IReadOnlyList<string> Allergies = new[]
        {
            "Shellfish", "Fish / dashi", "Peanuts", "Tree nuts", "Egg", "Dairy", "Soy", "Sesame", "Wheat / gluten"
        };

        public static readonly IReadOnlyList<string> Diet = new[]
        {
            "Halal-friendly", "Vegetarian", "Vegan", "No pork", "No beef", "No alcohol", "No raw food", "Mild / not spicy"
        };

        // Lets a customer say "nothing to declare", so staff can tell it apart from "never filled in".
        public const string None = "No allergies or preferences";

        public const int NotesMaxLength = 300;
    }

    // Everything the profile page shows (no database entity leaves the service).
    public record ProfileView(
        string FullName,
        string Email,
        string Role,                       // "Customer", "Staff" or "Admin"
        DateTime MemberSince,
        bool IsActive,
        string PhoneCountry,               // "MY"
        string PhoneLocal,                 // "012-345 6789" (shown in the box)
        string? PhoneDisplay,              // "+60 12-345 6789" (shown on the card), null if none
        List<string> Preferences,          // chips that are ticked
        string? Notes,                     // "Other notes"
        int PercentComplete,               // 0-100
        string? NextStepText,              // "Add your phone number"
        string? NextStepAnchor);           // "#pf-phone"

    // What the form sends back.
    public record ProfileUpdate(
        string FullName,
        string CountryIso,
        string PhoneNumber,
        IReadOnlyCollection<string> Preferences,
        string? Notes);

    // Owner: Member 1 (US03 customer profile, US04 staff profile).
    // Always works on the LOGGED-IN user, so nobody can view or edit someone else's profile.
    public interface IProfileService
    {
        Task<ProfileView?> GetAsync(ClaimsPrincipal user);

        // Returns field errors ("Input.PhoneNumber" -> message). Empty = saved.
        Task<Dictionary<string, string>> UpdateAsync(ClaimsPrincipal user, ProfileUpdate update);

        // For Member 2 (reservation special request) and Member 3 (order remarks):
        // a ready-to-use text like "Allergies: Shellfish, Peanuts. Diet: No pork. Notes: ...", or null if nothing saved.
        Task<string?> GetDiningSummaryAsync(string userId);
    }
}
