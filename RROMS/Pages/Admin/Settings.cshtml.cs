using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin;

public class Settings : PageModel
{
    private readonly IReportService _reportService;
    private readonly IWebHostEnvironment _environment;

    public Settings(
        IReportService reportService,
        IWebHostEnvironment environment)
    {
        _reportService = reportService;
        _environment = environment;
    }

    [BindProperty]
    public RestaurantSetting RestaurantSetting { get; set; } = new();

    [BindProperty]
    public IFormFile? LogoFile { get; set; }

    public async Task OnGetAsync()
    {
        RestaurantSetting = await _reportService.GetRestaurantSettingsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var existingSettings =
            await _reportService.GetRestaurantSettingsAsync();

        // Keep the existing logo if no new logo is uploaded
        RestaurantSetting.LogoPath = existingSettings.LogoPath;

        if (LogoFile != null && LogoFile.Length > 0)
        {
            // Maximum file size: 2 MB
            if (LogoFile.Length > 2 * 1024 * 1024)
            {
                ModelState.AddModelError(
                    nameof(LogoFile),
                    "Logo file must not exceed 2 MB.");
            }

            var extension =
                Path.GetExtension(LogoFile.FileName).ToLowerInvariant();

            var allowedExtensions =
                new[] { ".jpg", ".jpeg", ".png" };

            if (!allowedExtensions.Contains(extension))
            {
                ModelState.AddModelError(
                    nameof(LogoFile),
                    "Only JPG, JPEG or PNG files are allowed.");
            }
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (LogoFile != null && LogoFile.Length > 0)
        {
            var uploadsFolder = Path.Combine(
                _environment.WebRootPath,
                "uploads");

            Directory.CreateDirectory(uploadsFolder);

            var extension =
                Path.GetExtension(LogoFile.FileName).ToLowerInvariant();

            var fileName =
                $"{Guid.NewGuid()}{extension}";

            var filePath =
                Path.Combine(uploadsFolder, fileName);

            await using (var stream =
                         new FileStream(filePath, FileMode.Create))
            {
                await LogoFile.CopyToAsync(stream);
            }

            RestaurantSetting.LogoPath =
                $"/uploads/{fileName}";
        }

        await _reportService
            .UpdateRestaurantSettingsAsync(RestaurantSetting);

        TempData["SuccessMessage"] =
            "Restaurant settings updated successfully.";

        return RedirectToPage();
    }
}