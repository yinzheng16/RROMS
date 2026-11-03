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

        // ---------- US49: bill maths ----------

        public async Task<BillCharges> CalculateChargesAsync(decimal subtotal, bool isTakeaway)
        {
            var settings = await GetRestaurantSettingsAsync();
            return CalculateCharges(settings, subtotal, isTakeaway);
        }

        public BillCharges CalculateCharges(RestaurantSetting settings, decimal subtotal, bool isTakeaway)
        {
            var servicePercent = isTakeaway && !settings.ServiceChargeOnTakeaway
                ? 0m
                : settings.ServiceChargePercent;

            var service = Money(subtotal * servicePercent / 100m);
            var tax = Money((subtotal + service) * settings.TaxRatePercent / 100m);

            return new BillCharges(
                Subtotal: Money(subtotal),
                ServiceChargePercent: servicePercent,
                ServiceCharge: service,
                TaxRatePercent: settings.TaxRatePercent,
                Tax: tax,
                Total: Money(subtotal) + service + tax);
        }

        // RM to 2 decimal places, 0.005 rounds up
        private static decimal Money(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
