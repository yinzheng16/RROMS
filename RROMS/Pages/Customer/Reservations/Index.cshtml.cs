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
    INotificationService notifications,
    UserManager<ApplicationUser> users) : PageModel
{
    private readonly IMyReservationsService _reservations = reservations;

    public IReadOnlyList<MyReservationItem> Reservations { get; private set; } = Array.Empty<MyReservationItem>();
    public int NoShowGraceMinutes => BookingRules.NoShowGraceMinutes;
    // Only bookings that can still be cancelled count toward the US12 limit (ReservedAt > now).
    public int UpcomingCount => Reservations.Count(r => r.CanCancel);
    public int MaxActiveBookings => BookingRules.MaxActiveBookings;
    public string CustomerName { get; private set; } = "Guest";
    public static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("en-MY");

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var user = await users.GetUserAsync(User);
        CustomerName = string.IsNullOrWhiteSpace(user?.FullName) ? "Guest" : user!.FullName;
        Reservations = await _reservations.GetMyUpcomingReservationsAsync(userId, cancellationToken);
        return Page();
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

    public static string When(DateTime at)
    {
        var days = (at.Date - DateTime.Now.Date).Days;
        return days <= 0 ? "Today" : days == 1 ? "Tomorrow" : $"In {days} days";
    }

    public static string Spend(MyReservationItem r) => r.MinimumSpend > 0m
        ? $"RM {r.MinimumSpend.ToString("N0", DisplayCulture)} per booking" : "No minimum spend";
}