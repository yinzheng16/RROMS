using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Areas.Identity.Pages.Account.Manage
{
    // US03 / US04 (Owner: Member 1): My profile. Replaces the built-in Identity "Manage your account" page,
    // so the "My profile" link in the navbar opens this page (same URL: /Identity/Account/Manage).
    // It always loads the logged-in user; there is no user ID in the URL, so nobody can open someone else's profile.
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly IProfileService _profiles;
        private readonly IAccountService _accounts;

        public IndexModel(IProfileService profiles, IAccountService accounts)
        {
            _profiles = profiles;
            _accounts = accounts;
        }

        public ProfileView Profile { get; private set; } = null!;

        // Staff / Admin: their starting page (US51), for the "Go to my workspace" button.
        public string WorkspaceUrl { get; private set; } = "/";

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required(ErrorMessage = "Please enter your full name.")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Full name must be {2} to {1} characters.")]
            [Display(Name = "Full name")]
            public string FullName { get; set; } = string.Empty;

            [Required(ErrorMessage = "Please choose your country code.")]
            public string CountryCode { get; set; } = "MY";

            // Same rule as Register (US01); the service also checks it is a real number for the country.
            [Required(ErrorMessage = "Please enter your phone number.")]
            [RegularExpression(@"^[0-9][0-9\s-]{4,18}[0-9]$",
                ErrorMessage = "Please enter a valid phone number using numbers only, e.g. 12-345 6789.")]
            [Display(Name = "Phone number")]
            public string PhoneNumber { get; set; } = string.Empty;

            public List<string> Preferences { get; set; } = new();

            [StringLength(DiningOptions.NotesMaxLength, ErrorMessage = "Notes can be up to {1} characters.")]
            public string? Notes { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await LoadAsync()) return Challenge();

            Input = new InputModel
            {
                FullName = Profile.FullName,
                CountryCode = Profile.PhoneCountry,
                PhoneNumber = Profile.PhoneLocal,
                Preferences = Profile.Preferences,
                Notes = Profile.Notes
            };
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!await LoadAsync()) return Challenge();
            if (!ModelState.IsValid) return Page();   // keeps what the user typed

            var errors = await _profiles.UpdateAsync(User,
                new ProfileUpdate(Input.FullName, Input.CountryCode, Input.PhoneNumber, Input.Preferences, Input.Notes));

            if (errors.Count > 0)
            {
                foreach (var (key, message) in errors) ModelState.AddModelError(key, message);
                return Page();
            }

            TempData["SuccessMessage"] = "Your profile has been updated.";
            return RedirectToPage();   // reload, so the card shows the saved details
        }

        private async Task<bool> LoadAsync()
        {
            var profile = await _profiles.GetAsync(User);
            if (profile is null) return false;
            Profile = profile;
            WorkspaceUrl = _accounts.GetStartPage(User);
            return true;
        }
    }
}
