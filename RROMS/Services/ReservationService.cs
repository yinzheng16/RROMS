// Owner: Member 2 (Tables & Reservations)
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

public class ReservationService : IReservationService
{
    private static readonly string[] AllowedPhotoExt = { ".jpg", ".jpeg", ".png", ".webp" };
    private const long MaxPhotoBytes = 3 * 1024 * 1024;

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    public ReservationService(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    // ================= US11: dining areas =================

    public Task<List<DiningArea>> GetAreasWithTablesAsync() =>
        _db.DiningAreas.Include(a => a.Tables).OrderBy(a => a.Name).ToListAsync();

    public async Task<ReservationResult> SaveAreaAsync(DiningArea input)
    {
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0) return ReservationResult.Fail("Dining area name is required.");
        if (name.Length > 50) return ReservationResult.Fail("Dining area name can be at most 50 characters.");

        if (await _db.DiningAreas.AnyAsync(a => a.Name == name && a.Id != input.Id))
            return ReservationResult.Fail($"A dining area called '{name}' already exists.");

        if (input.Id == 0)
        {
            _db.DiningAreas.Add(new DiningArea { Name = name, Description = input.Description });
        }
        else
        {
            var area = await _db.DiningAreas.FindAsync(input.Id);
            if (area is null) return ReservationResult.Fail("Dining area not found.");
            area.Name = name;
            area.Description = input.Description;
        }
        await _db.SaveChangesAsync();
        return ReservationResult.Ok();
    }

    public async Task<ReservationResult> DeleteAreaAsync(int id)
    {
        var area = await _db.DiningAreas.Include(a => a.Tables).FirstOrDefaultAsync(a => a.Id == id);
        if (area is null) return ReservationResult.Fail("Dining area not found.");
        if (area.Tables.Any()) return ReservationResult.Fail("This area still has tables. Move or delete its tables first.");

        _db.DiningAreas.Remove(area);
        await _db.SaveChangesAsync();
        return ReservationResult.Ok();
    }

    // ================= US11: tables =================

    public async Task<ReservationResult> SaveTableAsync(RestaurantTable input)
    {
        var number = input.TableNumber?.Trim() ?? "";
        if (number.Length == 0) return ReservationResult.Fail("Table name is required.");
        if (number.Length > 10) return ReservationResult.Fail("Table name can be at most 10 characters.");
        if (input.Capacity <= 0) return ReservationResult.Fail("Capacity must be greater than 0.");
        if (input.Capacity > 20) return ReservationResult.Fail("Capacity cannot be more than 20.");
        if (input.MinGuests < 1 || input.MinGuests > input.Capacity)
            return ReservationResult.Fail("Minimum guests must be at least 1 and not more than the capacity.");
        if (input.MinimumSpend < 0) return ReservationResult.Fail("Minimum spend cannot be negative.");
        if (!await _db.DiningAreas.AnyAsync(a => a.Id == input.DiningAreaId))
            return ReservationResult.Fail("Please choose a dining area.");
        if (await _db.RestaurantTables.AnyAsync(t => t.TableNumber == number && t.Id != input.Id))
            return ReservationResult.Fail($"A table called '{number}' already exists.");

        if (input.Id == 0)
        {
            _db.RestaurantTables.Add(new RestaurantTable
            {
                TableNumber = number, Type = input.Type, MinGuests = input.MinGuests, Capacity = input.Capacity,
                MinimumSpend = input.MinimumSpend, Description = input.Description, DiningAreaId = input.DiningAreaId,
                IsActive = input.IsActive, Status = TableStatus.Available
            });
        }
        else
        {
            var table = await _db.RestaurantTables.FindAsync(input.Id);
            if (table is null) return ReservationResult.Fail("Table not found.");
            table.TableNumber = number;
            table.Type = input.Type;
            table.MinGuests = input.MinGuests;
            table.Capacity = input.Capacity;
            table.MinimumSpend = input.MinimumSpend;
            table.Description = input.Description;
            table.DiningAreaId = input.DiningAreaId;
            table.IsActive = input.IsActive;
        }
        await _db.SaveChangesAsync();
        return ReservationResult.Ok();
    }

