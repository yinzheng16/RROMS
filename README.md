# RROMS – YORU IZAKAYA

Web-Based Restaurant Reservation and Order Management System, built for the 5026CMD Software Engineering group project (INTI International College Penang / Coventry University).

ASP.NET Core Razor Pages (.NET 10), C#, Entity Framework Core, SQL Server LocalDB, ASP.NET Core Identity, SignalR.

| Branch | Purpose |
| --- | --- |
| `main` | Released version only (tag `v1.0` = Sprint 4 release) |
| `develop` | Integration branch. All pull requests go here |

## Features

| Module | Owner | What it does |
| --- | --- | --- |
| Identity, Access & AI Chatbot | Member 1 | Customer registration with phone validation, login, lockout after failed attempts, forgot/reset password by email, profile and password change, role-based access (Admin / Staff / Customer), admin user management and activity log, AI chatbot (Gemini) with FAQ fallback, chatbot FAQ management, home page |
| Tables & Reservations | Member 2 | Dining areas and tables, operating hours, online booking with automatic availability check, confirmation page and email, customer booking history, cancel/modify, staff reservation list, seating and occupancy analytics |
| Menu, Orders & Recommendations | Member 3 | Menu and categories with images, cart and checkout (dine-in / takeaway), live order status (SignalR), staff order queue, order history and reorder, promotions and specials |
| Payments, Billing & Notifications | Member 4 | Staff billing and bills, online payment with Stripe test mode, PDF receipts (QuestPDF), refunds, live notification bell (SignalR), announcements by email, admin payment records |
| Feedback, Reports & Dashboard | Member 5 | Customer feedback and admin replies, admin overview, dashboard charts (Chart.js), reports filtered by date and exported to Excel (ClosedXML), CSV and PDF, restaurant settings |

## Getting started

1. Clone `https://github.com/yinzheng16/RROMS.git` in Visual Studio 2026 and open `RROMS/RROMS.slnx`.
2. Switch to the `develop` branch and **Pull**.
3. Set up User Secrets (next section).
4. Package Manager Console: `Update-Database`
5. Press **F5**. NuGet packages download automatically on the first build.

Test accounts (password `Test@123`):

