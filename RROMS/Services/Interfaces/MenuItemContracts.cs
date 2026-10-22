using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace RROMS.Services.Interfaces
{
    // Owner: Member 3. DTOs and statuses only; no EF entities or migrations.
    public partial interface IOrderService
    {
        Task<MenuItemPage> GetMenuItemPageAsync(int pageNumber, int pageSize,
            string? searchTerm = null, int? categoryId = null,
            string? availability = null, string? sortOrder = null,
            bool showAll = false, CancellationToken cancellationToken = default);
        Task<MenuItemSummary?> GetMenuItemAsync(int id, CancellationToken cancellationToken = default);
        Task<MenuItemOperationStatus> SaveMenuItemAsync(int? id, MenuItemWrite input,
            IFormFile? image, bool removeImage, CancellationToken cancellationToken = default);
        Task<MenuItemOperationStatus> SetMenuItemAvailabilityAsync(int id, bool available,
            CancellationToken cancellationToken = default);
        Task<MenuItemOperationStatus> DeleteMenuItemAsync(int id, CancellationToken cancellationToken = default);
        Task<MenuImageFile?> OpenMenuImageAsync(string fileName, CancellationToken cancellationToken = default);

        // Customer menu/cart code must use these accessors, not the admin list.
        Task<IReadOnlyList<MenuItemSummary>> GetOrderableMenuItemsAsync(
            CancellationToken cancellationToken = default);
        Task<MenuItemSummary?> GetOrderableMenuItemAsync(int id,
            CancellationToken cancellationToken = default);
    }

    public sealed record MenuItemWrite(int CategoryId, string Name,
        string? Description, decimal Price, bool IsAvailable);
    public sealed record MenuItemSummary(int Id, int CategoryId, string CategoryName,
        string Name, string? Description, decimal Price, string? ImageUrl,
        bool IsAvailable, DateTime CreatedAt, bool HasOrders,
        string Code = "", string CategoryCode = "");
    public sealed record MenuItemPage(IReadOnlyList<MenuItemSummary> Items,
        int TotalCount, int AvailableCount, int UnavailableCount, int PageNumber, int PageSize)
    {
        public int TotalPages => TotalCount == 0 ? 1 : (TotalCount - 1) / PageSize + 1;
    }
    public sealed record MenuImageFile(Stream Content, string ContentType);
    public enum MenuItemOperationStatus
    {
        Success, NotFound, InvalidName, InvalidDescription, InvalidPrice,
        InvalidCategory, InvalidImage, ImageTooLarge, ImageStorageUnavailable, HasOrders,
        DuplicateName, Busy, CodeLimitReached
    }
    public static class MenuItemOptions
    {
        public const long MaxImageBytes = 5 * 1024 * 1024;
        public static string Availability(string? value) => value switch
        { "available" => "available", "unavailable" => "unavailable", _ => "all" };
        public static string Sort(string? value) => value switch
        {
            "name-desc" => "name-desc",
            "price" => "price",
            "price-desc" => "price-desc",
            "newest" => "newest",
            "oldest" => "oldest",
            _ => "name"
        };
    }
}
