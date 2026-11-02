using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin.Promotions;

// Owner: Member 3 (US29). Admin: promotional prices for a date range.
[Authorize(Policy = "AdminOnly")]
public class IndexModel(IPromotionService promotions) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int? Edit { get; set; }
    [BindProperty(SupportsGet = true)] public bool New { get; set; }
    [BindProperty(SupportsGet = true)] public int? Copy { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    // Calendar: the day picked (filters the list) and the month shown ("2026-11").
    [BindProperty(SupportsGet = true)] public DateTime? Day { get; set; }
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }
    // Paging: 9 cards per page (3 rows of 3); Page = 0 shows all.
    [BindProperty(SupportsGet = true, Name = "PageNo")] public int? PageNumber { get; set; }
    public const int PageSize = 9;
    public bool ShowAll => PageNumber == 0;
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public int MatchingCount { get; private set; }
    public IReadOnlyList<PromotionRow> PageRows { get; private set; } = Array.Empty<PromotionRow>();
    public int ScheduledTotal { get; private set; }

    [BindProperty] public PromotionInput Input { get; set; } = new();


    public PromotionBoard Board { get; private set; } = new(Array.Empty<PromotionRow>(), 0, 0, 0, 0, 0);
    public IReadOnlyList<PromotionDishOption> Dishes { get; private set; } = Array.Empty<PromotionDishOption>();
    public bool OpenForm { get; private set; }
    // "new", "edit" or "copy": the pop-up title and the New button behaviour depend on it.
    public string FormMode => Input.Id != 0 ? "edit" : Copy.HasValue ? "copy" : "new";
    public string? FormError { get; private set; }
    public string? FormErrorField { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Edit is int id)
        {
            var input = await promotions.GetInputAsync(id, cancellationToken);
            if (input is null)
            {
                TempData["ErrorMessage"] = "This promotion no longer exists.";
                return Redirect(PageUrl());
            }
            Input = input;
            OpenForm = true;
        }
        else if (Copy is int copyId)
        {
            var input = await promotions.GetCopyAsync(copyId, cancellationToken);
            if (input is null)
            {
                TempData["ErrorMessage"] = "This promotion no longer exists.";
                return Redirect(PageUrl());
            }
            Input = input;
            OpenForm = true;
        }
        else if (New)
        {
            OpenForm = true;
        }
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var result = await promotions.SaveAsync(Input, cancellationToken);
        if (result.Success)
        {
            TempData["SuccessMessage"] = result.Message;   // team toast (shared layout)
            return Redirect(PageUrl());
        }
        FormError = result.Message;
        FormErrorField = result.Field;
        OpenForm = true;
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostStopAsync(int id, CancellationToken cancellationToken)
    {
        var result = await promotions.StopAsync(id, cancellationToken);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return Redirect(PageUrl());
    }

    public async Task<IActionResult> OnPostResumeAsync(int id, CancellationToken cancellationToken)
    {
        var result = await promotions.ResumeAsync(id, cancellationToken);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return Redirect(PageUrl());
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var result = await promotions.DeleteAsync(id, cancellationToken);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return Redirect(PageUrl());
    }

    public string ListUrl() => PageUrl();

    public DateTime CalendarMonth { get; private set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public IReadOnlyList<CalendarDay> CalendarDays { get; private set; } = Array.Empty<CalendarDay>();
    public IReadOnlyList<PromotionRow> ComingNext { get; private set; } = Array.Empty<PromotionRow>();

    // Keeps the current filters in every link; pass only what changes.
    public string PageUrl(object? changes = null)
    {
        var values = new RouteValueDictionary(new { Status, Search, Sort, Day = Day?.ToString("yyyy-MM-dd"), Month,
            PageNo = PageNumber is null or 1 ? null : PageNumber });
        if (changes is not null)
            foreach (var pair in new RouteValueDictionary(changes)) values[pair.Key] = pair.Value;
        return Url.Page("./Index", values)!;
    }

    public string? FieldError(string field) => FormErrorField == field ? FormError : null;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Board = await promotions.GetBoardAsync(Status, Search, Sort, Day, cancellationToken);
        Dishes = await promotions.GetDishOptionsAsync(cancellationToken);
        BuildCalendar();
        var scheduled = Board.All.Where(r => r.State == PromotionState.Scheduled)
            .OrderBy(r => r.StartDate).ThenBy(r => r.DishName).ToList();
        ScheduledTotal = scheduled.Count;
        ComingNext = scheduled.Take(3).ToList();

        MatchingCount = Board.Rows.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(MatchingCount / (double)PageSize));
        CurrentPage = ShowAll ? 1 : Math.Clamp(PageNumber ?? 1, 1, TotalPages);
        PageRows = ShowAll ? Board.Rows : Board.Rows.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();
    }

    // Monday-first month grid. Each day shows a dot for promotions switched on that day:
    // green = running, gold = scheduled, grey = ended (stopped ones are not shown).
    private void BuildCalendar()
    {
        var today = DateTime.Today;
        var month = Day is DateTime picked ? new DateTime(picked.Year, picked.Month, 1) : new DateTime(today.Year, today.Month, 1);
        if (DateTime.TryParseExact(Month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var asked))
            month = new DateTime(asked.Year, asked.Month, 1);
        CalendarMonth = month;
        int offset = ((int)month.DayOfWeek + 6) % 7;            // days before the 1st, Monday first
        var first = month.AddDays(-offset);
        int cells = (int)Math.Ceiling((offset + DateTime.DaysInMonth(month.Year, month.Month)) / 7.0) * 7;
        var shown = Board.All.Where(r => r.State != PromotionState.Stopped).ToList();
        CalendarDays = Enumerable.Range(0, cells).Select(i =>
        {
            var date = first.AddDays(i);
            var on = shown.Where(r => r.StartDate.Date <= date && r.EndDate.Date >= date).ToList();
            string? dot = on.Count == 0 ? null
                : on.Any(r => r.State == PromotionState.Running) ? "Running"
                : on.Any(r => r.State == PromotionState.Scheduled) ? "Scheduled" : "Ended";
            return new CalendarDay(date, date.Month == month.Month, date == today, Day?.Date == date, dot, on.Count);
        }).ToList();
    }
}

public sealed record CalendarDay(DateTime Date, bool InMonth, bool IsToday, bool IsSelected, string? Dot, int Count);
