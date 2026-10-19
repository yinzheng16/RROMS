// Owner: Member 2 (Tables & Reservations)
using RROMS.Models;

namespace RROMS.Data;

/// <summary>
/// Sample data for YORU IZAKAYA's 30 tables. Call ReservationSeed.Seed(context) once inside YOUR section of DbSeeder.cs.
/// It does nothing if a dining area already exists, so delete any old sample areas/tables first
/// (or skip this and add the tables yourself in the admin page).
/// </summary>
public static class ReservationSeed
{
    public static void Seed(ApplicationDbContext db)
    {
        if (db.DiningAreas.Any()) return;

        var bar = new DiningArea { Name = "Bar Counter", Description = "Long counter beside the bar" };
        var hall = new DiningArea { Name = "Main Hall", Description = "Open dining hall" };
        var rooms = new DiningArea { Name = "Private Rooms", Description = "VIP and VVIP rooms" };
        db.DiningAreas.AddRange(bar, hall, rooms);

        for (var i = 1; i <= 6; i++)
            db.RestaurantTables.Add(new RestaurantTable { TableNumber = $"C{i}", Type = TableType.Counter, MinGuests = 1, Capacity = 1,
                DiningArea = bar, Description = "Single seat at the long counter beside the bar.",
                PhotoPath = "/images/tables/counter.jpg" 
            });

        for (var i = 1; i <= 10; i++)
            db.RestaurantTables.Add(new RestaurantTable { TableNumber = $"D{i}", Type = TableType.Double, MinGuests = 1, Capacity = 2,
                DiningArea = hall, Description = "Cosy table for two.",
                PhotoPath = "/images/tables/double.jpg"
            });

        for (var i = 1; i <= 10; i++)
            db.RestaurantTables.Add(new RestaurantTable { TableNumber = $"T{i}", Type = TableType.Standard, MinGuests = 3, Capacity = 6,
                DiningArea = hall, Description = "Table for small groups and families.",
                PhotoPath = "/images/tables/standard.jpg"
            });

        for (var i = 1; i <= 3; i++)
            db.RestaurantTables.Add(new RestaurantTable { TableNumber = $"VIP{i}", Type = TableType.Vip, MinGuests = 1, Capacity = 10,
                MinimumSpend = 500, DiningArea = rooms, Description = "Private room for 6-10 guests.",
                PhotoPath = "/images/tables/vip.jpg"
            });

        db.RestaurantTables.Add(new RestaurantTable { TableNumber = "VVIP1", Type = TableType.Vvip, MinGuests = 1, Capacity = 20,
            MinimumSpend = 1000, DiningArea = rooms, Description = "Our largest private room for 10-20 guests.",
            PhotoPath = "/images/tables/vvip.jpg"
        });

        db.SaveChanges();
    }
}
