using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 4 (US36 - Reservation confirmation PDF)
    // Read-only query of Member 2's Reservation data; Member 2's
    // ReservationService is not changed.
    public class ReservationConfirmationService : IReservationConfirmationService
    {
        private readonly IRepository<Reservation> _reservations;

        // US49 (Member 5): restaurant address and phone.
        private readonly IReportService _reportService;

        private readonly IWebHostEnvironment _env;

        public ReservationConfirmationService(
            IRepository<Reservation> reservations,
            IReportService reportService,
            IWebHostEnvironment env)
        {
            _reservations = reservations;
            _reportService = reportService;
            _env = env;
        }

        public async Task<ReservationConfirmation?> GetCustomerConfirmationAsync(string customerId, int reservationId)
        {
            if (string.IsNullOrWhiteSpace(customerId) || reservationId <= 0)
                return null;

            string id = customerId.Trim();

            // Same rule as My Reservations (Member 2, US13): upcoming Pending /
            // Confirmed bookings, still shown during the no-show grace time.
            var now = DateTime.Now;
            var cutoff = now.AddMinutes(-BookingRules.NoShowGraceMinutes);

            var r = await _reservations.Query()
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Table!).ThenInclude(t => t.TableTypeDefinition)
                .Include(x => x.Table!).ThenInclude(t => t.DiningArea)
                .Where(x => x.Id == reservationId &&
                            x.UserId == id &&
                            (x.Status == ReservationStatus.Pending ||
                             x.Status == ReservationStatus.Confirmed) &&
                            x.ReservedAt >= cutoff)
                .FirstOrDefaultAsync();

            if (r == null)
                return null;

            var settings = await _reportService.GetRestaurantSettingsAsync();
            var type = r.Table?.TableTypeDefinition;
            bool confirmed = r.Status == ReservationStatus.Confirmed;

            return new ReservationConfirmation
            {
                ReservationId = r.Id,
                ReferenceNo = r.ReferenceNo,
                Status = r.Status,
                GuestName = Blank(r.User?.FullName) ?? "Guest",
                ReservedAt = r.ReservedAt,
                Guests = r.Guests,
                TableType = type == null
                    ? null
                    : string.IsNullOrWhiteSpace(type.JapaneseName)
                        ? type.Name
                        : $"{type.Name} / {type.JapaneseName}",
                AreaName = Blank(r.Table?.DiningArea?.Name),
                TableNumber = confirmed ? Blank(r.Table?.TableNumber) : null,
                MinimumSpend = r.MinimumSpendAccepted ?? 0m,
                SpecialRequest = Blank(r.SpecialRequest),
                GraceMinutes = BookingRules.NoShowGraceMinutes,
                IssuedAt = now,
                Address = Blank(settings.Address),
                Phone = Blank(PhoneDisplay.Pretty(settings.Phone))
            };
        }

        public byte[] CreatePdf(ReservationConfirmation confirmation)
        {
            ArgumentNullException.ThrowIfNull(confirmation);

            // The same logo as the receipt (US35) and the staff printed bill.
            string logoPath = Path.Combine(_env.WebRootPath, "images", "yoru-icon.png");
            byte[]? logo = File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;

            // Shared with US35: sets the QuestPDF licence and loads the fonts once.
            ReceiptPdfDocument.EnsureFonts(_env.ContentRootPath);
            return ReservationConfirmationPdfDocument.Create(confirmation, logo);
        }

        private static string? Blank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
