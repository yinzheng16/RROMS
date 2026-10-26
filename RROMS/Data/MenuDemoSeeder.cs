using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using RROMS.Services;
using RROMS.Services.Interfaces;

namespace RROMS.Data;

// Owner: Member 3. All menu writes go through the existing menu service.
internal static class MenuDemoSeeder
{
    // SHA-256 of the original ten drink files in the shared 80-item catalogue.
    // Only those exact old photos may be refreshed; a different admin upload is kept.
    private static readonly IReadOnlyDictionary<string, string> OriginalDrinkPhotos =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["15-arukoru/01-japanese-lager-330-ml.png"] = "E84CE5D0A2607274F57E8CFDB1F108BBBF1DF7F662C95E9032C43538BFE7B1AD",
            ["15-arukoru/02-nihonshu-180-ml.png"] = "BEDB67638FE4E5D73F7D90ED2887CA804D520CDE28C3AC8C5B314AE6F037076D",
            ["15-arukoru/03-umeshu-on-the-rocks-60-ml.png"] = "D8ED4BFA6B8A65D5A76C548B01B3066B1F5B560BD45C33C1DABB1F3427F870AA",
            ["15-arukoru/04-house-red-wine-150-ml.png"] = "0D95D152779647CCA9EAF7CDCEC9E6758DAA53709686140505DD86981625CE9A",
            ["15-arukoru/05-house-white-wine-150-ml.png"] = "42932D03B890A425B37AF9BE593190DD708F4D59A0306C85DC7043734097737D",
            ["16-sofuto-dorinku/01-iced-green-tea.png"] = "86E529F01E3F821E997234AAC7C05C1FD74206F4799605E7BFA95157E0818751",
            ["16-sofuto-dorinku/02-iced-matcha-latte.png"] = "C1EA154BA30E66AEFF1BB19D1FDA87571845937D825774D63B3E1099DD4D4451",
            ["16-sofuto-dorinku/03-yuzu-soda.png"] = "2A4BBD4E630FB4E420C2C6B12A3E149B8B26BF28E73AA293760C9C90A25E4EF2",
            ["16-sofuto-dorinku/04-original-ramune-200-ml.png"] = "C8BF926653AD1932D0222EF47DE786892995FED65B668FC8F4C318D7FC8FE628",
            ["16-sofuto-dorinku/05-peach-iced-tea.png"] = "4D5246C959026C9668F0BB22DDB8F26386DF20FB5B095ECFFC00C78402F32E5C",
        };

    internal static async Task SeedAsync(IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        // Local opt-out after the demo has been installed; no shared setting is changed.
        if (!services.GetRequiredService<IConfiguration>().GetValue("MenuDemo:Enabled", true))
            return;

        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var menu = services.GetRequiredService<IOrderService>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MenuDemoSeeder");
        var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath, "wwwroot") : environment.WebRootPath;
        var photoRoot = Path.Combine(webRoot, "images", "menu-demo");

        // Fail before any database writes if the photo folder was not copied.
        foreach (var entry in MenuDemoCatalogue.Items)
        {
            var photo = new FileInfo(Path.Combine(photoRoot, entry.Photo.Replace('/', Path.DirectorySeparatorChar)));
            if (!photo.Exists)
                throw new FileNotFoundException("Copy the complete wwwroot/images/menu-demo folder before running menu seeding.", photo.FullName);
            if (photo.Length <= 0 || photo.Length > MenuItemOptions.MaxImageBytes)
                throw new InvalidOperationException("The demo photo is empty or exceeds 5 MB: " + entry.Photo);
        }

        var categories = (await menu.GetCategoriesAsync(cancellationToken)).ToList();
        var categoryIds = new Dictionary<string, int>(StringComparer.Ordinal);
        int createdCategories = 0, createdItems = 0, restoredPhotos = 0,
            refreshedDrinkPhotos = 0, keptItems = 0;
        foreach (string name in MenuDemoCatalogue.Items.Select(entry => entry.Category).Distinct())
        {
            var category = FindCategory(categories, name);
            if (category is null)
            {
                var result = await menu.CreateCategoryAsync(name, cancellationToken);
                if (result != CategoryOperationStatus.Success && result != CategoryOperationStatus.DuplicateName)
                    throw new InvalidOperationException("Could not seed category '" + name + "': " + result);
                if (result == CategoryOperationStatus.Success) createdCategories++;
                categories = (await menu.GetCategoriesAsync(cancellationToken)).ToList();
                category = FindCategory(categories, name)
                    ?? throw new InvalidOperationException("The seeded category could not be found: " + name);
            }
            categoryIds.Add(name, category.Id);
        }

        var page = await menu.GetMenuItemPageAsync(1, 50, showAll: true,
            cancellationToken: cancellationToken);
        var existing = page.Items.GroupBy(item => MenuItemNameRules.DuplicateKey(item.Name))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var entry in MenuDemoCatalogue.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            existing.TryGetValue(MenuItemNameRules.DuplicateKey(entry.Name), out var current);
            string path = Path.Combine(photoRoot, entry.Photo.Replace('/', Path.DirectorySeparatorChar));
            bool hasPhoto = current != null && HasPhoto(current.ImageUrl, environment, webRoot);
            bool refreshDrinkPhoto = hasPhoto && await ShouldRefreshDrinkPhotoAsync(
                entry.Photo, current!.ImageUrl, path, environment, webRoot, logger, cancellationToken);
            if (hasPhoto && !refreshDrinkPhoto)
            {
                keptItems++;
                continue;
            }

            // Existing admin edits, category moves, codes, IDs and availability stay intact.
            var input = current is null
                ? new MenuItemWrite(categoryIds[entry.Category], entry.Name, entry.Description, entry.Price, true)
                : new MenuItemWrite(current.CategoryId, current.Name, current.Description, current.Price, current.IsAvailable);
            await using var stream = File.OpenRead(path);
            var image = new FormFile(stream, 0, stream.Length, "ItemImage", Path.GetFileName(path))
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/png"
            };
            var result = await menu.SaveMenuItemAsync(current?.Id, input, image,
                removeImage: false, cancellationToken: cancellationToken);
            if (result == MenuItemOperationStatus.DuplicateName)
            {
                // Another app instance may have inserted this item after our snapshot.
                var refreshed = await menu.GetMenuItemPageAsync(1, 50, showAll: true,
                    cancellationToken: cancellationToken);
                if (refreshed.Items.Any(item => MenuItemNameRules.DuplicateKey(item.Name)
                    == MenuItemNameRules.DuplicateKey(entry.Name)))
                {
                    keptItems++;
                    continue;
                }
            }
            if (result != MenuItemOperationStatus.Success)
                throw new InvalidOperationException("Could not seed menu item '" + entry.Name + "': " + result);
            if (current is null) createdItems++;
            else if (refreshDrinkPhoto) refreshedDrinkPhotos++;
            else restoredPhotos++;
        }

        logger.LogInformation("Menu demo: {Categories} categories added, {Items} items added, {Photos} missing photos restored, {Drinks} original drink photos refreshed, {Kept} existing items kept.",
            createdCategories, createdItems, restoredPhotos, refreshedDrinkPhotos, keptItems);
    }

    private static MenuCategorySummary? FindCategory(IEnumerable<MenuCategorySummary> categories, string name)
    {
        string key = MenuCodeRules.CategoryKey(name);
        // Match the Japanese primary name first, then the stored known prefix.
        // This also respects renamed categories and different English subtitles.
        return categories.FirstOrDefault(category => MenuCodeRules.CategoryKey(category.Name) == key)
            ?? (MenuCodeRules.KnownPrefixes.TryGetValue(key, out string? code)
                ? categories.FirstOrDefault(category => category.Code == code) : null);
    }

    private static bool HasPhoto(string? url, IWebHostEnvironment environment, string webRoot)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (Uri.TryCreate(url, UriKind.Absolute, out var external)
            && (external.Scheme == Uri.UriSchemeHttp || external.Scheme == Uri.UriSchemeHttps)) return true;
        string? path = LocalPhotoPath(url, environment, webRoot);
        return path != null && File.Exists(path);
    }

    private static async Task<bool> ShouldRefreshDrinkPhotoAsync(string cataloguePhoto,
        string? currentUrl, string sourcePath, IWebHostEnvironment environment, string webRoot,
        ILogger logger, CancellationToken cancellationToken)
    {
        if (!OriginalDrinkPhotos.TryGetValue(cataloguePhoto, out string? originalHash))
            return false;
        string? currentPath = LocalPhotoPath(currentUrl, environment, webRoot);
        if (currentPath is null) return false;

        try
        {
            // New URLs are saved through the menu service so browser caches see the update.
            // Compare bytes, not a random upload filename or a timestamp. An unchanged
            // source does nothing, and a subsequent startup keeps the refreshed photo.
            await using var current = File.OpenRead(currentPath);
            string currentHash = Convert.ToHexString(await SHA256.HashDataAsync(current, cancellationToken));
            if (!string.Equals(currentHash, originalHash, StringComparison.Ordinal)) return false;
            await using var source = File.OpenRead(sourcePath);
            string sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken));
            return !string.Equals(sourceHash, originalHash, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            // An unreadable image cannot safely be identified as an original demo photo.
            logger.LogWarning(exception, "Kept an existing drink photo that could not be checked: {Photo}", cataloguePhoto);
            return false;
        }
    }

    private static string? LocalPhotoPath(string? url, IWebHostEnvironment environment, string webRoot)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var external)
            && (external.Scheme == Uri.UriSchemeHttp || external.Scheme == Uri.UriSchemeHttps)) return null;
        string relative = url.StartsWith("~/", StringComparison.Ordinal) ? url[2..] : url.TrimStart('/');
        if (relative.StartsWith("menu-images/", StringComparison.Ordinal))
        {
            string fileName = relative["menu-images/".Length..];
            return fileName == Path.GetFileName(fileName) && !fileName.Contains('\\')
                ? Path.Combine(environment.ContentRootPath, "App_Data", "MenuImages", fileName) : null;
        }
        string root = Path.GetFullPath(webRoot) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(webRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
    }
}
