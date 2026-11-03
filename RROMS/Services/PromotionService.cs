using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

// Owner: Member 3 (US29). Admins set a percentage or fixed promotional price for a date range.
// Promotions end by themselves after their end date (see PromotionRules.Running).
// Saved orders keep the price they were placed at, so editing or deleting a promotion never
// changes past orders.
public sealed class PromotionService(ApplicationDbContext context) : IPromotionService
{
    public const int TitleMaxLength = 100;
    public const int MaxDays = 366;

    public async Task<PromotionBoard> GetBoardAsync(string? status, string? search, string? sort = null,
        DateTime? day = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var saved = await context.Promotions.AsNoTracking()
            .Select(p => new
            {
                p.Id, p.Title, p.MenuItemId, p.DiscountPercent, p.PromoPrice, p.StartDate, p.EndDate, p.IsActive,
                DishName = p.MenuItem.Name, DishCode = p.MenuItem.Code, Category = p.MenuItem.Category.Name,
                p.MenuItem.ImageUrl, p.MenuItem.IsAvailable, BasePrice = p.MenuItem.Price
            })
            .ToListAsync(cancellationToken);

        // Results: promotional sales of these dishes (order items that saved a normal price above the paid price).
        int[] dishIds = saved.Select(p => p.MenuItemId).Distinct().ToArray();
        var sales = await context.OrderItems.AsNoTracking()
            .Where(oi => dishIds.Contains(oi.MenuItemId) && oi.NormalUnitPrice != null
                && oi.UnitPrice < oi.NormalUnitPrice && oi.Order.Status != OrderStatus.Cancelled)
            .Select(oi => new { oi.MenuItemId, oi.Quantity, oi.UnitPrice, Normal = oi.NormalUnitPrice!.Value, oi.Order.CreatedAt })
            .ToListAsync(cancellationToken);

        PromotionResults ResultsFor(int dishId, DateTime start, DateTime end)
        {
            DateTime from = start.Date, until = end.Date.AddDays(1);
            var lines = sales.Where(s => s.MenuItemId == dishId && s.CreatedAt >= from && s.CreatedAt < until).ToList();
            return lines.Count == 0 ? PromotionResults.None : new(lines.Sum(s => s.Quantity),
                lines.Sum(s => s.UnitPrice * s.Quantity), lines.Sum(s => (s.Normal - s.UnitPrice) * s.Quantity));
        }

        var rows = saved
            .Select(p =>
            {
                var state = StateOf(p.IsActive, p.StartDate, p.EndDate, now);
                return new PromotionRow(p.Id, p.Title, p.MenuItemId, p.DishName, p.DishCode, p.Category,
                    p.ImageUrl, p.IsAvailable, p.BasePrice,
                    PromotionRules.PriceUnder(p.BasePrice, p.DiscountPercent, p.PromoPrice),
                    PromotionRules.EffectivePercent(p.BasePrice, p.DiscountPercent, p.PromoPrice),
                    p.PromoPrice.HasValue ? PromotionKind.FixedPrice : PromotionKind.Percent,
                    p.StartDate, p.EndDate, state, TimingOf(state, p.StartDate, p.EndDate, now),
                    ResultsFor(p.MenuItemId, p.StartDate, p.EndDate));
            })
            .ToList();

        int running = rows.Count(r => r.State == PromotionState.Running);
        int scheduled = rows.Count(r => r.State == PromotionState.Scheduled);
        int ended = rows.Count(r => r.State == PromotionState.Ended);
        int stopped = rows.Count(r => r.State == PromotionState.Stopped);

        IEnumerable<PromotionRow> shown = rows;
        if (Enum.TryParse<PromotionState>(status, true, out var wanted))
            shown = shown.Where(r => r.State == wanted);
        string term = (search ?? string.Empty).Trim();
        if (term.Length > 0)
            shown = shown.Where(r => r.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.DishName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.DishCode.Contains(term, StringComparison.OrdinalIgnoreCase));

        // A calendar day: promotions switched on for that date (stopped ones are left out).
        if (day is DateTime picked)
            shown = shown.Where(r => r.State != PromotionState.Stopped
                && r.StartDate.Date <= picked.Date && r.EndDate.Date >= picked.Date);

        string order = PromotionSorts.Normalize(sort);
        var ordered = (order switch
        {
            // Ending soonest: running first by end date, then scheduled by start date, then the rest.
            PromotionSorts.Ending => shown
                .OrderBy(r => r.State switch { PromotionState.Running => 0, PromotionState.Scheduled => 1, _ => 2 })
                .ThenBy(r => r.State == PromotionState.Scheduled ? r.StartDate : r.EndDate),
            PromotionSorts.Discount => shown.OrderByDescending(r => r.PercentOff),
            PromotionSorts.Sold => shown.OrderByDescending(r => r.Results.Sold).ThenByDescending(r => r.Results.Revenue),
            _ => shown.OrderByDescending(r => r.Id)
        }).ThenBy(r => r.DishName).ToList();
        return new(ordered, running, scheduled, ended, stopped, rows.Count) { All = rows, Sort = order };
    }

