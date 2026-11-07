namespace RROMS.Services.Interfaces;

// Owner: Member 3 (US29). Admin management of promotional prices.
public interface IPromotionService
{
    // sort: "newest" (default), "ending", "discount", "sold". day: only promotions switched on for that date.
    Task<PromotionBoard> GetBoardAsync(string? status, string? search, string? sort = null, DateTime? day = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PromotionDishOption>> GetDishOptionsAsync(CancellationToken cancellationToken = default);
    Task<PromotionInput?> GetInputAsync(int id, CancellationToken cancellationToken = default);
    // A new promotion pre-filled from an old one, with the same length starting today.
    Task<PromotionInput?> GetCopyAsync(int id, CancellationToken cancellationToken = default);
    Task<PromotionResult> SaveAsync(PromotionInput input, CancellationToken cancellationToken = default);
    Task<PromotionResult> StopAsync(int id, CancellationToken cancellationToken = default);
    // Switches a stopped promotion back on (same date and overlap checks as saving).
    Task<PromotionResult> ResumeAsync(int id, CancellationToken cancellationToken = default);
    Task<PromotionResult> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public enum PromotionState { Running, Scheduled, Ended, Stopped }
public enum PromotionKind { Percent, FixedPrice }

public sealed record PromotionRow(int Id, string Title, int MenuItemId, string DishName, string DishCode,
    string CategoryName, string? ImageUrl, bool DishAvailable, decimal BasePrice, decimal PromoPrice,
    decimal PercentOff, PromotionKind Kind, DateTime StartDate, DateTime EndDate, PromotionState State,
    string Timing, PromotionResults Results)
{
    // Warnings shown on the admin card.
    public bool NoEffect => PercentOff <= 0;   // e.g. the dish's normal price was later lowered below the fixed price
    public bool CanRunAgain => State is PromotionState.Ended or PromotionState.Stopped;
}

// Sales of the dish at the promotional price during the promotion's dates (cancelled orders excluded).
// Counts orders placed after US29 saved the normal price on order items.
public sealed record PromotionResults(int Sold, decimal Revenue, decimal CustomersSaved)
{
    public static PromotionResults None { get; } = new(0, 0m, 0m);
}

// Rows = the filtered list; All = every promotion (for the calendar and "Coming next").
public sealed record PromotionBoard(IReadOnlyList<PromotionRow> Rows,
    int RunningCount, int ScheduledCount, int EndedCount, int StoppedCount, int TotalCount)
{
    public IReadOnlyList<PromotionRow> All { get; init; } = Array.Empty<PromotionRow>();
    public string Sort { get; init; } = PromotionSorts.Newest;
}

public static class PromotionSorts
{
    public const string Newest = "newest", Ending = "ending", Discount = "discount", Sold = "sold";
    public static string Normalize(string? value) => value switch
    {
        Ending or Discount or Sold => value!,
        _ => Newest
    };
}

public sealed record PromotionDishOption(int Id, string Name, string Code, string CategoryName,
    decimal Price, bool IsAvailable, string? ImageUrl = null);

public sealed class PromotionInput
{
    public int Id { get; set; }
    public int MenuItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public PromotionKind Kind { get; set; } = PromotionKind.Percent;
    public decimal? DiscountPercent { get; set; }
    public decimal? PromoPrice { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today.AddDays(7);
    public bool IsActive { get; set; } = true;
}

public sealed record PromotionResult(bool Success, string Message, string? Field = null);
