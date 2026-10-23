using Microsoft.Build.Framework;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RROMS.Services
{
    // Owner: Member 3. Database operations stay here, per the team instructions.
    public partial class OrderService : IOrderService
    {
        private readonly ApplicationDbContext _context;

        public OrderService(ApplicationDbContext context,
            Microsoft.AspNetCore.Hosting.IWebHostEnvironment environment,
            Microsoft.Extensions.Logging.ILogger<OrderService> logger)
        {
            _context = context;
            _imageDirectory = System.IO.Path.Combine(environment.ContentRootPath, "App_Data", "MenuImages");
            _logger = logger;
        }

        public async Task<IReadOnlyList<MenuCategorySummary>> GetCategoriesAsync(
            CancellationToken cancellationToken = default,
            string? searchTerm = null, string? sortOrder = null)
        {
            return await OrderCategories(FilterCategories(searchTerm), sortOrder)
                .Select(category => new MenuCategorySummary(
                    category.Id, category.Name, category.DisplayOrder,
                    category.MenuItems.Count, category.Code))
                .ToListAsync(cancellationToken);
        }

        public async Task<MenuCategoryPage> GetCategoryPageAsync(
            int pageNumber, int pageSize,
            CancellationToken cancellationToken = default,
            string? searchTerm = null, string? sortOrder = null,
            bool showAll = false)
        {
            pageSize = Math.Clamp(pageSize, 1, 50);
            var query = FilterCategories(searchTerm);
            int totalCount = await query.CountAsync(cancellationToken);
            if (showAll)
                pageSize = Math.Max(1, totalCount);
            int totalPages = totalCount == 0 ? 1 : (totalCount - 1) / pageSize + 1;
            pageNumber = Math.Clamp(pageNumber, 1, totalPages);

            // Query filtered totals separately so summary cards do not change
            // when the Admin simply switches pages. One DbContext is used
            // sequentially; only the paginated view uses Skip/Take.
            var categoryIds = query.Select(category => category.Id);
            int totalItemCount = await _context.MenuItems.AsNoTracking()
                .CountAsync(item => categoryIds.Contains(item.CategoryId), cancellationToken);
            int emptyCategoryCount = await query.CountAsync(
                category => !category.MenuItems.Any(), cancellationToken);

            IQueryable<MenuCategory> rows = OrderCategories(query, sortOrder);
            if (!showAll)
                rows = rows.Skip((pageNumber - 1) * pageSize).Take(pageSize);

            var items = await rows
                .Select(category => new MenuCategorySummary(
                    category.Id, category.Name, category.DisplayOrder,
                    category.MenuItems.Count, category.Code))
                .ToListAsync(cancellationToken);

            return new MenuCategoryPage(items, totalCount, totalItemCount,
                emptyCategoryCount, pageNumber, pageSize,
                showAll && string.IsNullOrWhiteSpace(searchTerm) && MenuCategorySorting.Normalize(sortOrder) == "display"
                    ? MenuCategoryOrderRules.Stamp(items.Select(row => (row.Id, row.DisplayOrder))) : "");
        }

        private IQueryable<MenuCategory> FilterCategories(string? searchTerm)
        {
            var query = _context.MenuCategories.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.Trim();
                query = query.Where(category => category.Name.Contains(term) || category.Code.Contains(term));
            }
            return query;
        }

        private static IOrderedQueryable<MenuCategory> OrderCategories(
            IQueryable<MenuCategory> query, string? sortOrder)
        {
            // Each ordering ends in a unique ID to keep page boundaries stable
            // when several categories share a name, position or dish count.
            return MenuCategorySorting.Normalize(sortOrder) switch
            {
                "display-desc" => query.OrderByDescending(category => category.DisplayOrder)
                    .ThenBy(category => category.Name).ThenBy(category => category.Id),
                "name" => query.OrderBy(category => category.Name)
                    .ThenBy(category => category.Id),
                "name-desc" => query.OrderByDescending(category => category.Name)
                    .ThenBy(category => category.Id),
                "items" => query.OrderBy(category => category.MenuItems.Count)
                    .ThenBy(category => category.Name).ThenBy(category => category.Id),
                "items-desc" => query.OrderByDescending(category => category.MenuItems.Count)
                    .ThenBy(category => category.Name).ThenBy(category => category.Id),
                _ => query.OrderBy(category => category.DisplayOrder)
                    .ThenBy(category => category.Name).ThenBy(category => category.Id)
            };
        }

        public async Task<MenuCategorySummary?> GetCategoryAsync(
            int id, CancellationToken cancellationToken = default)
        {
            return await _context.MenuCategories
                .AsNoTracking()
                .Where(category => category.Id == id)
                .Select(category => new MenuCategorySummary(
                    category.Id, category.Name, category.DisplayOrder,
                    category.MenuItems.Count, category.Code))
                .SingleOrDefaultAsync(cancellationToken);
        }

        public async Task<CategoryOperationStatus> CreateCategoryAsync(
            string name, CancellationToken cancellationToken = default)
        {
            string normalizedName = name?.Trim() ?? string.Empty;
            if (normalizedName.Length == 0 || normalizedName.Length > 50)
                return CategoryOperationStatus.InvalidName;

            return await WriteCategoryOrderAsync(async rows =>
            {
                // Database equality matches the unique name index/collation.
                if (await _context.MenuCategories.AnyAsync(row => row.Name == normalizedName, cancellationToken))
                    return CategoryOperationStatus.DuplicateName;
                MenuCategoryOrderRules.Compact(rows);
                string code = await AllocateCategoryCodeAsync(normalizedName, cancellationToken);
                _context.MenuCategories.Add(new MenuCategory { Name = normalizedName, Code = code, DisplayOrder = checked(rows.Count + 1) });
                return CategoryOperationStatus.Success;
            }, cancellationToken);
        }

        public async Task<CategoryOperationStatus> UpdateCategoryAsync(
            int id, string name, CancellationToken cancellationToken = default)
        {
            string normalizedName = name?.Trim() ?? string.Empty;

            // Enforce rules in the service as well as the form.
            if (normalizedName.Length == 0 || normalizedName.Length > 50)
                return CategoryOperationStatus.InvalidName;

            var category = await _context.MenuCategories
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (category is null)
                return CategoryOperationStatus.NotFound;

            // Equality follows the database collation, matching its unique index.
            bool duplicateExists = await _context.MenuCategories
                .AnyAsync(item => item.Name == normalizedName
                    && item.Id != id, cancellationToken);

            if (duplicateExists)
                return CategoryOperationStatus.DuplicateName;

            category.Name = normalizedName;
            // Renaming keeps the category in its existing position.

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return CategoryOperationStatus.Success;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another request deleted the row before this update completed.
                _context.Entry(category).State = EntityState.Detached;
                return CategoryOperationStatus.NotFound;
            }
            catch (DbUpdateException exception) when (IsDuplicateName(exception))
            {
                // Also handle simultaneous submissions that pass the first check.
                _context.Entry(category).State = EntityState.Detached;
                return CategoryOperationStatus.DuplicateName;
            }
        }

        public async Task<CategoryOperationStatus> DeleteCategoryAsync(
            int id, CancellationToken cancellationToken = default)
        {
            return await WriteCategoryOrderAsync(async rows =>
            {
                var category = rows.SingleOrDefault(row => row.Id == id);
                if (category is null) return CategoryOperationStatus.NotFound;
                if (await _context.MenuItems.AnyAsync(item => item.CategoryId == id, cancellationToken))
                    return CategoryOperationStatus.HasMenuItems;
                _context.MenuCategories.Remove(category);
                rows.Remove(category);
                MenuCategoryOrderRules.Compact(rows);
                return CategoryOperationStatus.Success;
            }, cancellationToken);
        }

        public async Task<CategoryOperationStatus> ReorderCategoriesAsync(
            IReadOnlyList<int> orderedIds, string? expectedStamp,
            CancellationToken cancellationToken = default)
        {
            // Materialize request data before a possible execution-strategy retry.
            int[] requested = orderedIds?.ToArray() ?? Array.Empty<int>();
            return await WriteCategoryOrderAsync(rows =>
            {
                string currentStamp = MenuCategoryOrderRules.Stamp(rows.Select(row => (row.Id, row.DisplayOrder)));
                if (string.IsNullOrEmpty(expectedStamp) || !string.Equals(currentStamp, expectedStamp, StringComparison.Ordinal))
                    return Task.FromResult(CategoryOperationStatus.OrderChanged);
                if (!MenuCategoryOrderRules.IsCompletePermutation(requested, rows.Select(row => row.Id)))
                    return Task.FromResult(CategoryOperationStatus.InvalidOrder);
                var byId = rows.ToDictionary(row => row.Id);
                MenuCategoryOrderRules.Compact(requested.Select(id => byId[id]));
                return Task.FromResult(CategoryOperationStatus.Success);
            }, cancellationToken);
        }

        // All changes to positions share one serializable transaction. Holding a
        // range lock on the complete list prevents concurrent create/delete/reorder
        // from assigning overlapping positions. Retry the whole operation on deadlock.
        private async Task<CategoryOperationStatus> WriteCategoryOrderAsync(
            Func<List<MenuCategory>, Task<CategoryOperationStatus>> change,
            CancellationToken cancellationToken)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var trackedBefore = _context.ChangeTracker.Entries<MenuCategory>().Select(entry => entry.Entity).ToHashSet();
                    try
                    {
                        await using var transaction = await _context.Database.BeginTransactionAsync(
                            IsolationLevel.Serializable, cancellationToken);
                        var rows = await _context.MenuCategories.OrderBy(row => row.DisplayOrder)
                            .ThenBy(row => row.Id).ToListAsync(cancellationToken);
                        var result = await change(rows);
                        if (result != CategoryOperationStatus.Success) return result;
                        await _context.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return CategoryOperationStatus.Success;
                    }
                    catch (DbUpdateException exception) when (IsDuplicateName(exception))
                    { return CategoryOperationStatus.DuplicateName; }
                    catch (OverflowException)
                    { return CategoryOperationStatus.CodeLimitReached; }
                    catch (DbUpdateConcurrencyException)
                    { return CategoryOperationStatus.OrderChanged; }
                    catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException sql && sql.Number == 547)
                    { return CategoryOperationStatus.HasMenuItems; }
                    catch (Exception exception) when (exception.GetBaseException() is SqlException sql && sql.Number == 1205)
                    {
                        if (attempt == 2) return CategoryOperationStatus.Busy;
                        await Task.Delay(50 * (attempt + 1), cancellationToken);
                    }
                    finally
                    {
                        // Do not carry failed or already committed category entities
                        // into a retry. Leave unrelated tracked entities untouched.
                        DetachMenuCodeSequences();
                        foreach (var entry in _context.ChangeTracker.Entries<MenuCategory>().ToList())
                            if (!trackedBefore.Contains(entry.Entity)) entry.State = EntityState.Detached;
                    }
                }
                return CategoryOperationStatus.Busy;
            });
        }

        private static bool IsDuplicateName(DbUpdateException exception)
        {
            return exception.GetBaseException() is SqlException sql
                && (sql.Number == 2601 || sql.Number == 2627);
        }
    }
}
