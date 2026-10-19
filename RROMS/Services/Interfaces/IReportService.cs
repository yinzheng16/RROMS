using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    public interface IReportService
    {
        Task<RestaurantSetting> GetRestaurantSettingsAsync();
        Task UpdateRestaurantSettingsAsync(RestaurantSetting settings);
    }
}