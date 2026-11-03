using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PhoneNumbers;   // NuGet: libphonenumber-csharp (already used by Register)
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 1 (US03 / US04).
    // DiningPreferences is saved as readable text in the existing column (500 characters), e.g.
    //   "Shellfish; Peanuts; No pork | Notes: celebrating anniversaries"
    // so no database change is needed.
    public class ProfileService : IProfileService
    {
        private const string NotesTag = "Notes: ";
        private const string Separator = " | ";
        private const int StoredMaxLength = 500;   // [StringLength(500)] on ApplicationUser.DiningPreferences

        private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IAccountService _accounts;

        public ProfileService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IAccountService accounts)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _accounts = accounts;
        }

        public async Task<ProfileView?> GetAsync(ClaimsPrincipal principal)
        {
            var user = await _userManager.GetUserAsync(principal);
            if (user is null) return null;

            var role = await RoleOfAsync(user);
            var (iso, local, display) = SplitPhone(user.PhoneNumber);
            var (prefs, notes) = Parse(user.DiningPreferences);
            var isCustomer = role == Roles.Customer;

            // Completeness: name + phone (+ allergies/preferences answered, customers only). Notes never count.
            var hasPhone = display is not null;
            var hasPrefs = prefs.Count > 0;
            var parts = isCustomer ? 3 : 2;
            var done = 1 + (hasPhone ? 1 : 0) + (isCustomer && hasPrefs ? 1 : 0);   // name is always there (required)
            var percent = (int)Math.Round(done * 100.0 / parts);

            string? nextText = null, nextAnchor = null;
            if (!hasPhone) { nextText = "Add your phone number"; nextAnchor = "#pf-phone"; }
            else if (isCustomer && !hasPrefs) { nextText = "Tell us about allergies"; nextAnchor = "#prefs"; }

            return new ProfileView(user.FullName, user.Email ?? "", role, user.CreatedAt, user.IsActive,
                iso, local, display, prefs, notes, percent, nextText, nextAnchor);
        }

        public async Task<Dictionary<string, string>> UpdateAsync(ClaimsPrincipal principal, ProfileUpdate input)
        {
            var errors = new Dictionary<string, string>();
            var user = await _userManager.GetUserAsync(principal);
            if (user is null)
            {
                errors[""] = "Please log in again.";
                return errors;
            }

            // Phone: same rule as Register (real number for the chosen country), saved as +60123456789.
            var country = PhoneCountries.Find(input.CountryIso);
            string? phone = null;
            if (country is null)
                errors["Input.CountryCode"] = "Please choose your country code.";
            else
            {
                phone = _accounts.FormatPhoneNumber(country.Iso, input.PhoneNumber);
                if (phone is null)
                    errors["Input.PhoneNumber"] = $"That doesn't look like a valid {country.Name} phone number.";
            }

            // Preferences: customers only. Staff and Admin keep whatever is there.
            var isCustomer = await RoleOfAsync(user) == Roles.Customer;
            string? stored = user.DiningPreferences;
            if (isCustomer)
            {
                var notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
                if (notes?.Length > DiningOptions.NotesMaxLength)
                    errors["Input.Notes"] = $"Notes can be up to {DiningOptions.NotesMaxLength} characters.";

                stored = Build(input.Preferences, notes);
                if (stored?.Length > StoredMaxLength)
                    errors["Input.Notes"] = "That's too much to save. Please shorten your notes.";
            }

            if (errors.Count > 0) return errors;

            user.FullName = input.FullName.Trim();
            user.PhoneNumber = phone;
            if (isCustomer) user.DiningPreferences = stored;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                errors[""] = "Sorry, your profile could not be saved. Please try again.";
                return errors;
            }

            // Refresh the login cookie so the new name is used straight away.
            await _signInManager.RefreshSignInAsync(user);
            return errors;
        }

        public async Task<string?> GetDiningSummaryAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return null;

            var (prefs, notes) = Parse(user.DiningPreferences);
            var allergies = prefs.Where(DiningOptions.Allergies.Contains).ToList();
            var diet = prefs.Where(DiningOptions.Diet.Contains).ToList();

            var parts = new List<string>();
            if (allergies.Count > 0) parts.Add("Allergies: " + string.Join(", ", allergies));
            if (diet.Count > 0) parts.Add("Diet: " + string.Join(", ", diet));
            if (notes is not null) parts.Add("Notes: " + notes);
            return parts.Count == 0 ? null : string.Join(". ", parts);
        }

        // ---------- helpers ----------

        private async Task<string> RoleOfAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains(Roles.Admin)) return Roles.Admin;
            if (roles.Contains(Roles.Staff)) return Roles.Staff;
            return Roles.Customer;
        }

        // Only known chips are kept. "No allergies or preferences" cannot be mixed with other chips.
        private static string? Build(IEnumerable<string> picked, string? notes)
        {
            var known = DiningOptions.Allergies.Concat(DiningOptions.Diet).ToList();
            var chips = picked.Contains(DiningOptions.None)
                ? new List<string> { DiningOptions.None }
                : known.Where(picked.Contains).ToList();   // keeps the page order

            var text = string.Join("; ", chips);
            if (notes is not null)
                text = text.Length == 0 ? NotesTag + notes : text + Separator + NotesTag + notes;
            return text.Length == 0 ? null : text;
        }

        // Anything that isn't a known chip (e.g. older free text) is kept as notes, so nothing is lost.
        private static (List<string> Prefs, string? Notes) Parse(string? stored)
        {
            var prefs = new List<string>();
            if (string.IsNullOrWhiteSpace(stored)) return (prefs, null);

            string chipPart = stored, notes = "";
            var at = stored.IndexOf(NotesTag, StringComparison.Ordinal);
            if (at >= 0)
            {
                notes = stored[(at + NotesTag.Length)..].Trim();
                chipPart = stored[..at].Replace(Separator.Trim(), "").Trim();
            }

            var known = DiningOptions.Allergies.Concat(DiningOptions.Diet).Append(DiningOptions.None).ToList();
            var unknown = new List<string>();
            foreach (var part in chipPart.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (known.Contains(part)) prefs.Add(part); else unknown.Add(part);
            }
            if (unknown.Count > 0) notes = string.Join("; ", unknown) + (notes.Length > 0 ? "; " + notes : "");

            return (prefs, notes.Length == 0 ? null : notes);
        }

        // "+60123456789" -> ("MY", "012-345 6789", "+60 12-345 6789"). Empty or unknown -> Malaysia, as typed.
        private static (string Iso, string Local, string? Display) SplitPhone(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return ("MY", "", null);
            try
            {
                var number = PhoneUtil.Parse(stored, "MY");
                var region = PhoneUtil.GetRegionCodeForNumber(number);
                var iso = PhoneCountries.Find(region)?.Iso ?? "MY";
                return (iso,
                        PhoneUtil.Format(number, PhoneNumberFormat.NATIONAL),
                        PhoneUtil.Format(number, PhoneNumberFormat.INTERNATIONAL));
            }
            catch (NumberParseException)
            {
                return ("MY", stored, stored);
            }
        }
    }
}
