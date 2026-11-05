// Owner: Member 2 (Tables & Reservations) - US12 make a reservation
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer.Reservations;

[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CreateModel(
    IReservationService reservations,
    INotificationService notifications,
    UserManager<ApplicationUser> users,
    IProfileService profiles) : PageModel
{
    public string CustomerName { get; private set; } = "";
    public string? CustomerPhone { get; private set; }

    // From My profile (Member 1, US03): allergies, diet and notes. Used only to pre-fill an empty request box.
    public string? DiningSummary { get; private set; }
    public List<string> BookableDates { get; private set; } = new();
    public int MaxGuests { get; private set; } = 2;

    // Rules shown to the customer (same numbers the service enforces).
    public int DurationMinutes => BookingRules.DurationMinutes;
    public int MaxActiveBookings => BookingRules.MaxActiveBookings;
    public int NoShowGraceMinutes => BookingRules.NoShowGraceMinutes;

    // "1 hour" / "90 minutes", for the hint under the calendar.
    public string MinLeadText => BookingRules.MinLeadMinutes % 60 == 0
        ? $"{BookingRules.MinLeadMinutes / 60} hour{(BookingRules.MinLeadMinutes / 60 == 1 ? "" : "s")}"
        : $"{BookingRules.MinLeadMinutes} minutes";

    public class SubmitDto
    {
        public string? Date { get; set; }
        public string? Time { get; set; }
        public int Guests { get; set; }
        public int TableTypeId { get; set; }
        public string? SpecialRequest { get; set; }
        public bool AcceptedMinimumSpend { get; set; }
        public decimal ExpectedMinimumSpend { get; set; }
        public string? Token { get; set; }
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        CustomerName = user?.FullName ?? "";
        CustomerPhone = user?.PhoneNumber;

        // Read-only and always for the logged-in customer. A failure here must never stop the booking page.
        try
        {
            var summary = (await profiles.GetDiningSummaryAsync(UserId))?.Trim();
            DiningSummary = string.IsNullOrEmpty(summary) ? null
                : summary.Length > 500 ? summary[..500] : summary;   // the request box holds 500 characters
        }
        catch
        {
            DiningSummary = null;
        }

        await reservations.ReleaseOverdueReservationsAsync();
        BookableDates = await reservations.GetBookableDatesAsync();
        MaxGuests = await reservations.GetMaxPartySizeAsync();
    }

    // Step 1: the times offered for one date.
    public async Task<IActionResult> OnGetSlotsAsync(string? date)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day))
            return new JsonResult(new { error = "Choose a valid date." }) { StatusCode = 400 };

        var slots = await reservations.GetTimeSlotsAsync(day);
        return new JsonResult(new { slots });
    }

    // Step 1: "set your own time" - is there a table at this exact time, and if not, what is nearby.
    public async Task<IActionResult> OnGetCheckTimeAsync(string? date, string? time, int guests)
    {
        if (!TryStart(date, time, out var start))
            return new JsonResult(new { valid = false, available = false, message = "Choose a valid date and time.", suggestions = Array.Empty<object>() });

        var r = await reservations.CheckTimeAsync(start, guests);

        return new JsonResult(new
        {
            valid = r.Valid,
            available = r.Available,
            message = r.Message,
            suggestions = r.Suggestions.Select(x => new { date = x.Date, time = x.Time, label = x.Label, kind = x.Kind })
        });
    }

    // Step 1: does this customer already hold a booking that overlaps the chosen time?
    public async Task<IActionResult> OnGetOverlapAsync(string? date, string? time)
    {
        if (!TryStart(date, time, out var start))
            return new JsonResult(new { overlap = false });

        // Blocked when the customer already has a booking then, or would go over the booking limit.
        string? message = await reservations.HasOverlapAsync(UserId, start)
            ? "You already have a reservation at this time. Please choose another time."
            : await reservations.GetLimitMessageAsync(UserId, start);

        return new JsonResult(new { overlap = message != null, message });
    }

    // Step 2: every table type with its availability for the chosen date, time and guests.
    public async Task<IActionResult> OnGetTypesAsync(string? date, string? time, int guests)
    {
        if (!TryStart(date, time, out var start))
            return new JsonResult(new { error = "Choose a valid date and time." }) { StatusCode = 400 };

        var result = await reservations.GetTypeOptionsAsync(start, guests);

        return new JsonResult(new
        {
            error = result.Error,
            options = result.Options.Select(o => new
            {
                id = o.Id,
                name = o.Name,
                japaneseName = o.JapaneseName,
                description = o.Description,
                photo = o.PhotoPath,
                minGuests = o.MinGuests,
                maxGuests = o.MaxGuests,
                minimumSpend = o.MinimumSpend,
                belowMinimum = o.BelowMinimum,
                status = o.Status.ToString().ToLowerInvariant(),
                reason = o.Reason,
                tablesLeft = o.TablesLeft,
                areaName = o.AreaName,
                areaIcon = o.AreaIcon,
                suggestions = (o.Suggestions ?? new List<TimeSuggestion>())
                    .Select(x => new { date = x.Date, time = x.Time, label = x.Label, kind = x.Kind })
            })
        });
    }

    // Step 3: save the reservation.
    public async Task<IActionResult> OnPostSubmitAsync([FromBody] SubmitDto? dto)
    {
        if (dto is null || !TryStart(dto.Date, dto.Time, out var start))
            return new JsonResult(new { success = false, code = "slot", error = "Choose a valid date and time." })
            { StatusCode = 400 };

        var result = await reservations.CreateReservationAsync(new CreateReservationRequest(
            UserId,
            start,
            dto.Guests,
            dto.TableTypeId,
            dto.SpecialRequest,
            dto.AcceptedMinimumSpend,
            dto.ExpectedMinimumSpend,
            dto.Token));

        if (!result.Success || result.Ticket is null)
            return new JsonResult(new { success = false, code = result.Code, error = result.Error });

        var t = result.Ticket;

        // A failed notification must never undo a saved reservation.
        try
        {
            await notifications.SendAsync(
                UserId,
                "Reservation received",
                $"Your reservation {t.ReferenceNo} for {t.Guests} on " +
                $"{t.ReservedAt.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} at " +
                $"{t.ReservedAt.ToString("h:mm tt", CultureInfo.InvariantCulture)} is pending confirmation.",
                NotificationType.Reservation);
        }
        catch
        {
            // Intentionally ignored: the customer already sees the confirmation on screen.
        }

        return new JsonResult(new
        {
            success = true,
            ticket = new
            {
                reference = t.ReferenceNo,
                date = t.ReservedAt.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture),
                time = t.ReservedAt.ToString("h:mm tt", CultureInfo.InvariantCulture),
                guests = t.Guests,
                typeName = t.TypeName,
                typeJapaneseName = t.TypeJapaneseName,
                area = t.AreaName,
                areaIcon = t.AreaIcon,
                special = t.SpecialRequest,
                minimumSpend = t.MinimumSpend,
                status = t.Status,
                reservationId = t.ReservationId
            }
        });
    }

    private static bool TryStart(string? date, string? time, out DateTime start) =>
        DateTime.TryParseExact(
            $"{date} {time}",
            "yyyy-MM-dd HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out start);
}