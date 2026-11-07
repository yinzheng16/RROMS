using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin.Menu
{
    // Owner: Member 3. The US22 part of the same /Admin/Menu PageModel.
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public partial class IndexModel
    {
        // Navigation/filter values are optional on an initial GET. Non-nullable
        // bound strings can acquire implicit Required validation before handlers run.
        // NormalizeItemOptions supplies safe defaults; dish validation stays separate.
        [BindProperty(SupportsGet = true)] public string? MenuView { get; set; } = "categories";
        [BindProperty(SupportsGet = true)] public string? ItemSearch { get; set; }
        [BindProperty(SupportsGet = true)] public int? ItemCategory { get; set; }
        [BindProperty(SupportsGet = true)] public string? ItemAvailability { get; set; } = "all";
        [BindProperty(SupportsGet = true)] public string? ItemSort { get; set; } = "name";
        [BindProperty(SupportsGet = true)] public int ItemPage { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public bool ItemShowAll { get; set; }
        [BindProperty(Name = "ItemInput")] public MenuItemInput ItemInput { get; set; } = new();
        [BindProperty] public IFormFile? ItemImage { get; set; }
        [BindProperty] public bool RemoveItemImage { get; set; }

        public const int ItemPageSize = 12;   // dish cards, 12 per page
        public MenuItemPage ItemResults { get; private set; } = new(
            Array.Empty<MenuItemSummary>(), 0, 0, 0, 1, ItemPageSize);
        public IReadOnlyList<MenuCategorySummary> ItemCategories { get; private set; }
            = Array.Empty<MenuCategorySummary>();
        public string? CurrentItemImage { get; private set; }
        public string? ItemEditorCode { get; private set; }
        public int? SavedItemCategoryId { get; private set; }
        public bool PhotoReselectionRequired { get; private set; }
        public int FirstItemNumber => ItemResults.TotalCount == 0 ? 0
            : ItemShowAll ? 1 : (ItemPage - 1) * ItemResults.PageSize + 1;
        public int LastItemNumber => ItemResults.TotalCount == 0 ? 0 : FirstItemNumber + ItemResults.Items.Count - 1;

        private async Task<IActionResult> GetItemsAsync(int? itemEditId, CancellationToken cancellationToken)
        {
            NormalizeItemOptions();
            if (itemEditId.HasValue)
            {
                var item = await _orderService.GetMenuItemAsync(itemEditId.Value, cancellationToken);
                if (item is null)
                {
                    TempData["ErrorMessage"] = "That menu item no longer exists.";
                    return RedirectToItems();
                }
                ItemInput = new MenuItemInput
                {
                    Id = item.Id,
                    CategoryId = item.CategoryId,
                    Name = item.Name,
                    Description = item.Description,
                    Price = item.Price,
                    IsAvailable = item.IsAvailable
                };
                CurrentItemImage = item.ImageUrl;
            }
            await LoadItemsAsync(cancellationToken);
            return Page();
        }

        public async Task<IActionResult> OnPostItemSaveAsync(CancellationToken cancellationToken)
        {
            NormalizeItemOptions();
            // Missing properties from the other form may be validated with an
            // empty binding prefix. Keep only this form's validation entries.
            KeepFormValidation(nameof(ItemInput), nameof(ItemImage), nameof(RemoveItemImage));
            if (ItemImage?.Length > MenuItemOptions.MaxImageBytes)
                ModelState.AddModelError(nameof(ItemImage), "Choose an image no larger than 5 MB.");
            if (ModelState.IsValid)
            {
                var result = await _orderService.SaveMenuItemAsync(ItemInput.Id,
                    new MenuItemWrite(ItemInput.CategoryId, ItemInput.Name,
                        ItemInput.Description, ItemInput.Price!.Value, ItemInput.IsAvailable),
                    ItemImage, RemoveItemImage, cancellationToken);
                if (result == MenuItemOperationStatus.Success)
                {
                    string savedName = ItemInput.Name.Trim();
                    TempData["SuccessMessage"] = ItemInput.Id.HasValue
                        ? $"“{savedName}” updated successfully." : $"“{savedName}” created successfully.";
                    return RedirectToItems();
                }
                string field = result switch
                {
                    MenuItemOperationStatus.InvalidName or MenuItemOperationStatus.DuplicateName => "ItemInput.Name",
                    MenuItemOperationStatus.InvalidDescription => "ItemInput.Description",
                    MenuItemOperationStatus.InvalidPrice => "ItemInput.Price",
                    MenuItemOperationStatus.InvalidCategory => "ItemInput.CategoryId",
                    MenuItemOperationStatus.InvalidImage or MenuItemOperationStatus.ImageTooLarge
                        or MenuItemOperationStatus.ImageStorageUnavailable => nameof(ItemImage),
                    _ => string.Empty
                };
                ModelState.AddModelError(field, ItemError(result));
            }
            // Field errors render beside their inputs, once. Preserve binding
            // errors for hidden/unplaced properties in the general summary.
            AddUnplacedItemErrorsToSummary();
            // Photo reselection is an instruction, not a validation error.
            PhotoReselectionRequired = ItemImage != null;
            await LoadItemsAsync(cancellationToken);
            return Page();
        }

        public async Task<IActionResult> OnPostItemAvailabilityAsync(int id, bool? available,
            CancellationToken cancellationToken)
        {
            NormalizeItemOptions();
            if (id <= 0 || !available.HasValue) return BadRequest();
            var result = await _orderService.SetMenuItemAvailabilityAsync(id, available.Value, cancellationToken);
            TempData[result == MenuItemOperationStatus.Success ? "SuccessMessage" : "ErrorMessage"] =
                result == MenuItemOperationStatus.Success
                    ? (available.Value ? "Menu item is now available." : "Menu item is now unavailable.") : ItemError(result);
            return RedirectToItems();
        }

        public async Task<IActionResult> OnPostItemDeleteAsync(int id, CancellationToken cancellationToken)
        {
            NormalizeItemOptions();
            if (id <= 0) return BadRequest();
            // Read the name first so the toast can say which dish was deleted.
            string? deletedName = (await _orderService.GetMenuItemAsync(id, cancellationToken))?.Name;
            var result = await _orderService.DeleteMenuItemAsync(id, cancellationToken);
            TempData[result == MenuItemOperationStatus.Success ? "SuccessMessage" : "ErrorMessage"] =
                result != MenuItemOperationStatus.Success ? ItemError(result)
                : deletedName is null ? "Menu item deleted successfully." : $"“{deletedName}” deleted successfully.";
            return RedirectToItems();
        }

        private async Task LoadItemsAsync(CancellationToken cancellationToken)
        {
            ItemCategories = await _orderService.GetCategoriesAsync(cancellationToken);
            ItemResults = await _orderService.GetMenuItemPageAsync(ItemPage, ItemPageSize, ItemSearch,
                ItemCategory, ItemAvailability, ItemSort, ItemShowAll, cancellationToken);
            ItemPage = ItemResults.PageNumber;
            var savedItem = ItemInput.Id.HasValue
                ? await _orderService.GetMenuItemAsync(ItemInput.Id.Value, cancellationToken)
                : null;
            ItemEditorCode = savedItem?.Code;
            SavedItemCategoryId = savedItem?.CategoryId;
            CurrentItemImage = savedItem?.ImageUrl;
        }
        private void NormalizeItemOptions()
        {
            MenuView = "items";
            ItemSearch = string.IsNullOrWhiteSpace(ItemSearch) ? null : ItemSearch.Trim();
            ItemCategory = ItemCategory > 0 ? ItemCategory : null;
            ItemAvailability = MenuItemOptions.Availability(ItemAvailability);
            ItemSort = MenuItemOptions.Sort(ItemSort);
            ItemPage = ItemShowAll ? 1 : Math.Max(1, ItemPage);
        }
        private void KeepFormValidation(params string[] prefixes)
        {
            foreach (string key in ModelState.Keys.ToArray())
            {
                // Preserve request-level errors and all binding/validation
                // errors for the submitted form, including malformed prices.
                if (key.Length == 0 || prefixes.Any(prefix =>
                    string.Equals(key, prefix, StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))) continue;
                ModelState.Remove(key);
            }
        }
        private void AddUnplacedItemErrorsToSummary()
        {
            var inlineFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ItemInput.CategoryId", "ItemInput.Name", "ItemInput.Description",
                "ItemInput.Price", nameof(ItemImage)
            };
            var summaryMessages = new HashSet<string>(StringComparer.Ordinal);
            if (ModelState.TryGetValue(string.Empty, out var summary) && summary is not null)
                foreach (var error in summary.Errors) summaryMessages.Add(error.ErrorMessage);
            foreach (var entry in ModelState.Where(entry => entry.Key.Length > 0
                && !inlineFields.Contains(entry.Key)).ToArray())
            {
                if (entry.Value is not { } state) continue;
                foreach (var error in state.Errors)
                {
                    string message = string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The submitted menu item details could not be read. Check your entries and try again."
                        : error.ErrorMessage;
                    if (summaryMessages.Add(message)) ModelState.AddModelError(string.Empty, message);
                }
            }
        }
        public string ItemListUrl(int? page = null, bool? showAll = null, int? editId = null, bool reset = false)
            => (Url.Page("./Index", new
            {
                MenuView = "items",
                ItemSearch = reset ? null : ItemSearch,
                ItemCategory = reset ? null : ItemCategory,
                ItemAvailability = reset ? "all" : ItemAvailability,
                ItemSort = reset ? "name" : ItemSort,
                ItemPage = page ?? ItemPage,
                ItemShowAll = showAll ?? ItemShowAll,
                itemEditId = editId
            }) ?? string.Empty) + "#category-collection";
        private IActionResult RedirectToItems() => RedirectToPage("./Index", new
        { MenuView = "items", ItemSearch, ItemCategory, ItemAvailability, ItemSort, ItemPage, ItemShowAll });
        private static string ItemError(MenuItemOperationStatus status) => status switch
        {
            MenuItemOperationStatus.NotFound => "That menu item no longer exists. Refresh the list.",
            MenuItemOperationStatus.InvalidName => "Enter a menu item name of 1 to 100 characters.",
            MenuItemOperationStatus.DuplicateName => "A menu item with this name already exists. Choose a different name.",
            MenuItemOperationStatus.CodeLimitReached => "No additional item codes can be assigned in this category. Please contact the administrator.",
            MenuItemOperationStatus.Busy => "The menu is busy. Please try saving again.",
            MenuItemOperationStatus.InvalidDescription => "Keep the description within 500 characters.",
            MenuItemOperationStatus.InvalidPrice => "Enter a price from RM0.01 to RM10,000.00, with at most two decimal places.",
            MenuItemOperationStatus.InvalidCategory => "Select a category that still exists.",
            MenuItemOperationStatus.InvalidImage => "Choose a valid JPG, PNG or WebP image.",
            MenuItemOperationStatus.ImageTooLarge => "Choose an image no larger than 5 MB.",
            MenuItemOperationStatus.ImageStorageUnavailable => "The photo could not be stored. Please try again.",
            MenuItemOperationStatus.HasOrders => "This dish is referenced by an order. Mark it unavailable instead of deleting it.",
            _ => "The menu item could not be saved."
        };
        public sealed class MenuItemInput
        {
            public int? Id { get; set; }
            [Range(1, int.MaxValue, ErrorMessage = "Select a category.")]
            [Display(Name = "Category")] public int CategoryId { get; set; }
            [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
            [StringLength(500)] public string? Description { get; set; }
            [Required(ErrorMessage = "Enter a price.")]
            [Range(typeof(decimal), "0.01", "10000", ErrorMessage = "Enter a price between RM0.01 and RM10,000.00.")]
            [Display(Name = "Price (RM)")] public decimal? Price { get; set; }
            [Display(Name = "Available for ordering")] public bool IsAvailable { get; set; } = true;
        }
    }
}
