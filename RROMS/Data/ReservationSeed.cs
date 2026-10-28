// Owner: Member 2 (Tables & Reservations)
using Microsoft.EntityFrameworkCore;
using RROMS.Models;

/// <summary>
/// Sample data for YORU IZAKAYA: 3 dining areas, 5 table types and 30 tables.
/// Called once at start-up from DbSeeder.cs.
/// Each part has its own check, so a database that already has some of the data
/// still gets the missing parts (no Drop-Database needed). Nothing is duplicated.
/// </summary>
namespace RROMS.Data;

public static class ReservationSeed
{
    private record SeedType(
        string Name,
        string Code,
        string LegacyPrefix,
        string Area,
        TableType LegacyType,
        int Count,
        int MinGuests,
        int MaxGuests,
        decimal MinimumSpend,
        string Photo,
        string Description
    );

    private static readonly SeedType[] Defaults =
    {
        new(
            "Counter seat", "CS", "C",
            "Bar Counter", TableType.Counter,
            6, 1, 1, 0,
            "/images/tables/counter.jpg",
            "Single seat at the long counter beside the bar."
        ),
        new(
            "Standard table", "ST", "T",
            "Main Hall", TableType.Standard,
            10, 3, 6, 0,
            "/images/tables/standard.jpg",
            "Table for small groups and families."
        ),
        new(
            "Table for two", "TFT", "D",
            "Main Hall", TableType.Double,
            10, 1, 2, 0,
            "/images/tables/double.jpg",
            "Cosy table for two."
        ),
        new(
            "VIP room", "VR", "VIP",
            "Private Rooms", TableType.Vip,
            3, 1, 10, 500,
            "/images/tables/vip.jpg",
            "Private room for 6-10 guests."
        ),
        new(
            "VVIP room", "VVIPR", "VVIP",
            "Private Rooms", TableType.Vvip,
            1, 1, 20, 1000,
            "/images/tables/vvip.jpg",
            "Our largest private room for 10-20 guests."
        )
    };

    public static void Seed(ApplicationDbContext db)
    {
        using var transaction = db.Database.BeginTransaction();

        SeedAreas(db);
        SeedTypesAndTables(db);

        transaction.Commit();
    }

    private static void SeedAreas(ApplicationDbContext db)
    {
        var areas = new[]
        {
            new DiningArea
            {
                Name = "Bar Counter",
                Description = "Long counter beside the bar",
                IsActive = true
            },
            new DiningArea
            {
                Name = "Main Hall",
                Description = "Open dining hall",
                IsActive = true
            },
            new DiningArea
            {
                Name = "Private Rooms",
                Description = "VIP and VVIP rooms",
                IsActive = true
            }
        };

        foreach (var area in areas)
        {
            if (!db.DiningAreas.Any(a => a.Name == area.Name))
                db.DiningAreas.Add(area);
        }

        db.SaveChanges();
    }

    private static void SeedTypesAndTables(ApplicationDbContext db)
    {
        foreach (var seed in Defaults)
        {
            var area = db.DiningAreas
                .Single(a => a.Name == seed.Area);

            var type = db.TableTypeDefinitions
                .SingleOrDefault(t => t.Name == seed.Name);

            if (type == null)
            {
                type = new TableTypeDefinition
                {
                    Name = seed.Name,
                    Code = seed.Code,
                    Prefix = seed.LegacyPrefix,
                    StartingNumber = 1,
                    NumberPadding = 3,
                    DefaultDiningAreaId = area.Id,
                    DefaultMinGuests = seed.MinGuests,
                    DefaultMaxGuests = seed.MaxGuests,
                    DefaultMinimumSpend = seed.MinimumSpend,
                    PhotoPath = seed.Photo,
                    Description = seed.Description,
                    IsActive = true
                };

                db.TableTypeDefinitions.Add(type);
            }
            else if (string.IsNullOrWhiteSpace(type.Code))
            {
                // One-time upgrade of legacy seeded types.
                type.Code = seed.Code;
                type.NumberPadding = 3;
            }

            db.SaveChanges();

            for (int number = 1; number <= seed.Count; number++)
            {
                string oldNumber = $"{seed.LegacyPrefix}{number}";
                string newNumber = $"{seed.Code}{number:D3}";

                var table = db.RestaurantTables
                    .SingleOrDefault(t =>
                        t.TableNumber == newNumber);

                if (table == null)
                {
                    table = db.RestaurantTables
                        .SingleOrDefault(t =>
                            t.TableNumber == oldNumber &&
                            t.Type == seed.LegacyType);
                }

                if (table == null)
                {
                    table = new RestaurantTable
                    {
                        TableNumber = newNumber,
                        Type = seed.LegacyType,
                        TableTypeDefinitionId = type.Id,
                        DiningAreaId = area.Id,
                        MinGuests = seed.MinGuests,
                        Capacity = seed.MaxGuests,
                        MinimumSpend = seed.MinimumSpend,
                        Description = seed.Description,
                        PhotoPath = seed.Photo,
                        IsActive = true,
                        Status = TableStatus.Available
                    };

                    db.RestaurantTables.Add(table);
                }
                else if (table.TableNumber == oldNumber)
                {
                    // Upgrade legacy seeded table without
                    // deleting it or changing its primary key.
                    table.TableNumber = newNumber;
                    table.TableTypeDefinitionId = type.Id;
                    table.DiningAreaId = area.Id;
                    table.MinGuests = seed.MinGuests;
                    table.Capacity = seed.MaxGuests;
                    table.MinimumSpend = seed.MinimumSpend;
                    table.Description = seed.Description;
                    table.PhotoPath = seed.Photo;
                    table.IsActive = true;
                }
                else if (table.TableTypeDefinitionId == null)
                {
                    table.TableTypeDefinitionId = type.Id;
                }
            }

            db.SaveChanges();
        }
    }
}

