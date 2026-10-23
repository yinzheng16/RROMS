namespace RROMS.Services.Interfaces
{
    // Small read-only shapes the Home page shows (no database entities leave the service).
    public record HomeDish(string Name, string? Description, decimal Price, decimal? DiscountPercent, string? ImageUrl, string CategoryName, bool IsPopular);
    public record HomeOffer(string Title, string DishName, decimal Price, decimal DiscountPercent, string? ImageUrl, string CategoryName, DateTime EndDate);
    public record HomeHoursRow(string Days, string Hours, bool IsClosed);
    public record OmikujiDish(string Name, string Category, decimal Price);
    public record BookingTime(string Value, string Label);   // Value "19:00", Label "7:00 PM"

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

        // Opening hours with days that have the same hours grouped: "Tuesday – Sunday  5:00 PM – 12:00 AM".
        Task<List<HomeHoursRow>> GetOpeningHoursAsync();

        // Address, phone, hero mood and the small numbers strip.
        Task<HomeInfo> GetInfoAsync();

        // Every available dish, for the omikuji box to pick from at random.
        Task<List<OmikujiDish>> GetOmikujiDishesAsync();

        // Half-hour times for the quick booking box, from opening time to one hour before closing.
        Task<List<BookingTime>> GetBookingTimesAsync();
    }
}
