using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // Owner: Member 4 (US36 - Reservation confirmation PDF)
    // Reads Member 2's reservations (read only, never changes them) and turns
    // one of the customer's own upcoming bookings into a PDF.
    public interface IReservationConfirmationService
    {
        // One of the customer's own upcoming Pending / Confirmed reservations.
        // customerId comes from the login, never from the URL.
        // Returns null when the reservation does not exist, belongs to someone
        // else, is Cancelled / Seated / Completed / No-show, or is past the
        // no-show grace time (the page shows 404 for all of them).
        // Same list of bookings as My Reservations (US13).
        Task<ReservationConfirmation?> GetCustomerConfirmationAsync(string customerId, int reservationId);

        // The confirmation as a PDF file (QuestPDF, same look as the US35 receipt).
        byte[] CreatePdf(ReservationConfirmation confirmation);
    }

    // Everything printed on the confirmation.
    public sealed record ReservationConfirmation
    {
        public int ReservationId { get; init; }
        public string ReferenceNo { get; init; } = string.Empty;

        // Pending -> BOOKING REQUEST, Confirmed -> RESERVATION CONFIRMATION.
        public ReservationStatus Status { get; init; }
        public bool IsConfirmed => Status == ReservationStatus.Confirmed;

        public string GuestName { get; init; } = string.Empty;
        public DateTime ReservedAt { get; init; }
        public int Guests { get; init; }

        // "Tatami / 座敷"; null when no table is assigned yet.
        public string? TableType { get; init; }
        public string? AreaName { get; init; }

        // Shown only once staff have confirmed the booking (Member 2's rule).
        public string? TableNumber { get; init; }

        // 0 = no minimum spend.
        public decimal MinimumSpend { get; init; }
        public string? SpecialRequest { get; init; }

        // How long the table is held after the arrival time.
        public int GraceMinutes { get; init; }

        public DateTime IssuedAt { get; init; }

        // Restaurant details (US49 Restaurant Settings).
        public string? Address { get; init; }
        public string? Phone { get; init; }
    }
}
