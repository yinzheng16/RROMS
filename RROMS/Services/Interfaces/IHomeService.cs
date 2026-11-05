namespace RROMS.Services.Interfaces
{
    // Small read-only shapes the Home page shows (no database entities leave the service).
    public record HomeDish(string Name, string? Description, decimal Price, decimal? DiscountPercent, string? ImageUrl, string CategoryName, bool IsPopular, int? Id = null);
    // FinalPrice: the price under this promotion (US29: a % off or a fixed promo price), already rounded.
    public record HomeOffer(string Title, string DishName, decimal Price, decimal DiscountPercent, string? ImageUrl, string CategoryName, DateTime EndDate, int? MenuItemId = null, decimal? FinalPrice = null);
    public record HomeHoursRow(string Days, string Hours, bool IsClosed);
    public record OmikujiDish(string Name, string Category, decimal Price, int? Id = null);
    // Everything the quick booking box needs (sent to the browser as JSON).
    // The rules are Member 2's (US12): the box offers exactly what her booking page accepts.
    // The times for one date are loaded on demand from /Index?handler=Slots&date=yyyy-MM-dd.
    public record BookingSetup(
        List<string> Dates,          // bookable dates "yyyy-MM-dd" (IReservationService.GetBookableDatesAsync)
        List<string> ClosedDates,    // closed special dates in range (shown as "Shut")
        List<int> ClosedWeekdays,    // weekly closed days, 0 = Sunday (shown as "Shut")
        int MaxGuests,               // IReservationService.GetMaxPartySizeAsync
        int DaysAhead,               // BookingRules.MaxAdvanceDays
        int StepMinutes,             // "set your own time" steps (BookingTimeRules.ExactStepMinutes)
        string Now);                 // restaurant time now, "2026-10-23T19:43" (server clock, not the visitor's)

    // Mood of the hero, worked out from today's opening hours:
    // "before" (not open yet), "open", "last" (last hour) or "closed".
    public record HomeInfo(
        string Address, string Phone,
        string Mood, string MoodText,
        int DishCount, int CategoryCount, int OpenDays, string LastOrders);

    // Owner: Member 1 (US50 Home page).
    // Reads other modules' tables (read-only) for the public Home page:
    // menu items + promotions (Member 3), opening hours (Member 2), restaurant settings (Member 5).
    public interface IHomeService
    {
        // Up to `count` dishes: the most ordered first, then the menu order. Only available items.
        Task<List<HomeDish>> GetSignatureDishesAsync(int count = 3);

        // Up to `count` active promotions (started, not ended), the one ending soonest first.
        // Later (US39) Member 4's announcements can be added here too.
        Task<List<HomeOffer>> GetActiveOffersAsync(int count = 3);

        // How many dishes are on promotion now (same number as the Menu's "Specials"), for "See all 5 specials".
        Task<int> CountOfferDishesAsync();

        // Opening hours with days that have the same hours grouped: "Tuesday – Sunday  5:00 PM – 12:00 AM",
        // breaks shown as "10:00 AM – 3:00 PM · 5:00 PM – 10:00 PM", then the next few special dates.
        Task<List<HomeHoursRow>> GetOpeningHoursAsync();

        // Address, phone, hero mood and the small numbers strip.
        Task<HomeInfo> GetInfoAsync();

        // Every available dish, for the omikuji box to pick from at random.
        Task<List<OmikujiDish>> GetOmikujiDishesAsync();

        // Opening hours, guest limit and rules for the quick booking box.
        Task<BookingSetup> GetBookingSetupAsync();
    }
}
