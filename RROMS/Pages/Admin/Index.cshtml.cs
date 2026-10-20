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

    public async Task OnGetAsync()
    {
        Overview = await _adminOverviewService.GetOverviewAsync();
    }
}