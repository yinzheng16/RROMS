using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // US44 Filtered reports (Member 5).
    // Separate from IReportService, which looks after the restaurant settings (US49).
    public interface ISalesReportService
    {
        // Builds one report (numbers, chart, filters and every matching row).
        Task<ReportResult> BuildAsync(ReportQuery query);
    }
}