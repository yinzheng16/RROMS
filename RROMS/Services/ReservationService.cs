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

    // ================= US11: dynamic table types =================

    public Task<List<TableTypeDefinition>> GetTableTypesAsync() =>
        _db.TableTypeDefinitions
            .Include(t => t.DefaultDiningArea)
            .Include(t => t.Tables)
            .OrderBy(t => t.Name)
            .ToListAsync();

    public Task<TableTypeDefinition?> GetTableTypeByIdAsync(int id) =>
        _db.TableTypeDefinitions
            .Include(t => t.DefaultDiningArea)
            .Include(t => t.Tables)
            .FirstOrDefaultAsync(t => t.Id == id);

    public async Task<string> GetNextTableNameAsync(int tableTypeId)
    {
        var type = await _db.TableTypeDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tableTypeId);

        if (type is null)
            throw new ArgumentException("Table type not found.", nameof(tableTypeId));

        var prefix = type.Prefix.Trim();

        if (string.IsNullOrWhiteSpace(prefix) ||
            !System.Text.RegularExpressions.Regex.IsMatch(prefix, @"^[A-Za-z]+$"))
            throw new InvalidOperationException("Invalid table type prefix.");

        if (type.StartingNumber < 1 || type.NumberPadding < 1 ||
            type.NumberPadding > 10)
            throw new InvalidOperationException("Invalid table numbering settings.");

        var existingNames = await _db.RestaurantTables
            .AsNoTracking()
            .Select(t => t.TableNumber)
            .ToListAsync();

        var usedNumbers = new HashSet<int>();

        foreach (var name in existingNames)
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var suffix = name.Substring(prefix.Length);

            if (int.TryParse(suffix, out var number) &&
                number >= 0 &&
                suffix.All(char.IsDigit))
            {
                usedNumbers.Add(number);
            }
        }

        var nextNumber = type.StartingNumber;

        while (usedNumbers.Contains(nextNumber))
        {
            if (nextNumber == int.MaxValue)
                throw new InvalidOperationException("No available table number.");

            nextNumber++;
        }

        var suggestedName =
            prefix + nextNumber.ToString().PadLeft(type.NumberPadding, '0');

        if (suggestedName.Length > 10)
            throw new InvalidOperationException(
                "Generated table name exceeds 10 characters.");

        return suggestedName;
    }

    public async Task<ReservationResult> DeleteTableTypeAsync(int id)
    {
        var type = await _db.TableTypeDefinitions
            .Include(t => t.Tables)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (type is null)
            return ReservationResult.Fail("Table type not found.");

        if (type.Tables.Any())
            return ReservationResult.Fail(
                "This table type still has tables. Delete or move its tables first.");

        _db.TableTypeDefinitions.Remove(type);

        try
        {
            await _db.SaveChangesAsync();
            return ReservationResult.Ok();
        }
        catch (DbUpdateException)
        {
            return ReservationResult.Fail(
                "This table type is still referenced and cannot be deleted.");
        }
    }

    public async Task<ReservationResult> SetTableTypePhotoAsync(
    int tableTypeId,
    IFormFile photo)
    {
        var type = await _db.TableTypeDefinitions
            .FirstOrDefaultAsync(t => t.Id == tableTypeId);

        if (type is null)
            return ReservationResult.Fail("Table type not found.");

        if (photo is null || photo.Length == 0)
            return ReservationResult.Fail("Choose a photo first.");

        var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();

        if (!AllowedPhotoExt.Contains(ext) ||
            photo.ContentType is null ||
            !photo.ContentType.StartsWith("image/"))
            return ReservationResult.Fail(
                "Only JPG, PNG or WEBP images are allowed.");

        if (photo.Length > MaxPhotoBytes)
            return ReservationResult.Fail(
                "Photo is too large. Maximum size is 3 MB.");

        var folder = Path.Combine(_env.WebRootPath, "images", "tables");
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(folder, fileName);
        var newPhotoPath = $"/images/tables/{fileName}";

        await using (var fs = new FileStream(fullPath, FileMode.CreateNew))
        {
            await photo.CopyToAsync(fs);
        }

        var oldPhoto = type.PhotoPath;

        type.PhotoPath = newPhotoPath;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (File.Exists(fullPath))
                File.Delete(fullPath);

            return ReservationResult.Fail("Could not save the table type photo.");
        }

        // Keep the old photo if another table or table type still uses it.
        if (!string.IsNullOrWhiteSpace(oldPhoto))
        {
            var stillUsedByType = await _db.TableTypeDefinitions
                .AnyAsync(t => t.PhotoPath == oldPhoto);

            if (!stillUsedByType)
                await DeleteUnusedPhotoAsync(oldPhoto);
        }

        return ReservationResult.Ok();
    }

    // ================= US11: dining areas =================

    public async Task<ReservationResult> SaveTableTypeAsync(
        TableTypeDefinition input,
        bool createFirstTable = false,
        string? firstTableName = null,
        bool applyToExistingTables = false)
    {
        // 1. Validate input
        var name = input.Name?.Trim() ?? "";
        var prefix = input.Prefix?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(name) || name.Length > 50)
            return ReservationResult.Fail("Type name must be 1–50 characters.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(prefix, @"^[A-Za-z]+$") ||
            prefix.Length > 10)
            return ReservationResult.Fail("Prefix must contain 1–10 letters only.");

        if (input.StartingNumber < 1)
            return ReservationResult.Fail("Starting number must be at least 1.");

        if (input.NumberPadding < 1 || input.NumberPadding > 10)
            return ReservationResult.Fail("Number padding must be between 1 and 10.");

        if (input.DefaultMinGuests < 1 ||
            input.DefaultMaxGuests > 20 ||
            input.DefaultMinGuests > input.DefaultMaxGuests)
            return ReservationResult.Fail("Invalid guest capacity range.");

        if (input.DefaultMinimumSpend < 0)
            return ReservationResult.Fail("Minimum spend cannot be negative.");

        if (!await _db.DiningAreas.AnyAsync(a => a.Id == input.DefaultDiningAreaId))
            return ReservationResult.Fail("Please select a valid dining area.");

        if (await _db.TableTypeDefinitions.AnyAsync(
            t => t.Name == name && t.Id != input.Id))
            return ReservationResult.Fail("A table type with this name already exists.");

        if (input.Id != 0 && createFirstTable)
            return ReservationResult.Fail("First table creation is only available for new types.");

        // 2. Validate first table name before saving anything
        string? tableName = null;

        if (input.Id == 0 && createFirstTable)
        {
            tableName = firstTableName?.Trim();

            if (string.IsNullOrWhiteSpace(tableName))
            {
                var number = input.StartingNumber.ToString()
                    .PadLeft(input.NumberPadding, '0');

                tableName = prefix + number;
            }

            if (tableName.Length > 10 ||
                !System.Text.RegularExpressions.Regex.IsMatch(
                    tableName, @"^[A-Za-z]+[0-9]+$"))
                return ReservationResult.Fail(
                    "Table name must contain letters followed by numbers, maximum 10 characters.");

            if (await _db.RestaurantTables.AnyAsync(t => t.TableNumber == tableName))
                return ReservationResult.Fail("This table name already exists.");
        }

        // 3. Load existing type and check reservation conflicts
        TableTypeDefinition? existing = null;

        if (input.Id != 0)
        {
            existing = await _db.TableTypeDefinitions
                .Include(t => t.Tables)
                .FirstOrDefaultAsync(t => t.Id == input.Id);

            if (existing is null)
                return ReservationResult.Fail("Table type not found.");

            if (applyToExistingTables)
            {
                var now = DateTime.Now;
                var tableIds = existing.Tables.Select(t => t.Id).ToList();

                var conflictingReservation = await _db.Reservations
                    .Where(r =>
                        r.TableId.HasValue &&
                        tableIds.Contains(r.TableId.Value) &&
                        (r.Status == ReservationStatus.Pending ||
                         r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Seated) &&
                        r.ReservedAt.AddMinutes(r.DurationMinutes) > now &&
                        (r.Guests < input.DefaultMinGuests ||
                         r.Guests > input.DefaultMaxGuests))
                    .Select(r => new
                    {
                        r.ReferenceNo,
                        r.Guests
                    })
                    .FirstOrDefaultAsync();

                if (conflictingReservation is not null)
                    return ReservationResult.Fail(
                        $"Cannot update existing tables. Reservation " +
                        $"{conflictingReservation.ReferenceNo} has " +
                        $"{conflictingReservation.Guests} guests, outside the new capacity range.");
            }
        }

        // Prepare and validate table names before saving any changes.
        var updatedTableNames = new Dictionary<int, string>();

        if (existing is not null &&
            applyToExistingTables &&
            (existing.Prefix != prefix ||
             existing.NumberPadding != input.NumberPadding))
        {
            var allTables = await _db.RestaurantTables
                .AsNoTracking()
                .Select(t => new
                {
                    t.Id,
                    t.TableNumber
                })
                .ToListAsync();

            var plannedNames = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var table in existing.Tables.OrderBy(t => t.Id))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    table.TableNumber,
                    @"^([A-Za-z]+)([0-9]+)$");

                if (!match.Success)
                {
                    return ReservationResult.Fail(
                        $"Cannot rename {table.TableNumber}. " +
                        "Its name must contain letters followed by numbers.");
                }

                // Keep the numeric value and apply the new padding.
                var digits = match.Groups[2].Value.TrimStart('0');

                if (digits.Length == 0)
                {
                    digits = "0";
                }

                var newName =
                    prefix + digits.PadLeft(input.NumberPadding, '0');

                if (newName.Length > 10)
                {
                    return ReservationResult.Fail(
                        $"Generated table name {newName} exceeds 10 characters.");
                }

                if (!plannedNames.Add(newName))
                {
                    return ReservationResult.Fail(
                        $"More than one table would become {newName}. " +
                        "Please give those tables different numbers first.");
                }

                // Check other tables, including their current names.
                var nameExists = allTables.Any(t =>
                    t.Id != table.Id &&
                    string.Equals(
                        t.TableNumber,
                        newName,
                        StringComparison.OrdinalIgnoreCase));

                if (nameExists)
                {
                    return ReservationResult.Fail(
                        $"Cannot rename {table.TableNumber} to {newName}. " +
                        "That table name is already in use.");
                }

                updatedTableNames[table.Id] = newName;
            }
        }
        // 4. Save everything in one transaction
        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            TableTypeDefinition type;

            if (existing is null)
            {
                type = new TableTypeDefinition();
                _db.TableTypeDefinitions.Add(type);
            }
            else
            {
                type = existing;
            }

            type.Name = name;
            type.Prefix = prefix;
            type.StartingNumber = input.StartingNumber;
            type.NumberPadding = input.NumberPadding;
            type.DefaultDiningAreaId = input.DefaultDiningAreaId;
            type.DefaultMinGuests = input.DefaultMinGuests;
            type.DefaultMaxGuests = input.DefaultMaxGuests;
            type.DefaultMinimumSpend = input.DefaultMinimumSpend;
            type.Description = input.Description;
            type.IsActive = input.IsActive;

            await _db.SaveChangesAsync();

            // 5. Optional first table
            if (existing is null && createFirstTable)
            {
                _db.RestaurantTables.Add(new RestaurantTable
                {
                    TableNumber = tableName!,
                    TableTypeDefinitionId = type.Id,
                    DiningAreaId = type.DefaultDiningAreaId,
                    MinGuests = type.DefaultMinGuests,
                    Capacity = type.DefaultMaxGuests,
                    MinimumSpend = type.DefaultMinimumSpend ?? 0m,
                    Description = type.Description,
                    PhotoPath = type.PhotoPath,
                    IsActive = type.IsActive,
                    Status = TableStatus.Available
                });
            }

            // 6. Optional bulk update
            if (existing is not null && applyToExistingTables)
            {
                foreach (var table in existing.Tables)
                {
                    if (updatedTableNames.TryGetValue(
                        table.Id,
                        out var newTableName))
                    {
                        table.TableNumber = newTableName;
                    }

                    table.DiningAreaId = type.DefaultDiningAreaId;
                    table.MinGuests = type.DefaultMinGuests;
                    table.Capacity = type.DefaultMaxGuests;
                    table.MinimumSpend = type.DefaultMinimumSpend ?? 0m;
                    table.Description = type.Description;
                    table.IsActive = type.IsActive;
                }
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            
            input.Id = type.Id;

            return ReservationResult.Ok();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return ReservationResult.Fail(
                "Could not save the table type. Check for duplicate names or database conflicts.");
        }
    }

    public Task<List<DiningArea>> GetAreasWithTablesAsync() =>
        _db.DiningAreas
            .Include(a => a.Tables)
                .ThenInclude(t => t.TableTypeDefinition)
            .OrderBy(a => a.Name)
            .ToListAsync();

    public async Task<ReservationResult> SaveAreaAsync(DiningArea input)
    {

        if (!Enum.IsDefined(input.StatusMode))
        {
            return ReservationResult.Fail("Invalid dining area status mode.");
        }

        if (input.StatusMode == DiningAreaStatusMode.Manual)
        {
            if (!input.ManualStatus.HasValue ||
                !Enum.IsDefined(input.ManualStatus.Value))
            {
                return ReservationResult.Fail(
                    "Manual status must be Available or Occupied.");
            }
        }
        else
        {
            input.ManualStatus = null;
        }

        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0) return ReservationResult.Fail("Dining area name is required.");
        if (name.Length > 50) return ReservationResult.Fail("Dining area name can be at most 50 characters.");

        if (await _db.DiningAreas.AnyAsync(a => a.Name == name && a.Id != input.Id))
            return ReservationResult.Fail($"A dining area called '{name}' already exists.");

        if (input.Id == 0)
        {
            _db.DiningAreas.Add(new DiningArea { Name = name, Description = input.Description, IconName = input.IconName, StatusMode = input.StatusMode, ManualStatus = input.ManualStatus });
        }
        else
        {
            var area = await _db.DiningAreas.FindAsync(input.Id);
            if (area is null) return ReservationResult.Fail("Dining area not found.");
            area.Name = name;
            area.Description = input.Description;
            area.IconName = input.IconName;
            area.StatusMode = input.StatusMode;
            area.ManualStatus = input.ManualStatus;
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
        TableTypeDefinition? dynamicType = null;

        if (input.TableTypeDefinitionId.HasValue)
        {
            dynamicType = await _db.TableTypeDefinitions
                .FirstOrDefaultAsync(t =>
                    t.Id == input.TableTypeDefinitionId.Value);

            if (dynamicType is null)
                return ReservationResult.Fail("Invalid table type.");

            if (input.Id == 0 && !dynamicType.IsActive)
                return ReservationResult.Fail(
                    "Cannot create a table under an inactive table type.");
        }
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
        if (!Enum.IsDefined(input.Status))
            return ReservationResult.Fail("Invalid table status.");
        if (input.Id == 0)
        {
            _db.RestaurantTables.Add(new RestaurantTable
            {
                TableNumber = number,
                TableTypeDefinitionId = input.TableTypeDefinitionId,
                Type = input.Type,
                MinGuests = input.MinGuests,
                Capacity = input.Capacity,
                MinimumSpend = input.MinimumSpend,
                Description = input.Description,
                DiningAreaId = input.DiningAreaId,
                IsActive = input.IsActive,
                Status = TableStatus.Available,
                PhotoPath = dynamicType?.PhotoPath ?? TablePhotoPath(input.Type)
            });
        }
        else
        {
            var table = await _db.RestaurantTables.FindAsync(input.Id);
            if (table is null) return ReservationResult.Fail("Table not found.");
            table.TableNumber = number;
            if(input.TableTypeDefinitionId.HasValue)
            {
                if (table.TableTypeDefinitionId != input.TableTypeDefinitionId ||
                    table.Type != input.Type)
                {
                    table.PhotoPath = dynamicType?.PhotoPath
                        ?? TablePhotoPath(input.Type);
                }
            }
            else if (table.Type != input.Type)
            {
                table.PhotoPath = TablePhotoPath(input.Type);
            }
            if (input.TableTypeDefinitionId.HasValue)
            {
                table.TableTypeDefinitionId = input.TableTypeDefinitionId;
            }
            table.Type = input.Type;
            table.MinGuests = input.MinGuests;
            table.Capacity = input.Capacity;
            table.MinimumSpend = input.MinimumSpend;
            table.Description = input.Description;
            table.DiningAreaId = input.DiningAreaId;
            table.IsActive = input.IsActive;
            table.Status = input.Status;
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
        if (await _db.TableTypeDefinitions.AnyAsync(t => t.PhotoPath == path)) return;
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
        // Same opening and closing time is not allowed.
        var bad = hours.FirstOrDefault(h => !h.IsClosed && h.CloseTime == h.OpenTime);

        if (bad is not null)
            return ReservationResult.Fail($"{bad.Day}: opening and closing times cannot be the same.");

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
    // ================= US11: special dates =================

    // Get all special dates, earliest first.
    public Task<List<SpecialDate>> GetSpecialDatesAsync() =>
        _db.SpecialDates
            .AsNoTracking()
            .OrderBy(s => s.Date)
            .ToListAsync();

    // Add or edit a special date.
    public async Task<ReservationResult> SaveSpecialDateAsync(
    SpecialDate input)
    {
        var name = input.Name?.Trim() ?? "";

        if (input.Date == default)
            return ReservationResult.Fail("Please select a start date.");

        var startDate = input.Date.Date;
        var endDate = (input.EndDate ?? input.Date).Date;

        if (endDate < startDate)
        {
            return ReservationResult.Fail(
                "End date must be on or after start date.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return ReservationResult.Fail(
                "Special date name must be 1–100 characters.");
        }

        if (!input.IsClosed)
        {
            if (!input.OpenTime.HasValue ||
                !input.CloseTime.HasValue)
            {
                return ReservationResult.Fail(
                    "Please select opening and closing times.");
            }

            if (input.OpenTime.Value < TimeSpan.Zero ||
                input.OpenTime.Value >= TimeSpan.FromDays(1) ||
                input.CloseTime.Value < TimeSpan.Zero ||
                input.CloseTime.Value >= TimeSpan.FromDays(1))
            {
                return ReservationResult.Fail(
                    "Please enter valid opening and closing times.");
            }

            if (input.OpenTime == input.CloseTime)
            {
                return ReservationResult.Fail(
                    "Opening and closing times cannot be the same.");
            }
        }

        // Exclude this record when editing.
        // A missing EndDate means the existing record covers one day.
        var conflict = await _db.SpecialDates
            .AsNoTracking()
            .Where(s =>
                s.Id != input.Id &&
                s.Date <= endDate &&
                (s.EndDate ?? s.Date) >= startDate)
            .OrderBy(s => s.Date)
            .Select(s => new
            {
                s.Name,
                s.Date,
                s.EndDate
            })
            .FirstOrDefaultAsync();

        if (conflict is not null)
        {
            var conflictEnd = (conflict.EndDate ?? conflict.Date).Date;

            var dateText = conflict.Date.Date == conflictEnd
                ? conflict.Date.ToString("dd MMM yyyy")
                : $"{conflict.Date:dd MMM yyyy} – " +
                  $"{conflictEnd:dd MMM yyyy}";

            return ReservationResult.Fail(
                $"Date range overlaps with {conflict.Name} " +
                $"({dateText}). Please choose different dates.");
        }

        SpecialDate row;

        if (input.Id == 0)
        {
            row = new SpecialDate();
            _db.SpecialDates.Add(row);
        }
        else
        {
            var existing = await _db.SpecialDates.FindAsync(input.Id);

            if (existing is null)
                return ReservationResult.Fail("Special date not found.");

            row = existing;
        }

        row.Date = startDate;
        row.EndDate = endDate;
        row.Name = name;
        row.IsClosed = input.IsClosed;
        row.OpenTime = input.IsClosed ? null : input.OpenTime;
        row.CloseTime = input.IsClosed ? null : input.CloseTime;

        try
        {
            await _db.SaveChangesAsync();
            return ReservationResult.Ok();
        }
        catch (DbUpdateException)
        {
            return ReservationResult.Fail(
                "Could not save the special date. " +
                "Please check for database conflicts.");
        }
    }

    // Delete a special date.
    public async Task<ReservationResult> DeleteSpecialDateAsync(int id)
    {
        var row = await _db.SpecialDates.FindAsync(id);

        if (row is null)
            return ReservationResult.Fail("Special date not found.");

        _db.SpecialDates.Remove(row);
        await _db.SaveChangesAsync();

        return ReservationResult.Ok();
    }
}
