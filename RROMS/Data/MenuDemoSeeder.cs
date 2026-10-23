using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RROMS.Services;
using RROMS.Services.Interfaces;

namespace RROMS.Data;

// Owner: Member 3. All menu writes go through the existing menu service.
internal static class MenuDemoSeeder
{
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
        int createdCategories = 0, createdItems = 0, restoredPhotos = 0, keptItems = 0;
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
            if (current != null && HasPhoto(current.ImageUrl, environment, webRoot))
            {
                keptItems++;
                continue;
            }

            // Existing admin edits, category moves, codes, IDs and availability stay intact.
            var input = current is null
                ? new MenuItemWrite(categoryIds[entry.Category], entry.Name, entry.Description, entry.Price, true)
                : new MenuItemWrite(current.CategoryId, current.Name, current.Description, current.Price, current.IsAvailable);
            string path = Path.Combine(photoRoot, entry.Photo.Replace('/', Path.DirectorySeparatorChar));
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
            else restoredPhotos++;
        }

        logger.LogInformation("Menu demo: {Categories} categories added, {Items} items added, {Photos} missing photos restored, {Kept} existing items kept.",
            createdCategories, createdItems, restoredPhotos, keptItems);
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
        string relative = url.StartsWith("~/", StringComparison.Ordinal) ? url[2..] : url.TrimStart('/');
        if (relative.StartsWith("menu-images/", StringComparison.Ordinal))
        {
            string fileName = relative["menu-images/".Length..];
            return fileName == Path.GetFileName(fileName)
                && !fileName.Contains('\\')
                && File.Exists(Path.Combine(environment.ContentRootPath, "App_Data", "MenuImages", fileName));
        }
        string root = Path.GetFullPath(webRoot) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(webRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path);
    }
}
