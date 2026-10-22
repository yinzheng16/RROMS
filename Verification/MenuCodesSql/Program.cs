using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;
using RROMS.Services.Interfaces;

// Optional SQL Server checks. Always create a randomly named disposable database;
// the database in the supplied connection string is NEVER migrated or deleted.
string server = Environment.GetEnvironmentVariable("RROMS_MENU_TEST_CONNECTION")
    ?? "Server=(localdb)\\mssqllocaldb;Integrated Security=true;TrustServerCertificate=true";
var connection = new SqlConnectionStringBuilder(server)
{
    InitialCatalog = "RROMS_MenuCodeTest_" + Guid.NewGuid().ToString("N")
};
connection.Remove("AttachDBFilename");
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options;
var environment = new TestEnvironment { ContentRootPath = Path.Combine(Path.GetTempPath(), connection.InitialCatalog) };
Directory.CreateDirectory(environment.ContentRootPath);
ApplicationDbContext Context() => new(options);
OrderService Service(ApplicationDbContext db) => new(db, environment, NullLogger<OrderService>.Instance);
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

try
{
    await using (var db = Context())
    {
        // Exercise upgrade of existing records, rather than only an empty database.
        await db.GetService<IMigrator>().MigrateAsync("20261017103732_InitialCreate");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO MenuCategories (Name, DisplayOrder) VALUES (N'Sashimi · Raw Seafood', 1);");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO MenuItems (CategoryId, Name, Price, IsAvailable, CreatedAt)
            SELECT Id, N'Legacy salmon', 22.90, 1, GETDATE() FROM MenuCategories;
            INSERT INTO MenuItems (CategoryId, Name, Price, IsAvailable, CreatedAt)
            SELECT Id, N'Legacy tuna', 27.90, 1, GETDATE() FROM MenuCategories;
            """);
        await db.Database.MigrateAsync();
        Check(!db.Database.HasPendingModelChanges(), "Model, designer and snapshot must match");
        var category = await db.MenuCategories.SingleAsync();
        var items = await db.MenuItems.OrderBy(item => item.Id).ToListAsync();
        Check(category.Id == 1 && category.Code == "SS", "Category backfill must preserve ID");
        Check(items[0].Id == 1 && items[0].CategoryId == 1 && items[0].Code == "SS01", "Item backfill must preserve ID/FK");
        Check(items[1].Id == 2 && items[1].Code == "SS02", "Stable legacy numbering");
        Check((await db.MenuCodeSequences.SingleAsync()).LastItemNumber == 2, "Counter backfill");
    }

    async Task<MenuItemOperationStatus> Save(string name, int categoryId = 1, int? id = null)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            await using var db = Context();
            var status = await Service(db).SaveMenuItemAsync(id, new(categoryId, name, null, 10m, true), null, false);
            if (status != MenuItemOperationStatus.Busy) return status;
            await Task.Delay(30 * (attempt + 1));
        }
        return MenuItemOperationStatus.Busy;
    }

    await using (var db = Context())
        Check(await Service(db).DeleteMenuItemAsync(2) == MenuItemOperationStatus.Success, "Delete highest item");
    Check(await Save("Replacement salmon") == MenuItemOperationStatus.Success, "Create after deletion");
    await using (var db = Context())
    {
        Check((await db.MenuItems.SingleAsync(item => item.Name == "Replacement salmon")).Code == "SS03", "Deleted SS02 must not be reused");
        Check(await Service(db).CreateCategoryAsync("Sakana") == CategoryOperationStatus.Success, "Unknown category");
        Check(await Service(db).CreateCategoryAsync("Sarada · Salads") == CategoryOperationStatus.Success, "Sarada after unknown category");
        Check((await db.MenuCategories.SingleAsync(category => category.Name == "Sakana")).Code == "SK", "Unknown name must not steal SA");
        Check((await db.MenuCategories.SingleAsync(category => category.Name.StartsWith("Sarada"))).Code == "SA", "Sarada mapping");
    }
    int sarada;
    await using (var db = Context()) sarada = await db.MenuCategories.Where(c => c.Code == "SA").Select(c => c.Id).SingleAsync();
    Check(await Save("Potato salad", sarada) == MenuItemOperationStatus.Success, "Separate prefix counter");
    Check(await Save("Moved legacy salmon", sarada, 1) == MenuItemOperationStatus.Success, "Move item");
    await using (var db = Context())
    {
        var moved = await db.MenuItems.SingleAsync(item => item.Id == 1);
        Check(moved.Code == "SA02" && moved.CategoryId == sarada, "Move keeps ID and consumes destination number");
        Check(await Service(db).UpdateCategoryAsync(1, "Sashimi · Fresh Seafood") == CategoryOperationStatus.Success, "Rename category");
        Check((await db.MenuCategories.SingleAsync(category => category.Id == 1)).Code == "SS", "Rename must not change prefix");
    }

    var concurrent = await Task.WhenAll(Enumerable.Range(1, 6).Select(number => Save("Concurrent dish " + number)));
    Check(concurrent.All(status => status == MenuItemOperationStatus.Success), "Concurrent saves must succeed or be safely retried");
    await using (var db = Context())
    {
        var codes = await db.MenuItems.Select(item => item.Code).ToListAsync();
        Check(codes.Distinct().Count() == codes.Count, "No duplicate item codes under concurrent creation");
        int before = await db.MenuCodeSequences.Where(sequence => sequence.Prefix == "SS").Select(sequence => sequence.LastItemNumber).SingleAsync();
        Check(await Service(db).SaveMenuItemAsync(null, new(1, "Concurrentdish1", null, 10m, true), null, false)
            == MenuItemOperationStatus.DuplicateName, "Whitespace-insensitive duplicate rule preserved");
        Check(await db.MenuCodeSequences.Where(sequence => sequence.Prefix == "SS").Select(sequence => sequence.LastItemNumber).SingleAsync() == before,
            "Rejected saves must not consume a number");
        var highest = await db.MenuItems.Where(item => item.Code == "SS" + before.ToString("D2")).SingleAsync();
        Check(await Service(db).DeleteMenuItemAsync(highest.Id) == MenuItemOperationStatus.Success, "Delete concurrent highest item");
    }
    Check(await Save("After highest deletion") == MenuItemOperationStatus.Success, "Create after highest deletion");

    await using (var db = Context())
    {
        var service = Service(db);
        var page = await service.GetCategoryPageAsync(1, 5, showAll: true);
        var reversed = page.Items.Select(category => category.Id).Reverse().ToArray();
        Check(await service.ReorderCategoriesAsync(reversed, page.OrderStamp) == CategoryOperationStatus.Success, "Save category order");
        var menu = await service.GetCustomerMenuAsync();
        var expected = reversed.Where(id => menu.Categories.Any(category => category.Id == id)).ToArray();
        Check(menu.Categories.Select(category => category.Id).SequenceEqual(expected), "Customer categories must follow saved order");
        Check(menu.Sections.Select(section => section.Category.Id).SequenceEqual(expected), "Customer sections must follow saved order");
        var result = await service.GetCustomerMenuAsync("SA02");
        Check(result.Sections.Single().Items.Single().Id == 1, "Customer search by item code");
        Check(await service.SetMenuItemAvailabilityAsync(1, false) == MenuItemOperationStatus.Success, "Availability change");
        Check(!(await service.GetCustomerMenuAsync()).Sections.SelectMany(section => section.Items).Any(item => item.Id == 1), "Unavailable items must stay out of public menu");
    }
    Console.WriteLine("PASS: SQL upgrade/backfill, original IDs/FKs, delete/create counters, moving/renaming, concurrent unique allocation, duplicate guards and customer order/availability/search.");
}
finally
{
    await using var cleanup = Context();
    await cleanup.Database.EnsureDeletedAsync();
    Directory.Delete(environment.ContentRootPath, recursive: true);
}

sealed class TestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "RROMS.MenuCodes.Verification";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
