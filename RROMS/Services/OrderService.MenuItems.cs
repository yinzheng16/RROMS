using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 3. Item queries, validation and upload storage stay here.
    public partial class OrderService
    {
        private readonly string _imageDirectory;
        private readonly ILogger<OrderService> _logger;

        private IQueryable<MenuItemSummary> ItemSummaries(IQueryable<MenuItem> query)
            => query.Select(item => new MenuItemSummary(item.Id, item.CategoryId,
                item.Category.Name, item.Name, item.Description, item.Price, item.ImageUrl,
                item.IsAvailable, item.CreatedAt,
                _context.OrderItems.Any(orderItem => orderItem.MenuItemId == item.Id),
                item.Code, item.Category.Code));

        public async Task<MenuItemPage> GetMenuItemPageAsync(int pageNumber, int pageSize,
            string? searchTerm = null, int? categoryId = null,
            string? availability = null, string? sortOrder = null,
            bool showAll = false, CancellationToken cancellationToken = default)
        {
            var query = _context.MenuItems.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.Trim();
                query = query.Where(item => item.Name.Contains(term) || item.Code.Contains(term)
                    || item.Category.Name.Contains(term) || item.Category.Code.Contains(term));
            }
            if (categoryId.HasValue)
                query = query.Where(item => item.CategoryId == categoryId.Value);
            query = MenuItemOptions.Availability(availability) switch
            {
                "available" => query.Where(item => item.IsAvailable),
                "unavailable" => query.Where(item => !item.IsAvailable),
                _ => query
            };
            int total = await query.CountAsync(cancellationToken);
            int available = await query.CountAsync(item => item.IsAvailable, cancellationToken);
            pageSize = showAll ? Math.Max(1, total) : Math.Clamp(pageSize, 1, 50);
            int pages = total == 0 ? 1 : (total - 1) / pageSize + 1;
            pageNumber = showAll ? 1 : Math.Clamp(pageNumber, 1, pages);
            IOrderedQueryable<MenuItem> ordered = MenuItemOptions.Sort(sortOrder) switch
            {
                "name-desc" => query.OrderByDescending(item => item.Name).ThenBy(item => item.Id),
                "price" => query.OrderBy(item => item.Price).ThenBy(item => item.Id),
                "price-desc" => query.OrderByDescending(item => item.Price).ThenBy(item => item.Id),
                "newest" => query.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id),
                "oldest" => query.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id),
                _ => query.OrderBy(item => item.Name).ThenBy(item => item.Id)
            };
            IQueryable<MenuItem> rows = ordered;
            if (!showAll) rows = rows.Skip((pageNumber - 1) * pageSize).Take(pageSize);
            return new MenuItemPage(await ItemSummaries(rows).ToListAsync(cancellationToken),
                total, available, total - available, pageNumber, pageSize);
        }

        public Task<MenuItemSummary?> GetMenuItemAsync(int id, CancellationToken cancellationToken = default)
            => ItemSummaries(_context.MenuItems.AsNoTracking().Where(item => item.Id == id))
                .SingleOrDefaultAsync(cancellationToken);

        public async Task<IReadOnlyList<MenuItemSummary>> GetOrderableMenuItemsAsync(
            CancellationToken cancellationToken = default)
            => await WithCustomerPricesAsync(await ItemSummaries(_context.MenuItems.AsNoTracking()
                .Where(item => item.IsAvailable && item.Price > 0)
                .OrderBy(item => item.Category.DisplayOrder).ThenBy(item => item.Category.Name).ThenBy(item => item.CategoryId)
                .ThenBy(item => item.Name).ThenBy(item => item.Id))
                .ToListAsync(cancellationToken), cancellationToken);

        public async Task<MenuItemSummary?> GetOrderableMenuItemAsync(int id,
            CancellationToken cancellationToken = default)
        {
            var item = await ItemSummaries(_context.MenuItems.AsNoTracking()
                .Where(item => item.Id == id && item.IsAvailable && item.Price > 0))
                .SingleOrDefaultAsync(cancellationToken);
            return item is null ? null : (await WithCustomerPricesAsync(new[] { item }, cancellationToken))[0];
        }

        public async Task<MenuItemOperationStatus> SaveMenuItemAsync(int? id, MenuItemWrite input,
            IFormFile? image, bool removeImage, CancellationToken cancellationToken = default)
        {
            string name = input.Name?.Trim() ?? string.Empty;
            string? description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
            if (name.Length == 0 || name.Length > 100) return MenuItemOperationStatus.InvalidName;
            if (description?.Length > 500) return MenuItemOperationStatus.InvalidDescription;
            if (input.Price < 0.01m || input.Price > 10000m || decimal.Round(input.Price, 2) != input.Price)
                return MenuItemOperationStatus.InvalidPrice;
            if (!await _context.MenuCategories.AnyAsync(c => c.Id == input.CategoryId, cancellationToken))
                return MenuItemOperationStatus.InvalidCategory;

            string duplicateKey = MenuItemNameRules.DuplicateKey(name);
            int excludedId = id ?? 0;
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    MenuItem? item = null;
                    string? newImage = null;
                    try
                    {
                        // Keep the name check and write in one serializable unit.
                        // Concurrent saves through this service cannot both pass
                        // the same name check, even without a new unique index.
                        await using var transaction = await _context.Database
                            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                        item = id.HasValue
                            ? await _context.MenuItems.SingleOrDefaultAsync(i => i.Id == id.Value, cancellationToken)
                            : new MenuItem();
                        if (item is null) return MenuItemOperationStatus.NotFound;
                        // Normalize both new input and legacy names in .NET so every
                        // Unicode whitespace character follows the same rule. Read only
                        // names, inside this transaction, before any upload or mutation.
                        var existingNames = await _context.MenuItems.AsNoTracking()
                            .Where(existing => existing.Id != excludedId)
                            .Select(existing => existing.Name)
                            .ToListAsync(cancellationToken);
                        bool duplicateExists = existingNames.Any(existingName =>
                            MenuItemNameRules.DuplicateKey(existingName) == duplicateKey);
                        if (duplicateExists) return MenuItemOperationStatus.DuplicateName;

                        // Reject duplicates before creating an uploaded file or
                        // changing a tracked item's values.
                        string? oldImage = item.ImageUrl;
                        if (image != null)
                        {
                            var upload = await StoreImageAsync(image, cancellationToken);
                            if (upload.Status != MenuItemOperationStatus.Success) return upload.Status;
                            newImage = upload.Url;
                        }
                        var categoryCode = await _context.MenuCategories.AsNoTracking()
                            .Where(category => category.Id == input.CategoryId)
                            .Select(category => category.Code).SingleOrDefaultAsync(cancellationToken);
                        if (categoryCode is null)
                        {
                            await CleanupUnusedImageAsync(newImage);
                            return MenuItemOperationStatus.InvalidCategory;
                        }
                        // Changing category consumes a new destination code; the
                        // database ID and existing order-item references stay intact.
                        if (!id.HasValue || item.CategoryId != input.CategoryId)
                            item.Code = await AllocateItemCodeAsync(categoryCode, cancellationToken);
                        item.CategoryId = input.CategoryId;
                        item.Name = name;
                        item.Description = description;
                        item.Price = input.Price;
                        item.IsAvailable = input.IsAvailable;
                        item.ImageUrl = newImage ?? (removeImage ? null : oldImage);
                        if (!id.HasValue) _context.MenuItems.Add(item);
                        await _context.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        if (oldImage != item.ImageUrl) await CleanupUnusedImageAsync(oldImage);
                        return MenuItemOperationStatus.Success;
                    }
                    catch (OverflowException)
                    {
                        if (item != null) _context.Entry(item).State = EntityState.Detached;
                        await CleanupUnusedImageAsync(newImage);
                        return MenuItemOperationStatus.CodeLimitReached;
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        if (item != null) _context.Entry(item).State = EntityState.Detached;
                        await CleanupUnusedImageAsync(newImage);
                        return MenuItemOperationStatus.NotFound;
                    }
                    catch (DbUpdateException exception) when (IsDuplicateName(exception))
                    {
                        if (item != null) _context.Entry(item).State = EntityState.Detached;
                        await CleanupUnusedImageAsync(newImage);
                        return MenuItemOperationStatus.DuplicateName;
                    }
                    catch (DbUpdateException exception) when (
                        exception.GetBaseException() is SqlException sql && sql.Number == 547)
                    {
                        if (item != null) _context.Entry(item).State = EntityState.Detached;
                        await CleanupUnusedImageAsync(newImage);
                        return MenuItemOperationStatus.InvalidCategory;
                    }
                    catch (Exception exception) when (
                        exception.GetBaseException() is SqlException sql && sql.Number == 1205)
                    {
                        if (item != null) _context.Entry(item).State = EntityState.Detached;
                        await CleanupUnusedImageAsync(newImage);
                        if (attempt == 2) return MenuItemOperationStatus.Busy;
                        await Task.Delay(50 * (attempt + 1), cancellationToken);
                    }
                    catch
                    {
                        if (item != null) _context.Entry(item).State = EntityState.Detached;
                        // Check DB references before cleanup if commit's outcome
                        // was uncertain; never delete an already-referenced photo.
                        await CleanupUnusedImageAsync(newImage);
                        throw;
                    }
                    finally { DetachMenuCodeSequences(); }
                }
                return MenuItemOperationStatus.Busy;
            });
        }

        public async Task<MenuItemOperationStatus> SetMenuItemAvailabilityAsync(int id, bool available,
            CancellationToken cancellationToken = default)
        {
            var item = await _context.MenuItems.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
            if (item is null) return MenuItemOperationStatus.NotFound;
            item.IsAvailable = available; // Set the desired state; never blindly invert on retry.
            try { await _context.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException)
            {
                _context.Entry(item).State = EntityState.Detached;
                return MenuItemOperationStatus.NotFound;
            }
            return MenuItemOperationStatus.Success;
        }

        public async Task<MenuItemOperationStatus> DeleteMenuItemAsync(int id,
            CancellationToken cancellationToken = default)
        {
            var item = await _context.MenuItems.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
            if (item is null) return MenuItemOperationStatus.NotFound;
            if (await _context.OrderItems.AnyAsync(i => i.MenuItemId == id, cancellationToken))
                return MenuItemOperationStatus.HasOrders;
            _context.MenuItems.Remove(item);
            try { await _context.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException)
            {
                _context.Entry(item).State = EntityState.Detached;
                return MenuItemOperationStatus.NotFound;
            }
            catch (DbUpdateException exception) when (
                exception.GetBaseException() is SqlException sql && sql.Number == 547)
            {
                _context.Entry(item).State = EntityState.Detached;
                return MenuItemOperationStatus.HasOrders;
            }
            await CleanupUnusedImageAsync(item.ImageUrl);
            return MenuItemOperationStatus.Success;
        }

        private async Task<(MenuItemOperationStatus Status, string? Url)> StoreImageAsync(
            IFormFile image, CancellationToken cancellationToken)
        {
            if (image.Length > MenuItemOptions.MaxImageBytes)
                return (MenuItemOperationStatus.ImageTooLarge, null);
            string extension = Path.GetExtension(image.FileName).ToLowerInvariant();
            if (extension == ".jpeg") extension = ".jpg";
            string? mime = extension switch
            { ".jpg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => null };
            if (image.Length <= 0 || mime is null ||
                !string.Equals(image.ContentType, mime, StringComparison.OrdinalIgnoreCase))
                return (MenuItemOperationStatus.InvalidImage, null);

            // Read at most the size limit + one byte, even if length metadata is wrong.
            using var buffer = new MemoryStream();
            await using (var input = image.OpenReadStream())
            {
                byte[] chunk = new byte[81920];
                while (true)
                {
                    int remaining = (int)(MenuItemOptions.MaxImageBytes + 1 - buffer.Length);
                    int read = await input.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), cancellationToken);
                    if (read == 0) break;
                    await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
                    if (buffer.Length > MenuItemOptions.MaxImageBytes)
                        return (MenuItemOperationStatus.ImageTooLarge, null);
                }
            }
            byte[] bytes = buffer.ToArray();
            if (!HasImageSignature(bytes, extension)) return (MenuItemOperationStatus.InvalidImage, null);
            string fileName = Guid.NewGuid().ToString("N") + extension;
            string path = Path.Combine(_imageDirectory, fileName);
            try
            {
                Directory.CreateDirectory(_imageDirectory);
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await output.WriteAsync(bytes.AsMemory(), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                DeleteNewUpload("~/menu-images/" + fileName);
                _logger.LogWarning(exception, "Could not save a menu image.");
                return (MenuItemOperationStatus.ImageStorageUnavailable, null);
            }
            catch
            {
                DeleteNewUpload("~/menu-images/" + fileName);
                throw;
            }
            return (MenuItemOperationStatus.Success, "~/menu-images/" + fileName);
        }

        private static bool HasImageSignature(byte[] b, string extension)
        {
            if (extension == ".jpg")
                return b.Length >= 4 && b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff
                    && b[^2] == 0xff && b[^1] == 0xd9;
            if (extension == ".png")
                return b.Length >= 33 && b[0] == 137 && b[1] == 80 && b[2] == 78 && b[3] == 71
                    && b[4] == 13 && b[5] == 10 && b[6] == 26 && b[7] == 10
                    && b[12] == 73 && b[13] == 72 && b[14] == 68 && b[15] == 82;
            return extension == ".webp" && b.Length >= 20 && b[0] == 82 && b[1] == 73
                && b[2] == 70 && b[3] == 70 && b[8] == 87 && b[9] == 69 && b[10] == 66 && b[11] == 80
                && b[12] == 86 && b[13] == 80 && b[14] == 56 && (b[15] == 32 || b[15] == 76 || b[15] == 88);
        }

        private static string? OwnedFileName(string? url)
        {
            const string prefix = "~/menu-images/";
            if (url is null || !url.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string name = url.Substring(prefix.Length);
            return Regex.IsMatch(name, @"\A[a-f0-9]{32}\.(jpg|png|webp)\z") ? name : null;
        }
        private void DeleteNewUpload(string? url)
        {
            string? name = OwnedFileName(url);
            if (name is null) return;
            try { File.Delete(Path.Combine(_imageDirectory, name)); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { _logger.LogWarning(exception, "Could not remove an unused menu image."); }
        }
        private async Task CleanupUnusedImageAsync(string? url)
        {
            if (OwnedFileName(url) is null) return;
            try
            {
                if (!await _context.MenuItems.AsNoTracking().AnyAsync(i => i.ImageUrl == url)) DeleteNewUpload(url);
            }
            catch (Exception exception)
            { _logger.LogWarning(exception, "Menu image cleanup deferred; the item change was saved."); }
        }
        public async Task<MenuImageFile?> OpenMenuImageAsync(string fileName,
            CancellationToken cancellationToken = default)
        {
            string url = "~/menu-images/" + fileName;
            if (OwnedFileName(url) is null ||
                !await _context.MenuItems.AsNoTracking().AnyAsync(i => i.ImageUrl == url, cancellationToken)) return null;
            string type = Path.GetExtension(fileName) switch
            { ".jpg" => "image/jpeg", ".png" => "image/png", _ => "image/webp" };
            try
            {
                return new MenuImageFile(new FileStream(Path.Combine(_imageDirectory, fileName),
                FileMode.Open, FileAccess.Read, FileShare.Read), type);
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }
    }
}