    public async Task<IReadOnlyList<PromotionDishOption>> GetDishOptionsAsync(CancellationToken cancellationToken = default)
        => await context.MenuItems.AsNoTracking()
            .OrderBy(m => m.Category.DisplayOrder).ThenBy(m => m.Name)
            .Select(m => new PromotionDishOption(m.Id, m.Name, m.Code, m.Category.Name, m.Price, m.IsAvailable, m.ImageUrl))
            .ToListAsync(cancellationToken);

    public async Task<PromotionInput?> GetInputAsync(int id, CancellationToken cancellationToken = default)
        => await context.Promotions.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new PromotionInput
            {
                Id = p.Id, MenuItemId = p.MenuItemId, Title = p.Title,
                Kind = p.PromoPrice.HasValue ? PromotionKind.FixedPrice : PromotionKind.Percent,
                DiscountPercent = p.PromoPrice.HasValue ? null : p.DiscountPercent,
                PromoPrice = p.PromoPrice,
                StartDate = p.StartDate.Date, EndDate = p.EndDate.Date, IsActive = p.IsActive
            })
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PromotionInput?> GetCopyAsync(int id, CancellationToken cancellationToken = default)
    {
        var source = await GetInputAsync(id, cancellationToken);
        if (source is null) return null;
        int days = Math.Max(0, (source.EndDate.Date - source.StartDate.Date).Days);
        source.Id = 0;
        source.StartDate = DateTime.Today;
        source.EndDate = DateTime.Today.AddDays(days);
        source.IsActive = true;
        return source;
    }

    public async Task<PromotionResult> SaveAsync(PromotionInput input, CancellationToken cancellationToken = default)
    {
        string title = (input.Title ?? string.Empty).Trim();
        if (title.Length == 0) return Fail("Enter a promotion title.", nameof(input.Title));
        if (title.Length > TitleMaxLength) return Fail($"Keep the title within {TitleMaxLength} characters.", nameof(input.Title));

        var dish = await context.MenuItems.AsNoTracking().Where(m => m.Id == input.MenuItemId)
            .Select(m => new { m.Id, m.Price }).SingleOrDefaultAsync(cancellationToken);
        if (dish is null) return Fail("Choose a dish from the menu.", nameof(input.MenuItemId));

        decimal percent;
        decimal? fixedPrice = null;
        if (input.Kind == PromotionKind.FixedPrice)
        {
            if (input.PromoPrice is not decimal price || price <= 0)
                return Fail("Enter the promotional price.", nameof(input.PromoPrice));
            price = OrderPricingRules.Money(price);
            if (price >= dish.Price)
                return Fail($"The promotional price must be lower than the normal price (RM {dish.Price:F2}).", nameof(input.PromoPrice));
            fixedPrice = price;
            // Saved for older readers only; prices are always worked out from PromoPrice.
            percent = Math.Max(0.01m, decimal.Round((dish.Price - price) / dish.Price * 100m, 2));
        }
        else
        {
            if (input.DiscountPercent is not decimal value || value < 1 || value > 99)
                return Fail("Enter a discount from 1% to 99%.", nameof(input.DiscountPercent));
            percent = decimal.Round(value, 2);
        }

        DateTime start = input.StartDate.Date, end = input.EndDate.Date;
        if (start == default || end == default) return Fail("Choose the start and end dates.", nameof(input.StartDate));
        if (end < start) return Fail("The end date must be on or after the start date.", nameof(input.EndDate));
        if ((end - start).TotalDays >= MaxDays) return Fail("A promotion can run for up to one year.", nameof(input.EndDate));

        Promotion? promotion = null;
        if (input.Id != 0)
        {
            promotion = await context.Promotions.SingleOrDefaultAsync(p => p.Id == input.Id, cancellationToken);
            if (promotion is null) return Fail("This promotion no longer exists. Refresh the page.");
        }
        // A new promotion cannot end in the past; an existing one may keep its past dates.
        if (promotion is null && end < DateTime.Today)
            return Fail("The end date has already passed.", nameof(input.EndDate));

        // One switched-on promotion per dish at a time keeps the customer price predictable.
        if (input.IsActive)
        {
            var clash = await context.Promotions.AsNoTracking()
                .Where(p => p.Id != input.Id && p.MenuItemId == dish.Id && p.IsActive
                    && p.StartDate.Date <= end && p.EndDate >= start)
                .OrderBy(p => p.StartDate)
                .Select(p => new { p.Title, p.StartDate, p.EndDate })
                .FirstOrDefaultAsync(cancellationToken);
            if (clash is not null)
                return Fail($"This dish already has \"{clash.Title}\" from {clash.StartDate:dd MMM yyyy} to {clash.EndDate:dd MMM yyyy}. " +
                    "Choose other dates, or stop that promotion first.", nameof(input.StartDate));
        }

        promotion ??= context.Promotions.Add(new Promotion()).Entity;
        promotion.MenuItemId = dish.Id;
        promotion.Title = title;
        promotion.DiscountPercent = percent;
        promotion.PromoPrice = fixedPrice;
        promotion.StartDate = start;
        promotion.EndDate = end;
        promotion.IsActive = input.IsActive;
        await context.SaveChangesAsync(cancellationToken);
        return new(true, input.Id == 0 ? $"\"{title}\" has been created." : $"\"{title}\" has been updated.");
    }

    public async Task<PromotionResult> StopAsync(int id, CancellationToken cancellationToken = default)
    {
        var promotion = await context.Promotions.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (promotion is null) return Fail("This promotion no longer exists. Refresh the page.");
        if (!promotion.IsActive) return new(true, $"\"{promotion.Title}\" is already stopped.");
        promotion.IsActive = false;
        await context.SaveChangesAsync(cancellationToken);
        return new(true, $"\"{promotion.Title}\" has been stopped. Customers now see the normal price.");
    }

    public async Task<PromotionResult> ResumeAsync(int id, CancellationToken cancellationToken = default)
    {
        var input = await GetInputAsync(id, cancellationToken);
        if (input is null) return Fail("This promotion no longer exists. Refresh the page.");
        if (input.IsActive) return new(true, $"\"{input.Title}\" is already switched on.");
        input.IsActive = true;
        var result = await SaveAsync(input, cancellationToken);
        return result.Success ? new(true, $"\"{input.Title}\" has been resumed.") : result;
    }

    public async Task<PromotionResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var promotion = await context.Promotions.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (promotion is null) return Fail("This promotion no longer exists. Refresh the page.");
        context.Promotions.Remove(promotion);
        await context.SaveChangesAsync(cancellationToken);
        return new(true, $"\"{promotion.Title}\" has been deleted. Past orders keep the price they were placed at.");
    }

    public static PromotionState StateOf(bool isActive, DateTime start, DateTime end, DateTime now)
        => !isActive ? PromotionState.Stopped
            : end.Date < now.Date ? PromotionState.Ended
            : start > now ? PromotionState.Scheduled
            : PromotionState.Running;

    // Short countdown for the admin card, e.g. "Ends in 3 days" or "Starts tomorrow".
    public static string TimingOf(PromotionState state, DateTime start, DateTime end, DateTime now)
    {
        int Days(DateTime date) => (date.Date - now.Date).Days;
        static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
        return state switch
        {
            PromotionState.Running => Days(end) switch
            {
                0 => "Ends today", 1 => "Ends tomorrow", var d => $"Ends in {Plural(d, "day")}"
            },
            PromotionState.Scheduled => Days(start) switch
            {
                <= 1 => "Starts tomorrow", var d => $"Starts in {Plural(d, "day")}"
            },
            PromotionState.Ended => Days(end) == -1 ? "Ended yesterday" : $"Ended {end:d MMM}",
            _ => "Paused"
        };
    }

    private static PromotionResult Fail(string message, string? field = null) => new(false, message, field);
}
