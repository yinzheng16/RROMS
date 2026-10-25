using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin;

public class Index : PageModel
{
    private readonly IAdminOverviewService _adminOverviewService;

    public Index(IAdminOverviewService adminOverviewService)
    {
        _adminOverviewService = adminOverviewService;
    }

    public AdminOverviewData Overview { get; set; } = new();

    // Today / This week / This month switch, read from the URL, e.g. /Admin?period=week
    [BindProperty(SupportsGet = true)]
    public string? Period { get; set; }

    public async Task OnGetAsync()
    {
        var period = Period?.ToLowerInvariant() switch
        {
            "week" => OverviewPeriod.Week,
            "month" => OverviewPeriod.Month,
            _ => OverviewPeriod.Today
        };

        Overview = await _adminOverviewService.GetOverviewAsync(period);
    }
}