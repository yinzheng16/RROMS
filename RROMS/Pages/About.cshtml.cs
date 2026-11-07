using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages
{
    // US53 (Owner: Member 1): About us page. Public (no login needed).
    // The address, phone, opening hours and numbers come from IHomeService,
    // so they follow what the admin saves in Restaurant Settings / Opening Hours.
    public class AboutModel : PageModel
    {
        private readonly IHomeService _home;

        public AboutModel(IHomeService home)
        {
            _home = home;
        }

        public HomeInfo Info { get; private set; } = null!;
        public List<HomeHoursRow> Hours { get; private set; } = new();

        public async Task OnGetAsync()
        {
            Info = await _home.GetInfoAsync();
            Hours = await _home.GetOpeningHoursAsync();
        }
    }
}
