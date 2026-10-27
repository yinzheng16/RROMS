using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;
using System.Text.RegularExpressions;

namespace RROMS.Pages.Admin;

// US49 Restaurant Settings (Member 5)
public class Settings : PageModel
{
    private const long MaxBrandImageBytes = 2 * 1024 * 1024;   // 2 MB
    private const long MaxFaviconBytes = 512 * 1024;           // 512 KB

    private static readonly string[] BrandImageExtensions = { ".jpg", ".jpeg", ".png" };
    private static readonly string[] FaviconExtensions = { ".png", ".ico" };

    private readonly IReportService _reportService;
    private readonly IAccountService _accountService;
    private readonly IWebHostEnvironment _environment;

    public Settings(
        IReportService reportService,
        IAccountService accountService,
        IWebHostEnvironment environment)
    {
        _reportService = reportService;
        _accountService = accountService;
        _environment = environment;
    }

    [BindProperty]
    public RestaurantSetting RestaurantSetting { get; set; } = new();

    // The number as the admin types it, without the country code, e.g. "12-345 6789".
    // Saved into RestaurantSetting.Phone in international format, e.g. "+60123456789".
    [BindProperty]
    public string? PhoneInput { get; set; }

    // Navbar image: a logo (LogoWithName) or the full brand image (FullBrandImage).
    [BindProperty]
    public IFormFile? LogoFile { get; set; }

    [BindProperty]
    public IFormFile? FooterFile { get; set; }

    [BindProperty]
    public IFormFile? FaviconFile { get; set; }

    // Ticked = the footer reuses the navbar image (FooterImagePath is cleared).
    [BindProperty]
    public bool FooterSameAsNav { get; set; } = true;

    // Set by the "Use default" button: go back to the site's default tab icon.
    [BindProperty]
    public bool RemoveFavicon { get; set; }

    // Same country list as /Account/Register (Member 1's PhoneCountries).
    public IReadOnlyList<PhoneCountries.Country> Countries => PhoneCountries.All;

    public async Task OnGetAsync()
    {
        RestaurantSetting = await _reportService.GetRestaurantSettingsAsync();
        PhoneInput = ToLocalNumber(RestaurantSetting.Phone, RestaurantSetting.PhoneCountry);
        FooterSameAsNav = string.IsNullOrWhiteSpace(RestaurantSetting.FooterImagePath);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var existingSettings = await _reportService.GetRestaurantSettingsAsync();

        // Image paths are never typed in the form.
        // Keep the saved ones unless the admin uploads or removes one below.
        RestaurantSetting.LogoPath = existingSettings.LogoPath;
        RestaurantSetting.FooterImagePath = FooterSameAsNav ? null : existingSettings.FooterImagePath;
        RestaurantSetting.FaviconPath = RemoveFavicon ? null : existingSettings.FaviconPath;

        // Restaurant name stays editable in BOTH branding modes:
        // it is still used on receipts, billing and the browser tab title.
        RestaurantSetting.RestaurantName = RestaurantSetting.RestaurantName?.Trim() ?? string.Empty;
        RestaurantSetting.RestaurantNameLine2 = NullIfBlank(RestaurantSetting.RestaurantNameLine2);
        RestaurantSetting.Email = NullIfBlank(RestaurantSetting.Email);
        RestaurantSetting.Address = NullIfBlank(RestaurantSetting.Address);

        // Validate branding mode
        if (RestaurantSetting.BrandingMode != "LogoWithName" &&
            RestaurantSetting.BrandingMode != "FullBrandImage")
        {
            ModelState.AddModelError(
                "RestaurantSetting.BrandingMode",
                "Please select a valid branding option.");
        }

        ValidateAndFormatPhone();

        ValidateImage(LogoFile, nameof(LogoFile), "Navigation image",
            BrandImageExtensions, MaxBrandImageBytes, "2 MB");

        if (!FooterSameAsNav)
        {
            ValidateImage(FooterFile, nameof(FooterFile), "Footer image",
                BrandImageExtensions, MaxBrandImageBytes, "2 MB");
        }

        ValidateImage(FaviconFile, nameof(FaviconFile), "Tab icon",
            FaviconExtensions, MaxFaviconBytes, "512 KB");

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Save new images only after everything is valid
        if (HasFile(LogoFile))
        {
            RestaurantSetting.LogoPath = await SaveImageAsync(LogoFile!);
        }

        if (!FooterSameAsNav && HasFile(FooterFile))
        {
            RestaurantSetting.FooterImagePath = await SaveImageAsync(FooterFile!);
        }

        if (HasFile(FaviconFile))
        {
            RestaurantSetting.FaviconPath = await SaveImageAsync(FaviconFile!);
        }

        await _reportService.UpdateRestaurantSettingsAsync(RestaurantSetting);

        TempData["SuccessMessage"] = "Settings saved";

        return RedirectToPage();
    }

    // ---------- Phone ----------

    // Uses the same rules as /Account/Register:
    // the number must be a real number for the selected country.
    private void ValidateAndFormatPhone()
    {
        var country = PhoneCountries.Find(RestaurantSetting.PhoneCountry);

        if (country is null)
        {
            ModelState.AddModelError(
                "RestaurantSetting.PhoneCountry",
                "Please choose a valid country.");
            return;
        }

        // Phone is optional.
        if (string.IsNullOrWhiteSpace(PhoneInput))
        {
            RestaurantSetting.Phone = null;
            return;
        }

        var input = PhoneInput.Trim();

        // Digits only (spaces, dashes, brackets and + are allowed for readability).
        // libphonenumber would otherwise turn letters into digits (e.g. "1-800-FLOWERS").
        if (!Regex.IsMatch(input, @"^[0-9+\-\s()]+$"))
        {
            ModelState.AddModelError(
                nameof(PhoneInput),
                "Phone number can only contain digits.");
            return;
        }

        var formatted = _accountService.FormatPhoneNumber(country.Iso, input);

        if (formatted is null)
        {
            ModelState.AddModelError(
                nameof(PhoneInput),
                $"This is not a valid {country.Name} ({country.DialCode}) phone number.");
            return;
        }

        RestaurantSetting.Phone = formatted;   // e.g. "+60123456789"
    }

    // "+60123456789" + MY  ->  "123456789" (shown after the +60 dropdown).
    private static string? ToLocalNumber(string? phone, string? countryIso)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var country = PhoneCountries.Find(countryIso);

        if (country != null && phone.StartsWith(country.DialCode))
        {
            return phone[country.DialCode.Length..];
        }

        return phone;   // old data saved before this change, e.g. "abc" or "012-3456789"
    }

    // ---------- Images ----------

    private static bool HasFile(IFormFile? file) => file != null && file.Length > 0;

    private void ValidateImage(
        IFormFile? file,
        string key,
        string label,
        string[] allowedExtensions,
        long maxBytes,
        string maxText)
    {
        if (!HasFile(file))
        {
            return;
        }

        if (file!.Length > maxBytes)
        {
            ModelState.AddModelError(key, $"{label} must not exceed {maxText}.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(extension))
        {
            var allowed = string.Join(", ", allowedExtensions.Select(e => e.TrimStart('.').ToUpperInvariant()));
            ModelState.AddModelError(key, $"{label} must be one of: {allowed}.");
        }
    }

    private async Task<string> SaveImageAsync(IFormFile file)
    {
        var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
        Directory.CreateDirectory(uploadsFolder);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadsFolder, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return $"/uploads/{fileName}";
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
