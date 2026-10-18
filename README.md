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

## Starting page after login

| Role | Goes to | File | Built by |
| --- | --- | --- | --- |
| Visitor / Customer | Home `/` | `Pages/Index.cshtml` | Member 1 (US50) |
| Staff | Today's reservations `/Staff` | `Pages/Staff/Index.cshtml` | Member 2 (US16) |
| Admin | Today's overview `/Admin` | `Pages/Admin/Index.cshtml` | Member 5 (US41) |

Member 1 builds the redirect after login and the role-based navbar (US51). Each member only builds the page.

## Page addresses

The navbar already links to these addresses. Create your page at exactly this path, and the link works; until then it shows "not found". You don't need to edit `_Layout.cshtml`.

| Address | File | Owner | Story |
| --- | --- | --- | --- |
| `/Menu` (public) | `Pages/Menu/Index.cshtml` | Member 3 | US23 |
| `/Customer/Reservations/Create` | `Pages/Customer/Reservations/Create.cshtml` | Member 2 | US12 |
| `/Customer/Reservations` | `Pages/Customer/Reservations/Index.cshtml` | Member 2 | US18 |
| `/Customer/Cart` | `Pages/Customer/Cart.cshtml` | Member 3 | US24 |
| `/Customer/Orders` | `Pages/Customer/Orders/Index.cshtml` | Member 3 | US28 |
| `/Customer/Payments` | `Pages/Customer/Payments/Index.cshtml` | Member 4 | US34 |
| `/Customer/Notifications` | `Pages/Customer/Notifications.cshtml` | Member 4 | US37 |
| `/Staff` | `Pages/Staff/Index.cshtml` | Member 2 | US16 |
| `/Staff/Orders` | `Pages/Staff/Orders/Index.cshtml` | Member 3 | US25 |
| `/Staff/Billing` | `Pages/Staff/Billing/Index.cshtml` | Member 4 | US32 |
| `/Admin` | `Pages/Admin/Index.cshtml` | Member 5 | US41 |
| `/Admin/Users` | `Pages/Admin/Users/Index.cshtml` | Member 1 | US05, US07 |
| `/Admin/ChatFaqs` | `Pages/Admin/ChatFaqs/Index.cshtml` | Member 1 | US10 |
| `/Admin/Tables` | `Pages/Admin/Tables/Index.cshtml` | Member 2 | US11 |
| `/Admin/Occupancy` | `Pages/Admin/Occupancy.cshtml` | Member 2 | US20 |
| `/Admin/Menu` | `Pages/Admin/Menu/Index.cshtml` | Member 3 | US21, US22 |
| `/Admin/Promotions` | `Pages/Admin/Promotions/Index.cshtml` | Member 3 | US29 |
| `/Admin/Payments` | `Pages/Admin/Payments.cshtml` | Member 4 | US40 |
| `/Admin/Announcements` | `Pages/Admin/Announcements/Index.cshtml` | Member 4 | US39 |
| `/Admin/Dashboard` | `Pages/Admin/Dashboard.cshtml` | Member 5 | US46 |
| `/Admin/Reports` | `Pages/Admin/Reports/Index.cshtml` | Member 5 | US44 |
| `/Admin/Feedback` | `Pages/Admin/Feedback/Index.cshtml` | Member 5 | US43 |
| `/Admin/Settings` | `Pages/Admin/Settings.cshtml` | Member 5 | US49 |
| My profile | Identity `Account/Manage` (built in) | Member 1 | US03, US04 |

Need a page that isn't listed? Tell Member 1, who adds the link to the navbar.

## Design (YORU IZAKAYA theme)

Shared layout files (Member 1, don't edit): `Pages/Shared/_Layout.cshtml`, `Pages/Shared/_LoginPartial.cshtml`, `wwwroot/css/site.css`.

| Colour | Hex | Use |
| --- | --- | --- |
| Charcoal Black | `#191919` | Navbar, footer, headings |
| Warm Cream | `#F5F0E7` | Page background |
| Vermilion Red | `#C83E2D` | Main buttons, brand mark |
| Muted Gold | `#B79B72` | Accents on dark backgrounds only |
| Olive Grey | `#73766B` | Icons, borders |

In your pages, use normal Bootstrap classes; `site.css` already restyles them. Don't add your own colours or `<style>` blocks.

- Page title:
  ```html
  <div class="page-header">
      <div><p class="eyebrow">Reservations</p><h1>Today's reservations</h1></div>
      <a class="btn btn-primary" href="...">New booking</a>
  </div>
  ```
- Main action: `btn btn-primary` (red). Other actions: `btn btn-outline-secondary`.
- Lists: `<div class="table-responsive"><table class="table table-hover align-middle">...</table></div>`. Boxes: `card`.
- Layout: use the Bootstrap grid (`row`, `col-md-6`, `col-lg-4`) and `rem` sizes. Never set fixed pixel widths like `width: 1200px`; the layout already keeps the same width on every screen.
- Status (same look in every module): `<span class="status-badge status-@item.Status">@item.Status</span>`
- Full-width page (e.g. a hero): set `ViewData["FullWidth"] = true;` and use `<section class="section-dark">`.

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

## Troubleshooting

- `Add-Migration` or `Update-Database` stops with warning **NU1902**: run `$ErrorActionPreference = "Continue"` in the Package Manager Console, then run the command again.
- Page shows "not found": check your file path matches the table in "Page addresses".
- "Access denied": you're logged in with the wrong role. Use the test account for that role.
