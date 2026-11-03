// Owner: Member 2 — US13 My Reservations / cancellation.
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer.Reservations;

[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(
    IMyReservationsService reservations,
    IReservationService booking,
    INotificationService notifications,
    UserManager<ApplicationUser> users) : PageModel
{
    private readonly IMyReservationsService _reservations = reservations;

    public IReadOnlyList<MyReservationItem> Reservations { get; private set; } = Array.Empty<MyReservationItem>();
    // US18: finished bookings (Completed, Cancelled, No-show), newest first.
    public IReadOnlyList<MyHistoryItem> History { get; private set; } = Array.Empty<MyHistoryItem>();
    public int NoShowGraceMinutes => BookingRules.NoShowGraceMinutes;
    // Only bookings that can still be cancelled count toward the US12 limit (ReservedAt > now).
    public int UpcomingCount => Reservations.Count(r => r.CanCancel);
    public int MaxActiveBookings => BookingRules.MaxActiveBookings;
    public string CustomerName { get; private set; } = "Guest";
    // US15: the days a booking can be moved to, and the largest party a table can seat.
    public IReadOnlyList<string> BookableDates { get; private set; } = Array.Empty<string>();
    public int MaxGuests { get; private set; } = 2;
    public static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("en-MY");

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var user = await users.GetUserAsync(User);
        CustomerName = string.IsNullOrWhiteSpace(user?.FullName) ? "Guest" : user!.FullName;
        History = await _reservations.GetMyHistoryAsync(userId, cancellationToken);
        Reservations = await _reservations.GetMyUpcomingReservationsAsync(userId, cancellationToken);
        BookableDates = await booking.GetBookableDatesAsync();
        MaxGuests = await booking.GetMaxPartySizeAsync();
        return Page();
    }

    // ---------- US18: live status ----------

    // Polled by the page every few seconds. Only the customer's own bookings, and only their status.
    public async Task<IActionResult> OnGetStatusesAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var list = await _reservations.GetMyStatusesAsync(userId, cancellationToken);
        return new JsonResult(new
        {
            items = list.Select(x => new { id = x.Id, status = x.Status, table = x.TableNumber })
        });
    }

    // ---------- US15: change a booking ----------

    private static bool TryStart(string? date, string? time, out DateTime start)
    {
        start = default;

        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day))
            return false;

        if (!TimeSpan.TryParseExact(time, "hh\\:mm", CultureInfo.InvariantCulture, out var clock))
            return false;

        start = day.Date + clock;
        return true;
    }

    // The times offered on one date (already without times less than 1 hour from now).
    public async Task<IActionResult> OnGetSlotsAsync(string? date)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day))
            return new JsonResult(new { error = "Choose a valid date." }) { StatusCode = 400 };

        var slots = await booking.GetTimeSlotsAsync(day);
        return new JsonResult(new { slots });
    }

    // Can the booking be changed like this? Shows the reason, nearby times or other table types when not.
    public async Task<IActionResult> OnGetCheckChangeAsync(
        int id, string? date, string? time, int guests, int? typeId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        if (!TryStart(date, time, out var start))
            return new JsonResult(new { valid = false, available = false, message = "Choose a valid date and time.", suggestions = Array.Empty<object>() });

        var r = await _reservations.CheckMyChangeAsync(userId, id, start, guests, typeId, cancellationToken);

        return new JsonResult(new
        {
            valid = r.Valid,
            available = r.Available,
            message = r.Message,
            backToPending = r.BackToPending,
            suggestions = r.Suggestions.Select(x => new { date = x.Date, time = x.Time, label = x.Label, kind = x.Kind }),
            typeOptions = r.TypeOptions?.Select(o => new
            {
                id = o.Id,
                name = o.Name,
                japaneseName = o.JapaneseName,
                minGuests = o.MinGuests,
                maxGuests = o.MaxGuests,
                minimumSpend = o.MinimumSpend,
                tablesLeft = o.TablesLeft
            })
        });
    }

    // The customer id always comes from the logged-in user, never from the form.
    public async Task<IActionResult> OnPostChangeAsync(
        int id, string? date, string? time, int guests, int? typeId, bool acceptSpend, decimal expectedSpend,
        string? specialRequest, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        if (!TryStart(date, time, out var start))
            return new JsonResult(new { success = false, code = "slot", message = "Choose a valid date and time." })
            { StatusCode = StatusCodes.Status409Conflict };

        var result = await _reservations.ChangeMyReservationAsync(
            userId, id, start, guests, typeId, acceptSpend, expectedSpend, specialRequest, cancellationToken);

        if (result.Success && result.NewReservedAt is { } at)
        {
            var when = $"{at.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} at " +
                       $"{at.ToString("h:mm tt", CultureInfo.InvariantCulture)} for " +
                       $"{result.NewGuests} {(result.NewGuests == 1 ? "guest" : "guests")}";
            var follow = result.BackToPending
                ? "Our team will confirm the new details."
                : result.RequestChanged
                    ? "Your booking stays confirmed and our team can see your updated request."
                    : "Your booking stays as it was.";

            // A failed notification must never undo a saved change.
            try
            {
                await notifications.SendAsync(
                    userId,
                    "Reservation modified",
                    $"Your reservation {result.ReferenceNo} is now on {when}. {follow}",
                    NotificationType.Reservation);
            }
            catch
            {
                // Intentionally ignored: the customer already sees the result on screen.
            }

            TempData["SuccessMessage"] = result.BackToPending
                ? $"{result.ReferenceNo} has been modified to {when}. It is waiting for confirmation again."
                : result.RequestChanged
                    ? $"{result.ReferenceNo} has been updated. Our team can see your new details."
                    : $"{result.ReferenceNo} now has {result.NewGuests} {(result.NewGuests == 1 ? "guest" : "guests")}.";
        }

        return new JsonResult(new { success = result.Success, code = result.Code, message = result.Message })
        {
            StatusCode = result.Success ? StatusCodes.Status200OK
                : result.Code == "not_found" ? StatusCodes.Status404NotFound
                : StatusCodes.Status409Conflict
        };
    }

    // Razor Pages automatically validates the antiforgery token on POST.
    // The customer id always comes from authenticated claims, never from the form.
    public async Task<IActionResult> OnPostCancelAsync(int id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        // Read the booking first so the notification can name it (the cancel result carries no details).
        var before = (await _reservations.GetMyUpcomingReservationsAsync(userId, cancellationToken))
            .FirstOrDefault(r => r.Id == id);

        var result = await _reservations.CancelMyReservationAsync(userId, id, cancellationToken);

        // Only the request that really changed the row notifies; a retried POST ("already_cancelled") does not.
        if (result.Success && result.Code == "cancelled" && before is not null)
        {
            // A failed notification must never undo a saved cancellation (same rule as US12).
            try
            {
                await notifications.SendAsync(
                    userId,
                    "Reservation cancelled",
                    $"Your reservation {before.ReferenceNo} on " +
                    $"{before.ReservedAt.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} at " +
                    $"{before.ReservedAt.ToString("h:mm tt", CultureInfo.InvariantCulture)} has been cancelled.",
                    NotificationType.Reservation);
            }
            catch
            {
                // Intentionally ignored: the customer already sees the result on screen.
            }
        }
        // Shown by the shared popup in _Layout (Member 1) on the page reload that follows a successful cancel.
        if (result.Success && before is not null)
            TempData["SuccessMessage"] = $"{before.ReferenceNo} has been cancelled. Your table has been released.";

        return new JsonResult(new { success = result.Success, code = result.Code, message = result.Message })
        {
            StatusCode = result.Success ? StatusCodes.Status200OK
                : result.Code == "not_found" ? StatusCodes.Status404NotFound
                : StatusCodes.Status409Conflict
        };
    }

    public static long RestaurantEpoch(DateTime date) => new DateTimeOffset(
        DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.FromHours(8)).ToUnixTimeMilliseconds();

    public static string Seating(MyReservationItem r) => string.IsNullOrWhiteSpace(r.JapaneseName)
        ? r.TableTypeName : $"{r.TableTypeName} / {r.JapaneseName}";

    // 12-hour clock with AM/PM everywhere, so 12:00 can never be mistaken for midnight.
    public static string Clock(DateTime at) => at.ToString("h:mm tt", CultureInfo.InvariantCulture);

    // The same time in two parts, so the page can show AM/PM smaller.
    public static (string Time, string Meridiem) ClockParts(DateTime at) =>
        (at.ToString("h:mm", CultureInfo.InvariantCulture), at.ToString("tt", CultureInfo.InvariantCulture));
    public static string When(DateTime at)
    {
        var days = (at.Date - DateTime.Now.Date).Days;
        return days <= 0 ? "Today" : days == 1 ? "Tomorrow" : $"In {days} days";
    }

    // The dining area (e.g. "Main hall") of the table type, shown under the table type name.
    public static string? Area(MyReservationItem r) =>
        string.IsNullOrWhiteSpace(r.AreaName) ? null : r.AreaName.Trim();

    // The symbol of the dining area icon (see _AreaIcons.cshtml). An unknown name falls back to the map pin.
    public static string AreaIconHref(string? icon) =>
        "#area-" + (!string.IsNullOrEmpty(icon) && icon.All(c => (c >= 'a' && c <= 'z') || c == '-') ? icon : "map-pin");

    // The table number is shown only after staff confirm the booking. It can still change on the day.
    public static string? TableNumber(MyReservationItem r) =>
        (r.Status == "Confirmed" || r.Status == "Seated") && !string.IsNullOrWhiteSpace(r.TableNumber) ? r.TableNumber.Trim() : null;

    // ---------- US18: status badges and the progress tracker ----------

    public static string StatusClass(string status) => status switch
    {
        "Confirmed" => "is-confirmed",
        "Seated" => "is-seated",
        "Completed" => "is-completed",
        "Cancelled" => "is-cancelled",
        "NoShow" => "is-noshow",
        _ => "is-pending"
    };

    public static string StatusIcon(string status) => status switch
    {
        "Confirmed" or "Completed" => "#myr-check",
        "Seated" => "#myr-table",
        "Cancelled" => "#myr-cross",
        "NoShow" => "#myr-clock",
        _ => "#myr-hourglass"
    };

    public static string StatusLabel(string status) => status == "NoShow" ? "No-show" : status;

    // A short sentence that goes with the badge.
    public static string StatusNote(string status) => status switch
    {
        "Pending" => "Awaiting restaurant confirmation.",
        "Confirmed" => "We look forward to welcoming you.",
        "Seated" => "You are being served. Enjoy your evening.",
        "Completed" => "Thank you for dining with us.",
        "Cancelled" => "This reservation was cancelled.",
        "NoShow" => "The table was released after the booking time.",
        _ => ""
    };

    // The progress tracker: Pending, Confirmed, Seated, Completed. A booking that ended early stops
    // where it was and finishes with a red Cancelled or No-show step.
    // State is "done", "now", "todo" or "bad". `reached` lists the steps the booking really went through.
    public static List<(string Label, string State)> Track(string status, IReadOnlyCollection<string>? reached = null)
    {
        var main = new[] { "Pending", "Confirmed", "Seated", "Completed" };

        if (status is "Cancelled" or "NoShow")
        {
            var stopped = new List<(string, string)>();
            foreach (var step in main)
                if (step == "Pending" || (reached?.Contains(step) ?? false))
                    stopped.Add((step, "done"));
            stopped.Add((StatusLabel(status), "bad"));
            return stopped;
        }

        var at = Array.IndexOf(main, status);
        return main.Select((step, i) =>
            (step, i < at || (i == at && status == "Completed") ? "done" : i == at ? "now" : "todo")).ToList();
    }

    public static string StepTime(DateTime at) =>
        at.ToString("d MMM yyyy", CultureInfo.InvariantCulture) + ", " + Clock(at);

    // The wax seal shrinks its text for longer table numbers (up to about 7 characters).
    public static string SealSize(string number) => number.Length switch
    {
        <= 3 => "s3",
        4 => "s4",
        5 => "s5",
        6 => "s6",
        _ => "s7"
    };

    public static string Spend(MyReservationItem r) => r.MinimumSpend > 0m
        ? $"RM {r.MinimumSpend.ToString("N0", DisplayCulture)} per booking" : "No minimum spend";
}