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
                TableNumber = number,
                Type = input.Type,
                MinGuests = input.MinGuests,
                Capacity = input.Capacity,
                MinimumSpend = input.MinimumSpend,
                Description = input.Description,
                DiningAreaId = input.DiningAreaId,
                IsActive = input.IsActive,
                Status = TableStatus.Available,
                PhotoPath = TablePhotoPath(input.Type)
            });
        }
        else
        {
            var table = await _db.RestaurantTables.FindAsync(input.Id);
            if (table is null) return ReservationResult.Fail("Table not found.");
            table.TableNumber = number;
            if (table.Type != input.Type) table.PhotoPath = TablePhotoPath(input.Type);
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

        var oldPhoto = table.PhotoPath;
        _db.RestaurantTables.Remove(table);
        await _db.SaveChangesAsync();
        await DeleteUnusedPhotoAsync(oldPhoto);
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

        var oldPhoto = table.PhotoPath;
        table.PhotoPath = $"/images/tables/{fileName}";
        await _db.SaveChangesAsync();
        await DeleteUnusedPhotoAsync(oldPhoto);
        return ReservationResult.Ok();
    }

    // One shared image per table type; updates existing tables of that type.
    public async Task<ReservationResult> SetTypePhotoAsync(TableType type, IFormFile photo)
    {
        if (!Enum.IsDefined(typeof(TableType), type)) return ReservationResult.Fail("Invalid table type.");
        if (photo is null || photo.Length == 0) return ReservationResult.Fail("Choose a photo first.");
        var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
        if (!AllowedPhotoExt.Contains(ext) || photo.ContentType is null || !photo.ContentType.StartsWith("image/"))
            return ReservationResult.Fail("Only JPG, PNG or WEBP images are allowed.");
        if (photo.Length > MaxPhotoBytes) return ReservationResult.Fail("Photo is too large. Maximum size is 3 MB.");

        var folder = Path.Combine(_env.WebRootPath, "images", "tables");
        Directory.CreateDirectory(folder);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        await using (var fs = new FileStream(Path.Combine(folder, fileName), FileMode.CreateNew))
            await photo.CopyToAsync(fs);

        var tables = await _db.RestaurantTables.Where(t => t.Type == type).ToListAsync();
        if (tables.Count == 0)
        {
            File.Delete(Path.Combine(folder, fileName));
            return ReservationResult.Fail("There are no tables of this type yet.");
        }
        var oldPaths = tables.Select(t => t.PhotoPath).Where(x => x != null).Distinct().ToList();
        foreach (var table in tables) table.PhotoPath = $"/images/tables/{fileName}";
        await _db.SaveChangesAsync();
        foreach (var oldPath in oldPaths) await DeleteUnusedPhotoAsync(oldPath);
        return ReservationResult.Ok();
    }

    public async Task<ReservationResult> SetTableStatusAsync(int id, TableStatus status)
    {
        if (!Enum.IsDefined(typeof(TableStatus), status)) return ReservationResult.Fail("Invalid status.");
        var table = await _db.RestaurantTables.FindAsync(id);
        if (table is null) return ReservationResult.Fail("Table not found.");
        table.Status = status;
        await _db.SaveChangesAsync();
        return ReservationResult.Ok();
    }

    private static string TablePhotoPath(TableType type) => type switch
    {
        TableType.Counter => "/images/tables/counter.jpg",
        TableType.Double => "/images/tables/double.jpg",
        TableType.Standard => "/images/tables/standard.jpg",
        TableType.Vip => "/images/tables/vip.jpg",
        TableType.Vvip => "/images/tables/vvip.jpg",
        _ => ""
    };

    // Never delete the five Git-shared sample images or a photo referenced by another table.
    private async Task DeleteUnusedPhotoAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/images/tables/", StringComparison.Ordinal)) return;
        var fileName = Path.GetFileName(path);
        if (new[] { "counter.jpg", "double.jpg", "standard.jpg", "vip.jpg", "vvip.jpg" }
            .Contains(fileName, StringComparer.OrdinalIgnoreCase)) return;
        if (await _db.RestaurantTables.AnyAsync(t => t.PhotoPath == path)) return;
        var file = Path.Combine(_env.WebRootPath, "images", "tables", fileName);
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
                _db.OperatingHours.Add(new OperatingHour { Day = d, OpenTime = new TimeOnly(10, 0), CloseTime = new TimeOnly(22, 0), IsClosed = false });
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
