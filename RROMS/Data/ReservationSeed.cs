// Owner: Member 2 (Tables & Reservations)
using Microsoft.EntityFrameworkCore;
using RROMS.Models;

namespace RROMS.Data;

/// <summary>
/// Sample data for YORU IZAKAYA: 3 dining areas, 5 table types and 30 tables.
/// Called once at start-up from DbSeeder.cs.
/// Each part has its own check, so a database that already has some of the data
/// still gets the missing parts (no Drop-Database needed). Nothing is duplicated.
/// </summary>
public static class ReservationSeed
{
    public static void Seed(ApplicationDbContext db)
    {
        SeedAreasAndTables(db);
        SeedTableTypes(db);
        LinkTablesToTypes(db);
    }

    // ---------- 1) Dining areas + 30 tables (only on an empty database) ----------
    private static void SeedAreasAndTables(ApplicationDbContext db)
    {
        if (db.DiningAreas.Any()) return;

        var bar = new DiningArea { Name = "Bar Counter", Description = "Long counter beside the bar" };
        var hall = new DiningArea { Name = "Main Hall", Description = "Open dining hall" };
        var rooms = new DiningArea { Name = "Private Rooms", Description = "VIP and VVIP rooms" };
        db.DiningAreas.AddRange(bar, hall, rooms);

        for (var i = 1; i <= 6; i++)
            db.RestaurantTables.Add(new RestaurantTable
            {
                TableNumber = $"C{i}",
                Type = TableType.Counter,
                MinGuests = 1,
                Capacity = 1,
                DiningArea = bar,
                Description = "Single seat at the long counter beside the bar.",
                PhotoPath = "/images/tables/Counter.jpg"
            });

        for (var i = 1; i <= 10; i++)
            db.RestaurantTables.Add(new RestaurantTable
            {
                TableNumber = $"D{i}",
                Type = TableType.Double,
                MinGuests = 1,
                Capacity = 2,
                DiningArea = hall,
                Description = "Cosy table for two.",
                PhotoPath = "/images/tables/Double.jpg"
            });

        for (var i = 1; i <= 10; i++)
            db.RestaurantTables.Add(new RestaurantTable
            {
                TableNumber = $"T{i}",
                Type = TableType.Standard,
                MinGuests = 3,
                Capacity = 6,
                DiningArea = hall,
                Description = "Table for small groups and families.",
                PhotoPath = "/images/tables/Standard.jpg"
            });

        for (var i = 1; i <= 3; i++)
            db.RestaurantTables.Add(new RestaurantTable
            {
                TableNumber = $"VIP{i}",
                Type = TableType.Vip,
                MinGuests = 1,
                Capacity = 10,
                MinimumSpend = 500,
                DiningArea = rooms,
                Description = "Private room for 6-10 guests.",
                PhotoPath = "/images/tables/vip.jpg"
            });

        db.RestaurantTables.Add(new RestaurantTable
        {
            TableNumber = "VVIP1",
            Type = TableType.Vvip,
            MinGuests = 1,
            Capacity = 20,
            MinimumSpend = 1000,
            DiningArea = rooms,
            Description = "Our largest private room for 10-20 guests.",
            PhotoPath = "/images/tables/vvip.jpg"
        });

        db.SaveChanges();
    }

    // ---------- 2) The 5 table types (added even if tables already exist) ----------
    private static void SeedTableTypes(ApplicationDbContext db)
    {
        if (db.TableTypeDefinitions.Any()) return;

        int? AreaId(string name) => db.DiningAreas.Where(a => a.Name == name).Select(a => (int?)a.Id).FirstOrDefault();
        var bar = AreaId("Bar Counter");
        var hall = AreaId("Main Hall");
        var rooms = AreaId("Private Rooms");
        if (bar is null || hall is null || rooms is null) return;   // areas were renamed by hand: leave it to the admin page

        db.TableTypeDefinitions.AddRange(
            new TableTypeDefinition
            {
                Name = "Counter seat",
                Prefix = "C",
                DefaultDiningAreaId = bar.Value,
                DefaultMinGuests = 1,
                DefaultMaxGuests = 1,
                PhotoPath = "/images/tables/Counter.jpg",
                Description = "Single seat at the long counter beside the bar."
            },
            new TableTypeDefinition
            {
                Name = "Standard table",
                Prefix = "T",
                DefaultDiningAreaId = hall.Value,
                DefaultMinGuests = 3,
                DefaultMaxGuests = 6,
                PhotoPath = "/images/tables/Standard.jpg",
                Description = "Table for small groups and families."
            },
            new TableTypeDefinition
            {
                Name = "Table for two",
                Prefix = "D",
                DefaultDiningAreaId = hall.Value,
                DefaultMinGuests = 1,
                DefaultMaxGuests = 2,
                PhotoPath = "/images/tables/Double.jpg",
                Description = "Cosy table for two."
            },
            new TableTypeDefinition
            {
                Name = "VIP room",
                Prefix = "VIP",
                DefaultDiningAreaId = rooms.Value,
                DefaultMinGuests = 1,
                DefaultMaxGuests = 10,
                DefaultMinimumSpend = 500,
                PhotoPath = "/images/tables/vip.jpg",
                Description = "Private room for 6-10 guests."
            },
            new TableTypeDefinition
            {
                Name = "VVIP room",
                Prefix = "VVIP",
                DefaultDiningAreaId = rooms.Value,
                DefaultMinGuests = 10,
                DefaultMaxGuests = 20,
                DefaultMinimumSpend = 1000,
                PhotoPath = "/images/tables/vvip.jpg",
                Description = "Our largest private room for 10-20 guests."
            });

        db.SaveChanges();
    }

    // ---------- 3) Repair: put tables that have no table type into the matching type ----------
    // Older databases already had the 30 tables before table types existed.
    // Matching is by the type's Prefix (C, D, T, VIP, VVIP), so tables keep their numbers and bookings.
    private static void LinkTablesToTypes(ApplicationDbContext db)
    {
        var types = db.TableTypeDefinitions.Include(t => t.Tables).ToList();
        if (types.Count == 0) return;

        var linkedIds = types.SelectMany(t => t.Tables).Select(t => t.Id).ToHashSet();
        var prefixFor = new Dictionary<TableType, string>
        {
            [TableType.Counter] = "C",
            [TableType.Double] = "D",
            [TableType.Standard] = "T",
            [TableType.Vip] = "VIP",
            [TableType.Vvip] = "VVIP"
        };

        var changed = false;
        foreach (var table in db.RestaurantTables.ToList().Where(t => !linkedIds.Contains(t.Id)))
        {
            if (!prefixFor.TryGetValue(table.Type, out var prefix)) continue;
            var type = types.FirstOrDefault(t => t.Prefix == prefix);
            if (type is null) continue;

            type.Tables.Add(table);   // EF fills in the table's type link
            changed = true;
        }

        if (changed) db.SaveChanges();
    }
}
