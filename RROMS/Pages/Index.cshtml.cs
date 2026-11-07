using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages
{
    // US50 (Owner: Member 1): public Home page. Everything it shows comes from IHomeService,
    // so when the admin changes the menu, promotions, opening hours or settings, this page follows.
    public class IndexModel : PageModel
    {
        private readonly IHomeService _home;
        private readonly IAccountService _accounts;
        private readonly IReservationService _reservations;

        public IndexModel(IHomeService home, IAccountService accounts, IReservationService reservations)
        {
            _home = home;
            _accounts = accounts;
            _reservations = reservations;
        }

        public List<HomeDish> Dishes { get; private set; } = new();
        public List<HomeOffer> Offers { get; private set; } = new();

        // All dishes on promotion now; the home page shows 3 and the button says "See all 5 specials".
        public int OfferTotal { get; private set; }
        public List<HomeHoursRow> Hours { get; private set; } = new();
        public List<OmikujiDish> OmikujiDishes { get; private set; } = new();
        public BookingSetup Booking { get; private set; } = null!;
        public HomeInfo Info { get; private set; } = null!;

        // "Welcome back, Tan Mei Ling" for a logged-in customer; null for visitors.
        public string? CustomerName { get; private set; }

        // Today's date for the booking box ("2026-10-22").
        public string Today { get; private set; } = "";

        public async Task OnGetAsync()
        {
            Dishes = await _home.GetSignatureDishesAsync(3);
            Offers = await _home.GetActiveOffersAsync(3);   // empty -> the section is hidden
            OfferTotal = Offers.Count > 0 ? await _home.CountOfferDishesAsync() : 0;
            Hours = await _home.GetOpeningHoursAsync();
            Info = await _home.GetInfoAsync();
            OmikujiDishes = await _home.GetOmikujiDishesAsync();
            Booking = await _home.GetBookingSetupAsync();
            Today = DateTime.Today.ToString("yyyy-MM-dd");

            if (User.IsInRole(Roles.Customer))
            {
                CustomerName = await _accounts.GetDisplayNameAsync(User);
            }

            // While Member 3's menu is still empty, show sample dishes so the page never looks broken.
            if (Dishes.Count == 0)
            {
                Dishes = new List<HomeDish>
                {
                    new("Salmon aburi sushi", "Torched salmon nigiri with a touch of lime.", 22m, null, null, "Sushi", true),
                    new("Chicken yakitori", "Charcoal-grilled thigh skewers with tare glaze.", 18m, null, null, "Grill", false),
                    new("Tonkotsu ramen", "Rich pork broth, chashu, egg and spring onion.", 26m, null, null, "Noodles", false),
                };
            }
            if (OmikujiDishes.Count == 0)
            {
                OmikujiDishes = Dishes.Select(d => new OmikujiDish(d.Name, d.CategoryName, d.Price)).ToList();
                OmikujiDishes.Add(new OmikujiDish("Karaage", "Fried", 16m));
            }
        }

        // Quick booking box: the times for one date, from Member 2's booking rules (same list as her page).
        // Read-only and anonymous on purpose: visitors may look before logging in; booking itself needs a login.
        public async Task<IActionResult> OnGetSlotsAsync(string? date)
        {
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                return new JsonResult(Array.Empty<object>());

            var slots = await _reservations.GetTimeSlotsAsync(day);
            return new JsonResult(slots.Select(s => new { value = s.Time, label = s.Label }));
        }
    }
}
