using Microsoft.AspNetCore.Mvc;
using RROMS.Services.Interfaces;

namespace RROMS.ViewComponents
{
    // US39 (Owner: Member 4): the one-line announcement banner at the top of the home page.
    // The home page (Member 1's US50) only needs one line:
    //     @await Component.InvokeAsync("AnnouncementBanner")
    // Shows the newest live announcements (up to AnnouncementBanner.MaxSlides) taking turns;
    // nothing at all when there is none or when reading fails, so the home page never breaks because of it. View: Pages/Shared/Components/AnnouncementBanner/Default.cshtml
    public class AnnouncementBannerViewComponent : ViewComponent
    {
        private readonly IAnnouncementService _announcements;
        private readonly ILogger<AnnouncementBannerViewComponent> _logger;

        public AnnouncementBannerViewComponent(IAnnouncementService announcements,
            ILogger<AnnouncementBannerViewComponent> logger)
        {
            _announcements = announcements;
            _logger = logger;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            try
            {
                var items = await _announcements.GetRunningAsync();
                return items.Count == 0 ? Content(string.Empty) : View(items);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The home page announcement banner could not be loaded.");
                return Content(string.Empty);
            }
        }
    }
}
