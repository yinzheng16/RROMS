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
                tablesLeft = o.TablesLeft
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
                special = t.SpecialRequest,
                minimumSpend = t.MinimumSpend,
                status = t.Status
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
