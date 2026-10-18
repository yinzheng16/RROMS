// Owner: Member 2 (Tables & Reservations)
using RROMS.Models;

namespace RROMS.Services.Interfaces;

public interface IReservationService
{
    // ---- US11: dining areas, tables, operating hours ----
    Task<List<DiningArea>> GetAreasWithTablesAsync();
    /// <summary>Id = 0 adds a new area, otherwise updates it.</summary>
    Task<ReservationResult> SaveAreaAsync(DiningArea input);
    Task<ReservationResult> DeleteAreaAsync(int id);

    /// <summary>Id = 0 adds a new table, otherwise updates it (live status and photo are not changed here).</summary>
    Task<ReservationResult> SaveTableAsync(RestaurantTable input);
    Task<ReservationResult> DeleteTableAsync(int id);
    Task<ReservationResult> SetTablePhotoAsync(int tableId, IFormFile photo);
    Task<ReservationResult> SetTypePhotoAsync(TableType type, IFormFile photo);
    Task<ReservationResult> SetTableStatusAsync(int id, TableStatus status);

    /// <summary>Always returns 7 rows (Monday to Sunday).</summary>
    Task<List<OperatingHour>> GetOperatingHoursAsync();
    Task<ReservationResult> SaveOperatingHoursAsync(List<OperatingHour> hours);

    // US12, US13, US14... will be added here in the next branches.
}
