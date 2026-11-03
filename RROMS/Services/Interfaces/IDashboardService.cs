using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // Owner: Member 5 (US46 Interactive dashboard).
    // Numbers and chart data for Pages/Admin/Dashboard. Read-only.
    public interface IDashboardService
    {
        Task<DashboardResult> BuildAsync(DashboardQuery query);
    }
}