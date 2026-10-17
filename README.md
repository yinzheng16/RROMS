# RROMS

Web-Based Restaurant Reservation and Order Management System
5026CMD Software Engineering group project. ASP.NET Core Razor Pages (.NET 10), C#, EF Core, SQL Server.

## Getting started

1. Clone `https://github.com/yinzheng16/RROMS.git` in Visual Studio 2026 and open `RROMS/RROMS.slnx`.
2. Switch to the `develop` branch and Pull.
3. Package Manager Console: `Update-Database`
4. Press F5. Test accounts (password `Test@123`):

| Role | Email |
| --- | --- |
| Admin | admin@rroms.com |
| Staff | staff@rroms.com |
| Customer | customer@rroms.com |

## Folder structure (3-tier architecture)

| Tier | Folder | What goes there |
| --- | --- | --- |
| Presentation | `Pages/Admin`, `Pages/Staff`, `Pages/Customer`, `Pages/Shared` | Razor pages. Pages call services only, never the database directly. |
| Business logic | `Services/`, `Services/Interfaces/` | One service per module, e.g. `IReservationService` + `ReservationService` |
| Data access | `Repositories/`, `Data/ApplicationDbContext.cs`, `Models/` | Entity classes, DbContext, repositories, migrations |

Pages in `Pages/Admin`, `Pages/Staff` and `Pages/Customer` are protected by role automatically (see `Program.cs`).

## Module owners

| Member | Module | Main files |
| --- | --- | --- |
| Member 1 | Identity, Access & AI Chatbot | `ApplicationUser.cs`, `ChatFaq.cs`, `Roles.cs` |
| Member 2 | Tables & Reservations | `DiningArea.cs`, `RestaurantTable.cs`, `OperatingHour.cs`, `Reservation.cs`, `ReservationStatus.cs` |
| Member 3 | Menu, Orders & Recommendations | `MenuCategory.cs`, `MenuItem.cs`, `Promotion.cs`, `Order.cs`, `OrderItem.cs`, `OrderStatus.cs`, `OrderType.cs` |
| Member 4 | Payments, Billing & Notifications | `Payment.cs`, `PaymentMethod.cs`, `PaymentStatus.cs`, `Notification.cs`, `NotificationType.cs`, `NotificationService.cs` |
| Member 5 | Feedback, Reports & Dashboard | `Feedback.cs`, `RestaurantSetting.cs` |

Each file in `Models/` has its owner written at the top. Only edit your own files; ask the owner before changing someone else's.

## Team rules

- Never push directly to `main` or `develop`.
- One branch per user story from `develop`: `feature/US12-make-reservation`.
- Commit messages start with the story ID: `US12: add availability check`.
- Open a pull request into `develop`; Member 1 reviews and merges.
- Pull `develop` every day before you start.

## Database changes

- Do not rename or delete shared IDs and foreign keys, or tables owned by other members.
- To add a column or table: pull `develop` first, change the model (and add a `DbSet` for a new table), then run `Add-Migration <ShortName>` and push.
- After pulling someone else's migration, run `Update-Database`.
- Migration conflict? `Remove-Migration`, pull `develop`, then `Add-Migration` again.

## Enum rules

- Enums are saved in the database as their **name** (e.g. `Paid`), so tables are readable in SSMS.
- Never rename or delete an existing value: old records keep the old word.
- Adding a new value anywhere in the list is safe.
- Changing the label shown to users with `[Display(Name = "...")]` is always safe.
- A new enum: put it in its own file in `Models/`, and add one `HaveConversion<string>()` line for it in `ConfigureConventions` in `ApplicationDbContext.cs`.
