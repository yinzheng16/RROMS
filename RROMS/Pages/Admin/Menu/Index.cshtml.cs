using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin.Menu
{
    // Owner: Member 3. Pages call services only.
    [Authorize(Policy = "AdminOnly")]
    public class IndexModel : PageModel
    {
        private readonly IOrderService _orderService;

        public IndexModel(IOrderService orderService)
        {
            _orderService = orderService;
        }

        public IReadOnlyList<MenuCategorySummary> Categories { get; private set; }
            = Array.Empty<MenuCategorySummary>();

        public const int CategoryPageSize = 5;
        public int TotalCategories { get; private set; }
        public int ListedItemCount { get; private set; }
        public int EmptyCategoryCount { get; private set; }
        public int TotalPages { get; private set; } = 1;
        public int FirstCategoryNumber => TotalCategories == 0 ? 0
            : ShowAll ? 1 : (PageNumber - 1) * CategoryPageSize + 1;
        public int LastCategoryNumber => TotalCategories == 0 ? 0
            : Math.Min(FirstCategoryNumber + (Categories.Count - 1), TotalCategories);

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public bool ShowAll { get; set; }

        [BindProperty]
        public CategoryInputModel Input { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SortOrder { get; set; } = MenuCategorySorting.Default;

        public bool IsSearching => !string.IsNullOrWhiteSpace(SearchTerm);

        public string SortDescription => SortOrder switch
        {
            "display-desc" => "Display order: high to low",
            "name" => "Name: A to Z",
            "name-desc" => "Name: Z to A",
            "items" => "Items: fewest first",
            "items-desc" => "Items: most first",
            _ => "Display order: low to high"
        };

        public async Task<IActionResult> OnGetAsync(
            int? editId, CancellationToken cancellationToken)
        {
            NormalizeTableOptions();

            if (editId.HasValue)
            {
                var category = await _orderService.GetCategoryAsync(
                    editId.Value, cancellationToken);

                if (category is null)
                {
                    TempData["MenuCategoryError"] = "That category no longer exists.";
                    return RedirectToCategoryList();
                }

                Input = new CategoryInputModel
                {
                    Id = category.Id,
                    Name = category.Name
                };
            }

            await LoadCategoriesAsync(cancellationToken);
            return Page();
        }

        public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
        {
            NormalizeTableOptions();

            if (!ModelState.IsValid)
            {
                await LoadCategoriesAsync(cancellationToken);
                return Page();
            }

            var result = Input.Id.HasValue
                ? await _orderService.UpdateCategoryAsync(
                    Input.Id.Value, Input.Name, cancellationToken)
                : await _orderService.CreateCategoryAsync(
                    Input.Name, cancellationToken);

            if (result == CategoryOperationStatus.Success)
            {
                TempData["MenuCategorySuccess"] = Input.Id.HasValue
                    ? "Category updated successfully."
                    : "Category created successfully.";
                return RedirectToCategoryList();
            }

            string key = result switch
            {
                CategoryOperationStatus.InvalidName => "Input.Name",
                CategoryOperationStatus.DuplicateName => "Input.Name",
                _ => string.Empty
            };

            ModelState.AddModelError(key, GetErrorMessage(result));
            await LoadCategoriesAsync(cancellationToken);
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync(
            int id, CancellationToken cancellationToken)
        {
            NormalizeTableOptions();

            // Delete validates its own ID and service rules, independently of the add/edit form.
            if (id <= 0)
                return BadRequest();

            var result = await _orderService.DeleteCategoryAsync(id, cancellationToken);

            if (result == CategoryOperationStatus.Success)
                TempData["MenuCategorySuccess"] = "Category deleted successfully.";
            else
                TempData["MenuCategoryError"] = GetErrorMessage(result);

            return RedirectToCategoryList();
        }

        private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
        {
            var result = await _orderService.GetCategoryPageAsync(
                PageNumber, CategoryPageSize, cancellationToken, SearchTerm, SortOrder, ShowAll);
            Categories = result.Items;
            TotalCategories = result.TotalCount;
            ListedItemCount = result.TotalItemCount;
            EmptyCategoryCount = result.EmptyCategoryCount;
            TotalPages = result.TotalPages;
            PageNumber = result.PageNumber;
        }

        private void NormalizeTableOptions()
        {
            SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm.Trim();
            SortOrder = MenuCategorySorting.Normalize(SortOrder);
            PageNumber = ShowAll ? 1 : Math.Max(1, PageNumber);
        }

        private IActionResult RedirectToCategoryList()
        {
            // Keep the current table view after save/delete and missing-record redirects.
            return RedirectToPage("./Index", new { SearchTerm, SortOrder, PageNumber, ShowAll });
        }

        private static string GetErrorMessage(CategoryOperationStatus status)
        {
            return status switch
            {
                CategoryOperationStatus.InvalidName => "Enter a category name of 1 to 50 characters.",
                CategoryOperationStatus.DisplayOrderLimitReached => "A new category could not be assigned a position. Please contact the administrator.",
                CategoryOperationStatus.Busy => "Another category is being saved. Please try again.",
                CategoryOperationStatus.DuplicateName => "A category with this name already exists.",
                CategoryOperationStatus.NotFound => "That category no longer exists. Refresh the page and try again.",
                CategoryOperationStatus.HasMenuItems => "This category contains menu items and cannot be deleted. Move or remove those items first.",
                _ => "The category could not be saved. Please try again."
            };
        }

        public sealed class CategoryInputModel
        {
            public int? Id { get; set; }

            [Required(ErrorMessage = "Enter a category name.")]
            [StringLength(50, ErrorMessage = "Category names cannot exceed 50 characters.")]
            [Display(Name = "Category name")]
            public string Name { get; set; } = string.Empty;

        }
    }
}
