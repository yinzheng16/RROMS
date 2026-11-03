// Owner: Member 2 (Tables & Reservations)
using RROMS.Models;

namespace RROMS.Services.Interfaces;

public interface IReservationService
{
    // Get all table types, including inactive types.
    Task<List<TableTypeDefinition>> GetTableTypesAsync();

    // Add or update a table type.
    // Id = 0 means Add; otherwise Edit.
    // createFirstTable is used only when adding a new type.
    Task<ReservationResult> SaveTableTypeAsync(
        TableTypeDefinition input,
        bool createFirstTable = false);

    // Delete a type only if it has no tables.
    Task<ReservationResult> DeleteTableTypeAsync(int id);
    Task<DeleteAllTablesResult> DeleteAllTablesAsync(int tableTypeId);

    // Upload or change a table type photo.
    Task<ReservationResult> SetTableTypePhotoAsync(
        int tableTypeId,
        IFormFile photo);

    // ---- US11: dining areas, tables, operating hours ----
    Task<List<DiningArea>> GetAreasWithTablesAsync();
    /// <summary>Id = 0 adds a new area, otherwise updates it.</summary>
    Task<ReservationResult> SaveAreaAsync(DiningArea input);
    Task<ReservationResult> DeleteAreaAsync(int id);

    /// <summary>Id = 0 adds a new table, otherwise updates it (live status and photo are not changed here).</summary>
    Task<ReservationResult> SaveTableAsync(RestaurantTable input);
    Task<ReservationResult> DeleteTableAsync(int id);
    Task<ReservationResult> RemoveTableTypePhotoAsync(int tableTypeId);

    /// <summary>Always returns 7 rows (Monday to Sunday).</summary>
    Task<List<OperatingHour>> GetOperatingHoursAsync();
    Task<ReservationResult> SaveOperatingHoursAsync(List<OperatingHour> hours);
    // ---- Special Dates ----
    Task<List<SpecialDate>> GetSpecialDatesAsync();
    Task<ReservationResult> SaveSpecialDateAsync(SpecialDate input);
    Task<ReservationResult> DeleteSpecialDateAsync(int id);

    // US12, US13, US14... will be added here in the next branches.
    // ---- US12 / US14: customer booking ----
    Task<List<string>> GetBookableDatesAsync();
    Task<List<BookingSlot>> GetTimeSlotsAsync(DateTime date);
    Task<int> GetMaxPartySizeAsync();
    Task<TypeOptionsResult> GetTypeOptionsAsync(DateTime start, int guests);
    Task<BookingResult> CreateReservationAsync(CreateReservationRequest request);
    Task<int> ReleaseOverdueReservationsAsync();
    Task<TimeCheckResult> CheckTimeAsync(DateTime start, int guests);
    Task<bool> HasOverlapAsync(string userId, DateTime start);
    Task<string?> GetLimitMessageAsync(string userId, DateTime start);
    Task<bool> IsOpenAtAsync(DateTime when);
    Task<bool> CanOrderAtAsync(DateTime when);
}