    public async Task<ReservationResult> DeleteTableAsync(int id)
    {
        var table = await _db.RestaurantTables.FindAsync(id);
        if (table is null) return ReservationResult.Fail("Table not found.");

        // Keep booking history valid: a table that was ever booked cannot be deleted.
        if (await _db.Reservations.AnyAsync(r => r.TableId == id))
            return ReservationResult.Fail($"Table {table.TableNumber} has reservations, so it cannot be deleted. Edit it and untick 'Active' instead.");

        DeletePhotoFile(table.PhotoPath);
        _db.RestaurantTables.Remove(table);
        await _db.SaveChangesAsync();
        return ReservationResult.Ok();
    }

    public async Task<ReservationResult> SetTablePhotoAsync(int tableId, IFormFile photo)
    {
        var table = await _db.RestaurantTables.FindAsync(tableId);
        if (table is null) return ReservationResult.Fail("Table not found.");
        if (photo is null || photo.Length == 0) return ReservationResult.Fail("Choose a photo first.");

        var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
        if (!AllowedPhotoExt.Contains(ext) || !photo.ContentType.StartsWith("image/"))
            return ReservationResult.Fail("Only JPG, PNG or WEBP images are allowed.");
        if (photo.Length > MaxPhotoBytes) return ReservationResult.Fail("Photo is too large. Maximum size is 3 MB.");

        var folder = Path.Combine(_env.WebRootPath, "images", "tables");
        Directory.CreateDirectory(folder);
        var fileName = $"{Guid.NewGuid():N}{ext}";          // random name: never trust the uploaded file name
        await using (var fs = new FileStream(Path.Combine(folder, fileName), FileMode.Create))
            await photo.CopyToAsync(fs);

        DeletePhotoFile(table.PhotoPath);
        table.PhotoPath = $"/images/tables/{fileName}";
        await _db.SaveChangesAsync();
        return ReservationResult.Ok();
    }

    private void DeletePhotoFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("/images/tables/")) return;
        var file = Path.Combine(_env.WebRootPath, "images", "tables", Path.GetFileName(path));
        if (File.Exists(file)) File.Delete(file);
    }

    // ================= US11: operating hours =================

    public async Task<List<OperatingHour>> GetOperatingHoursAsync()
    {
        var hours = await _db.OperatingHours.ToListAsync();
        var missing = Enum.GetValues<DayOfWeek>().Where(d => hours.All(h => h.Day != d)).ToList();
        if (missing.Count > 0)
        {
            foreach (var d in missing)
                _db.OperatingHours.Add(new OperatingHour { Day = d, OpenTime = new TimeOnly(17, 0), CloseTime = new TimeOnly(23, 30), IsClosed = false });
            await _db.SaveChangesAsync();
            hours = await _db.OperatingHours.ToListAsync();
        }
        // Sorted in memory (days are stored as words, so SQL would sort them alphabetically). Monday first.
        return hours.OrderBy(h => ((int)h.Day + 6) % 7).ToList();
    }

    public async Task<ReservationResult> SaveOperatingHoursAsync(List<OperatingHour> hours)
    {
        var bad = hours.FirstOrDefault(h => !h.IsClosed && h.CloseTime <= h.OpenTime);
        if (bad is not null) return ReservationResult.Fail($"{bad.Day}: closing time must be after opening time.");

        foreach (var h in hours)
        {
            var row = await _db.OperatingHours.FindAsync(h.Id);
            if (row is null) continue;
            row.OpenTime = h.OpenTime;
            row.CloseTime = h.CloseTime;
            row.IsClosed = h.IsClosed;
        }
        await _db.SaveChangesAsync();
        return ReservationResult.Ok();
    }
}
