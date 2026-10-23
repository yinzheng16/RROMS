using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RROMS.Services.Interfaces
{
    // Owner: Member 3. Extend this interface for later menu/order stories.
    public partial interface IOrderService
    {
        Task<IReadOnlyList<MenuCategorySummary>> GetCategoriesAsync(
            CancellationToken cancellationToken = default,
            string? searchTerm = null, string? sortOrder = null);

        Task<MenuCategoryPage> GetCategoryPageAsync(
            int pageNumber, int pageSize,
            CancellationToken cancellationToken = default,
            string? searchTerm = null, string? sortOrder = null,
            bool showAll = false);

        Task<MenuCategorySummary?> GetCategoryAsync(
            int id, CancellationToken cancellationToken = default);

        Task<CategoryOperationStatus> CreateCategoryAsync(
            string name, CancellationToken cancellationToken = default);

        Task<CategoryOperationStatus> UpdateCategoryAsync(
            int id, string name, CancellationToken cancellationToken = default);

        Task<CategoryOperationStatus> ReorderCategoriesAsync(
            IReadOnlyList<int> orderedIds, string? expectedStamp,
            CancellationToken cancellationToken = default);

        Task<CategoryOperationStatus> DeleteCategoryAsync(
            int id, CancellationToken cancellationToken = default);
    }

    // Service output only: these types are NOT database entities.
    public sealed record MenuCategorySummary(
        int Id, string Name, int DisplayOrder, int ItemCount, string Code = "");

    // Page rows plus totals for the entire filtered view, not just this page.
    // This is a service result, not an EF entity or a new database table.
    public sealed record MenuCategoryPage(
        IReadOnlyList<MenuCategorySummary> Items,
        int TotalCount, int TotalItemCount, int EmptyCategoryCount,
        int PageNumber, int PageSize, string OrderStamp = "")
    {
        public int TotalPages => TotalCount == 0 ? 1 : (TotalCount - 1) / PageSize + 1;
    }

    // A fixed list of sort keys: request text is never used as a SQL expression.
    public static class MenuCategorySorting
    {
        public const string Default = "display";

        public static string Normalize(string? sortOrder)
        {
            return sortOrder switch
            {
                "display-desc" => "display-desc",
                "name" => "name",
                "name-desc" => "name-desc",
                "items" => "items",
                "items-desc" => "items-desc",
                _ => Default
            };
        }
    }

    // This enum is not persisted, so no EF enum conversion is needed.
    public enum CategoryOperationStatus
    {
        Success,
        NotFound,
        InvalidName,
        DuplicateName,
        HasMenuItems,
        DisplayOrderLimitReached,
        Busy,
        InvalidOrder,
        OrderChanged,
        CodeLimitReached
    }
}
