using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    public class ReportService : IReportService
    {
        private readonly IRepository<RestaurantSetting> _restaurantSettingRepository;

        public ReportService(IRepository<RestaurantSetting> restaurantSettingRepository)
        {
            _restaurantSettingRepository = restaurantSettingRepository;
        }

        public async Task<RestaurantSetting> GetRestaurantSettingsAsync()
        {
            var settings = await _restaurantSettingRepository.GetAllAsync();

            if (settings.Count > 0)
            {
                return settings[0];
            }

            var newSettings = new RestaurantSetting
            {
                RestaurantName = "YORU IZAKAYA",
                TaxRatePercent = 0,
                ServiceChargePercent = 0
            };

            await _restaurantSettingRepository.AddAsync(newSettings);
            await _restaurantSettingRepository.SaveChangesAsync();

            return newSettings;
        }

        public async Task UpdateRestaurantSettingsAsync(RestaurantSetting settings)
        {
            _restaurantSettingRepository.Update(settings);
            await _restaurantSettingRepository.SaveChangesAsync();
        }
    }
}