| Role | Email | Start page after login |
| --- | --- | --- |
| Admin | admin@rroms.com | `/Admin` (today's overview) |
| Staff | staff@rroms.com | `/Staff/Reservations` (today's reservations) |
| Customer | customer@rroms.com | `/` (home) |

## User Secrets (keys and passwords)

Keys and passwords are **never** stored in `appsettings.json`, in code, in GitHub, in screenshots or in the group chat. Each developer keeps them in User Secrets on their own computer.

In Visual Studio, right-click the **RROMS** project → **Manage User Secrets**, and paste:

```json
{
  "Email": { "Password": "<Gmail App password for yoru.rroms@gmail.com>" },
  "Ai": { "ApiKey": "<Gemini API key>", "Enabled": true },
  "Stripe": { "SecretKey": "sk_test_..." }
}
```

| Secret | Needed for | Without it |
| --- | --- | --- |
| `Email:Password` | Confirmation, reset-password and announcement emails (Gmail SMTP, port 587) | Emails are not sent; in Development you can still see them at `/Dev/Emails` |
| `Ai:ApiKey` | AI answers in the chatbot (`/chat`) | Set `"Enabled": false` and the chatbot answers from the FAQ keywords only |
| `Stripe:SecretKey` | Online payment (`/Customer/Payments/Pay`) | The Pay now button cannot open Stripe checkout |

- Use a Stripe **test** key (`sk_test_...`) only. Never use `sk_live_`.
- Ask Member 1 or the key owner for a key one-to-one, never in the group chat.

Stripe test card: `4242 4242 4242 4242`, any future expiry date, any CVC.

## Running the tests

The `tests/RROMS.Tests` project uses xUnit.

1. **Test → Run All Tests** (or `dotnet test` in the solution folder).
2. Most tests use an in-memory SQLite database and need no setup.
3. `PaymentSqlServerTests` use LocalDB and create a separate database called `RROMS_US31_Test`, so your normal `RROMS` data is never touched. If LocalDB isn't installed, skip these tests.

## Folder structure (3-tier architecture)

| Tier | Folder | What goes there |
| --- | --- | --- |
| Presentation | `Pages/Admin`, `Pages/Staff`, `Pages/Customer`, `Pages/Shared`, `Areas/Identity`, `ViewComponents`, `TagHelpers` | Razor pages and their page models. Pages call services only, never the database directly |
| Business logic | `Services/`, `Services/Interfaces/`, `Hubs/` | One service per module (e.g. `IReservationService` + `ReservationService`), rules classes, SignalR hubs |
| Data access | `Repositories/`, `Data/ApplicationDbContext.cs`, `Models/`, `Migrations/` | Entity classes, DbContext, repositories, seed data, migrations |

Pages in `Pages/Admin`, `Pages/Staff` and `Pages/Customer` are protected by role automatically (see `Program.cs`).

## Main packages

| Package | Used for |
| --- | --- |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | Accounts, roles, lockout |
| Microsoft.EntityFrameworkCore.SqlServer / Tools | Database and migrations |
| libphonenumber-csharp | Phone number validation |
| MailKit | Gmail SMTP emails |
| Stripe.net | Online payment (test mode) |
| QuestPDF | PDF receipts and PDF reports |
| ClosedXML | Excel report export |
| QRCoder | QR codes |
| SignalR (built in) | Live order status and notification bell |
| Chart.js (wwwroot/lib) | Dashboard charts |

## Module owners

| Member | Name (GitHub) | Module |
| --- | --- | --- |
| Member 1 | Chan Yin Zheng ([@yinzheng16](https://github.com/yinzheng16)), Scrum Master | Identity, Access & AI Chatbot |
| Member 2 | Teh Cheryl ([@cheryl415-o](https://github.com/cheryl415-o)) | Tables & Reservations |
| Member 3 | Tan Khai Wern ([@KhaiWern0525](https://github.com/KhaiWern0525)) | Menu, Orders & Recommendations |
| Member 4 | Kung Xuan Yu ([@ckxy0758](https://github.com/ckxy0758)) | Payments, Billing & Notifications |
| Member 5 | Chloe Yeoh Wen Ci ([@chloeywc](https://github.com/chloeywc)) | Feedback, Reports & Dashboard |

Each file in `Models/` has its owner written at the top. Only edit your own files; ask the owner before changing someone else's.

## Page addresses

| Address | Owner | Story |
| --- | --- | --- |
| `/` Home | Member 1 | US50 |
| `/Identity/Account/Register`, `/Identity/Account/Login`, `/Identity/Account/ForgotPassword` | Member 1 | US01, US02, US52 |
| `/Identity/Account/Manage` (profile, password) | Member 1 | US03, US04 |
| `/chat` AI chatbot | Member 1 | US08, US09 |
| `/Admin/Users`, `/Admin/Users/Activity` | Member 1 | US05, US07 |
| `/Admin/ChatFaqs` | Member 1 | US10 |
| `/Customer/Reservations/Create` | Member 2 | US12 |
| `/Customer/Reservations` | Member 2 | US13, US15, US18 |
| `/Customer/ReservationConfirmation?id=` (confirmation PDF) | Member 4 | US36 |
| `/Staff/Reservations` | Member 2 | US16 |
| `/Admin/Tables` | Member 2 | US11 |
| `/Admin/Occupancy` | Member 2 | US20 |
| `/Menu` (public) | Member 3 | US23 |
| `/Customer/Cart`, `/Customer/OrderConfirmation` | Member 3 | US24 |
| `/Customer/Orders`, `/Customer/Orders/Reorder` | Member 3 | US28 |
| `/Staff/Orders` | Member 3 | US25 |
| `/Admin/Menu` | Member 3 | US21, US22 |
| `/Admin/Promotions` | Member 3 | US29 |
| `/Staff/Billing`, `/Staff/Billing/Bill` | Member 4 | US32 |
| `/Customer/Payments`, `/Customer/Payments/Pay` | Member 4 | US33, US34, US35 |
| `/Customer/Notifications` | Member 4 | US37 |
| `/Admin/Payments` | Member 4 | US40 |
| `/Admin/Announcements` | Member 4 | US39 |
| `/Customer/Feedback`, `/Customer/Feedback/Create` | Member 5 | US42, US43 |
| `/Admin` (overview) | Member 5 | US41 |
| `/Admin/Dashboard` | Member 5 | US46 |
| `/Admin/Reports` | Member 5 | US44, US45, US47, US48 |
| `/Admin/Feedback` | Member 5 | US43 |
| `/Admin/Settings` | Member 5 | US49 |
| `/Dev/Emails` (Development only) | Member 1 | EN07 |

Story IDs follow the master product backlog. Check the backlog for the full list of stories and acceptance criteria.

## Using accounts in your pages

- Logged-in user's ID: `UserManager.GetUserId(User)`. Check a role: `User.IsInRole(Roles.Staff)`.
- Phone numbers are saved as `+60123456789`. To check one, call `IAccountService.FormatPhoneNumber("MY", number)`: it returns the formatted number, or `null` if it isn't valid.
- Only customers can register. Staff and Admin accounts are created by an Admin (US05).

## Design (YORU IZAKAYA theme)

Shared layout files (Member 1): `Pages/Shared/_Layout.cshtml`, `Pages/Shared/_LoginPartial.cshtml`, `wwwroot/css/site.css`.

| Colour | Hex | Use |
| --- | --- | --- |
| Charcoal Black | `#191919` | Navbar, footer, headings |
| Warm Cream | `#F5F0E7` | Page background |
| Vermilion Red | `#C83E2D` | Main buttons, brand mark |
| Muted Gold | `#B79B72` | Accents on dark backgrounds only |
| Olive Grey | `#73766B` | Icons, borders |

Use normal Bootstrap classes; `site.css` already restyles them.

- Main action: `btn btn-primary` (red). Other actions: `btn btn-outline-secondary`.
- Lists: `<div class="table-responsive"><table class="table table-hover align-middle">...</table></div>`. Boxes: `card`.
- Status badge (same look in every module): `<span class="status-badge status-@item.Status">@item.Status</span>`
- Messages after saving: set `TempData["SuccessMessage"]`, `TempData["ErrorMessage"]` or `TempData["InfoMessage"]` before `return RedirectToPage(...)`. A popup appears under the navbar and closes after 5 seconds.

## Branching and pull requests

- Never push directly to `main` or `develop`.
- One branch per user story, from `develop`: `feature/US12-make-reservation`. Fixes: `fix/...`, documentation: `docs/...`.
- Commit messages start with the story ID: `US12: add availability check`.
- Before opening a pull request: pull `develop`, merge `develop` into your branch, build, and test your pages.
- Open the pull request into `develop` and write `Closes #<issue number>` in the description so the issue closes when it is merged.
- Member 1 (Scrum Master) reviews and merges. A pull request with build errors or a failing test is not merged.
- Never commit `wwwroot/uploads`, zip files or secrets.
- At the end of a release, `develop` is merged into `main` and tagged (e.g. `v1.0`).

## Database changes

- Do not rename or delete shared IDs, foreign keys or tables owned by other members.
- To add a column or table: pull `develop`, change the model (add a `DbSet` for a new table), then `Add-Migration <ShortName>`.
- After pulling someone else's migration, run `Update-Database`.
- Check that the model and migrations match: `Add-Migration CheckSnapshot`. Its `Up()` must be empty; then `Remove-Migration`.
- Migration conflict? `Remove-Migration`, pull `develop`, then `Add-Migration` again.

## Enum rules

- Enums are saved in the database as their **name** (e.g. `Paid`), so tables are readable in SSMS.
- Never rename or delete an existing value: old records keep the old word. Adding a new value is safe.
- Changing the label shown to users with `[Display(Name = "...")]` is always safe.
- A new enum: put it in its own file in `Models/`, and add one `HaveConversion<string>()` line in `ConfigureConventions` in `ApplicationDbContext.cs`.

## Troubleshooting

- `Add-Migration` or `Update-Database` stops with warning **NU1902**: run `$ErrorActionPreference = "Continue"` in the Package Manager Console, then run the command again.
- Emails not arriving: check `Email:Password` in User Secrets, then open `/Dev/Emails` to see what the app tried to send.
- Chatbot says the AI is unavailable: check `Ai:ApiKey`, or set `"Ai": { "Enabled": false }` to use FAQ answers only.
- Pay now does nothing or shows an error: check `Stripe:SecretKey` is an `sk_test_` key.
- "Access denied": you're logged in with the wrong role. Use the test account for that role.
- Test account locked ("Too many failed attempts"): wait 15 minutes, or in SSMS open `dbo.AspNetUsers` and set `LockoutEnd` to NULL and `AccessFailedCount` to 0.
- Still logged in after closing the browser: another Chrome window is still open. Close all windows or test in Incognito.
