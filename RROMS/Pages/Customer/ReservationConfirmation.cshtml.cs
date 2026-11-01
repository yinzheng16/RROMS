// Owner: Member 4 — US36 Reservation confirmation PDF.
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer;

// The /Customer folder already requires CustomerOnly (Program.cs);
// the attribute repeats it so the page stays protected if moved.
[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ReservationConfirmationModel(IReservationConfirmationService confirmations) : PageModel
{
    private readonly IReservationConfirmationService _confirmations = confirmations;

    // /Customer/ReservationConfirmation?id=12
    // Not found, someone else's, cancelled or past bookings: all give 404,
    // so nobody can find out which reservation IDs exist.
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var confirmation = await _confirmations.GetCustomerConfirmationAsync(userId, id);
        if (confirmation is null) return NotFound();

        var pdf = _confirmations.CreatePdf(confirmation);
        return File(pdf, "application/pdf", $"YORU-reservation-{confirmation.ReferenceNo}.pdf");
    }
}
