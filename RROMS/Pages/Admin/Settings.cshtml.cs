using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;
using RROMS.Services.Interfaces;
using System.Text.RegularExpressions;

namespace RROMS.Pages.Admin;

// US49 Restaurant Settings (Member 5; follow-up fixes by Member 1)
// Contact details, charges and receipt details. The brand is fixed in Models/Brand.cs.
public class Settings : PageModel
{
    private readonly IReportService _reportService;
    private readonly IAccountService _accountService;

    public Settings(IReportService reportService, IAccountService accountService)
    {
        _reportService = reportService;
        _accountService = accountService;
    }

    [BindProperty]
    public RestaurantSetting RestaurantSetting { get; set; } = new();

    // The number as the admin types it, without the country code, e.g. "03-2142 6688".
    // Saved into RestaurantSetting.Phone in international format, e.g. "+60321426688".
    [BindProperty]
    public string? PhoneInput { get; set; }

    // Same country list as /Account/Register (Member 1's PhoneCountries).
    public IReadOnlyList<PhoneCountries.Country> Countries => PhoneCountries.All;

    public async Task OnGetAsync()
    {
        RestaurantSetting = await _reportService.GetRestaurantSettingsAsync();
        PhoneInput = PhoneDisplay.National(RestaurantSetting.Phone, RestaurantSetting.PhoneCountry);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        RestaurantSetting.Email = NullIfBlank(RestaurantSetting.Email);
        RestaurantSetting.Address = NullIfBlank(RestaurantSetting.Address);
        RestaurantSetting.SstRegistrationNo = NullIfBlank(RestaurantSetting.SstRegistrationNo)?.ToUpperInvariant();
        RestaurantSetting.ReceiptFooterNote = NullIfBlank(RestaurantSetting.ReceiptFooterNote);

        ValidateAndFormatPhone();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await _reportService.UpdateRestaurantSettingsAsync(RestaurantSetting);

        TempData["SuccessMessage"] = "Settings saved";
        return RedirectToPage();
    }

    // Same rules as /Account/Register: a real number for the selected country.
    private void ValidateAndFormatPhone()
    {
        var country = PhoneCountries.Find(RestaurantSetting.PhoneCountry);

        if (country is null)
        {
            ModelState.AddModelError("RestaurantSetting.PhoneCountry", "Please choose a valid country.");
            return;
        }

        // Phone is optional.
        if (string.IsNullOrWhiteSpace(PhoneInput))
        {
            RestaurantSetting.Phone = null;
            return;
        }

        var input = PhoneInput.Trim();

        // Digits only (spaces, dashes, brackets and + are fine).
        // libphonenumber would otherwise turn letters into digits (e.g. "1-800-FLOWERS").
        if (!Regex.IsMatch(input, @"^[0-9+\-\s()]+$"))
        {
            ModelState.AddModelError(nameof(PhoneInput), "Phone number can only contain digits.");
            return;
        }

        var formatted = _accountService.FormatPhoneNumber(country.Iso, input);

        if (formatted is null)
        {
            ModelState.AddModelError(nameof(PhoneInput),
                $"This is not a valid {country.Name} ({country.DialCode}) phone number.");
            return;
        }

        RestaurantSetting.Phone = formatted;   // e.g. "+60321426688"
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
