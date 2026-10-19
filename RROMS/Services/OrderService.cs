using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 3. Database operations stay here, per the team instructions.
    public class OrderService : IOrderService
    {
        private readonly ApplicationDbContext _context;

        public OrderService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<MenuCategorySummary>> GetCategoriesAsync(
            CancellationToken cancellationToken = default,
            string? searchTerm = null, string? sortOrder = null)
        {
            return await OrderCategories(FilterCategories(searchTerm), sortOrder)
                .Select(category => new MenuCategorySummary(
                    category.Id, category.Name, category.DisplayOrder,
                    category.MenuItems.Count))
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
                    category.MenuItems.Count))
                .ToListAsync(cancellationToken);

            return new MenuCategoryPage(items, totalCount, totalItemCount,
                emptyCategoryCount, pageNumber, pageSize);
        }

        private IQueryable<MenuCategory> FilterCategories(string? searchTerm)
        {
            var query = _context.MenuCategories.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.Trim();
                query = query.Where(category => category.Name.Contains(term));
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
                    category.MenuItems.Count))
                .SingleOrDefaultAsync(cancellationToken);
        }

        public async Task<CategoryOperationStatus> CreateCategoryAsync(
            string name, CancellationToken cancellationToken = default)
        {
            string normalizedName = name?.Trim() ?? string.Empty;
            if (normalizedName.Length == 0 || normalizedName.Length > 50)
                return CategoryOperationStatus.InvalidName;

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                // Protect max + 1 from concurrent category creation. A deadlock
                // retries the whole short transaction, not only the failed query.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var category = new MenuCategory { Name = normalizedName };
                    try
                    {
                        await using var transaction = await _context.Database
                            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

                        bool duplicateExists = await _context.MenuCategories.AnyAsync(
                            item => item.Name == normalizedName, cancellationToken);
                        if (duplicateExists)
                            return CategoryOperationStatus.DuplicateName;

                        int highestOrder = await _context.MenuCategories
                            .MaxAsync(item => (int?)item.DisplayOrder, cancellationToken) ?? 0;
                        if (highestOrder == int.MaxValue)
                            return CategoryOperationStatus.DisplayOrderLimitReached;

                        category.DisplayOrder = Math.Max(0, highestOrder) + 1;
                        _context.MenuCategories.Add(category);
                        await _context.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return CategoryOperationStatus.Success;
                    }
                    catch (DbUpdateException exception) when (IsDuplicateName(exception))
                    {
                        _context.Entry(category).State = EntityState.Detached;
                        return CategoryOperationStatus.DuplicateName;
                    }
                    catch (Exception exception) when (
                        exception.GetBaseException() is SqlException sql && sql.Number == 1205)
                    {
                        _context.Entry(category).State = EntityState.Detached;
                        if (attempt == 2)
                            return CategoryOperationStatus.Busy;

                        await Task.Delay(50 * (attempt + 1), cancellationToken);
                    }
                    catch
                    {
                        // A configured execution strategy may retry another transient
                        // failure. Do not carry the failed entity into its next attempt.
                        _context.Entry(category).State = EntityState.Detached;
                        throw;
                    }
                }

                return CategoryOperationStatus.Busy;
            });
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
            var category = await _context.MenuCategories
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (category is null)
                return CategoryOperationStatus.NotFound;

            // Includes unavailable dishes: the category is still in use.
            if (await _context.MenuItems.AnyAsync(
                item => item.CategoryId == id, cancellationToken))
                return CategoryOperationStatus.HasMenuItems;

            _context.MenuCategories.Remove(category);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return CategoryOperationStatus.Success;
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.Entry(category).State = EntityState.Detached;
                return CategoryOperationStatus.NotFound;
            }
            catch (DbUpdateException exception) when (
                exception.GetBaseException() is SqlException sql && sql.Number == 547)
            {
                // A dish may have been assigned after the first check.
                // The existing foreign key prevents accidental cascade deletion.
                _context.Entry(category).State = EntityState.Detached;
                return CategoryOperationStatus.HasMenuItems;
            }
        }

        private static bool IsDuplicateName(DbUpdateException exception)
        {
            return exception.GetBaseException() is SqlException sql
                && (sql.Number == 2601 || sql.Number == 2627);
        }
    }
}
