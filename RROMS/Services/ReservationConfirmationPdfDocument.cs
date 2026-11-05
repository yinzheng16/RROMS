using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 4 (US36 - Reservation confirmation PDF)
    // Same look as the US35 receipt (ReceiptPdfDocument): A4, centred logo +
    // YORU + I Z A K A Y A, gold dividers, section labels, soft labels with
    // right-aligned values, centred footer.
    // The QuestPDF licence and fonts are set up by ReceiptPdfDocument.EnsureFonts.
    internal static class ReservationConfirmationPdfDocument
    {
        // Colours: the same as the receipt.
        private const string Ink = "#24201C";
        private const string Muted = "#77716B";
        private const string Soft = "#5C5650";
        private const string Label = "#6F5B47";
        private const string Line = "#B49A76";
        private const float LineWidth = 0.75f;
        private const string Red = "#A94736";
        private const string Gold = "#9B8062";

        private static readonly string[] Display = { "Shippori Mincho", "Yu Mincho", "MS Mincho", "Yu Gothic", "MS Gothic" };
        private static readonly string[] Body = { "Zen Kaku Gothic New", "Yu Gothic", "Meiryo", "MS Gothic" };

        private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-MY");

        public static byte[] Create(ReservationConfirmation c, byte[]? logo)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(18, Unit.Millimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontColor(Ink).FontFamily(Body).LineHeight(1.4f));

                    page.Content().Column(col =>
                    {
                        Header(col, c, logo);
                        Divider(col, 15);
                        Title(col, c);
                        Divider(col, 10);
                        Booking(col, c);
                        Divider(col, 10);
                        Requests(col, c);
                        Divider(col, 10);
                        Footer(col, c);
                    });
                });
            })
            .WithMetadata(new DocumentMetadata
            {
                Title = $"{Brand.FullName} reservation {c.ReferenceNo}",
                Author = Brand.FullName
            })
            .GeneratePdf();
        }

        // Logo, YORU, I Z A K A Y A, address / phone
        private static void Header(ColumnDescriptor col, ReservationConfirmation c, byte[]? logo)
        {
            col.Item().AlignCenter().Column(h =>
            {
                if (logo != null)
                    h.Item().AlignCenter().Width(64).Image(logo);

                h.Item().PaddingTop(8).AlignCenter().Text(Brand.Name)
                    .FontFamily(Display).Bold().FontSize(25.5f).LetterSpacing(0.09f);

                h.Item().AlignCenter().Text(string.Join(" ", Brand.Sub.ToCharArray()))
                    .FontSize(8.25f).FontColor(Gold).LetterSpacing(0.45f);

                string contact = string.Join("  ·  ", new[] { c.Address, c.Phone }.Where(s => s != null));
                if (contact.Length > 0)
                    h.Item().PaddingTop(9).AlignCenter().Text(contact).FontSize(8).FontColor(Muted);
            });
        }

        // RESERVATION CONFIRMATION (Confirmed) or BOOKING REQUEST (Pending)
        private static void Title(ColumnDescriptor col, ReservationConfirmation c)
        {
            col.Item().AlignCenter().Text(c.IsConfirmed ? "RESERVATION CONFIRMATION" : "BOOKING REQUEST")
                .FontFamily(Display).Bold().FontSize(12).LetterSpacing(0.19f);
            col.Item().AlignCenter().Text("Japanese Dining").FontSize(8).FontColor(Muted);
        }

        // BOOKING: reference, guest, date, time, guests, table, area, spend, status
        private static void Booking(ColumnDescriptor col, ReservationConfirmation c)
        {
            SectionLabel(col, "Booking");

            Row(col, "Reference", c.ReferenceNo);
            Row(col, "Guest name", c.GuestName);
            Row(col, "Date", c.ReservedAt.ToString("ddd, d MMM yyyy", En));
            Row(col, "Arrival time", c.ReservedAt.ToString("h:mm tt", CultureInfo.InvariantCulture));
            Row(col, "Guests", c.Guests.ToString(CultureInfo.InvariantCulture));

            // Confirmed: "TFT001 · Tatami / 座敷"; Pending: the table type only.
            if (c.TableNumber != null)
                Row(col, "Table", c.TableType != null ? $"{c.TableNumber}  ·  {c.TableType}" : c.TableNumber);
            else if (c.TableType != null)
                Row(col, "Table type", c.TableType);

            if (c.AreaName != null)
                Row(col, "Area", c.AreaName);

            Row(col, "Minimum spend", c.MinimumSpend > 0m
                ? $"RM {c.MinimumSpend.ToString("N0", CultureInfo.InvariantCulture)} per booking"
                : "No minimum spend");

            col.Item().PaddingBottom(3).Row(row =>
            {
                row.RelativeItem().Text("Status").FontSize(9).FontColor(Soft);
                row.AutoItem().AlignRight().Text(c.IsConfirmed ? "Confirmed" : "Pending")
                    .FontSize(9).Bold().FontColor(c.IsConfirmed ? Red : Gold);
            });
        }

        private static void Requests(ColumnDescriptor col, ReservationConfirmation c)
        {
            SectionLabel(col, "Special Requests");

            if (c.SpecialRequest != null)
                col.Item().Text(c.SpecialRequest).FontSize(9);
            else
                col.Item().Text("None").FontSize(9).FontColor(Muted);
        }

        // お待ちしております (Confirmed) or a "waiting for confirmation" note (Pending)
        private static void Footer(ColumnDescriptor col, ReservationConfirmation c)
        {
            col.Item().PaddingTop(5).AlignCenter().Column(f =>
            {
                if (c.IsConfirmed)
                {
                    f.Item().AlignCenter().Text("お待ちしております")
                        .FontFamily(Display).Bold().FontSize(12).FontColor(Gold);
                    f.Item().PaddingTop(4).AlignCenter()
                        .Text($"Please arrive on time. Your table is held for {c.GraceMinutes} minutes after the arrival time.")
                        .FontSize(8).FontColor(Muted);
                    f.Item().AlignCenter()
                        .Text("Your table may change on the day.")
                        .FontSize(8).FontColor(Muted);
                }
                else
                {
                    f.Item().AlignCenter()
                        .Text("Waiting for the restaurant to confirm.")
                        .FontSize(8).FontColor(Muted);
                    f.Item().AlignCenter()
                        .Text("Your table number appears once the booking is confirmed.")
                        .FontSize(8).FontColor(Muted);
                }

                f.Item().PaddingTop(9).AlignCenter().Text(Brand.FullName)
                    .FontSize(8).Bold().FontColor(Red).LetterSpacing(0.19f);

                string issued = c.IssuedAt.ToString("d MMM yyyy, h:mm tt", En);
                f.Item().PaddingTop(12).AlignCenter()
                    .Text(c.IsConfirmed
                        ? $"Issued {issued}. Computer-generated, no signature required."
                        : $"Issued {issued}. Not yet confirmed.")
                    .FontSize(7.5f).Italic().FontColor(Muted);
            });
        }

        // ---------- Small helpers (same as the receipt) ----------

        private static void Divider(ColumnDescriptor col, float space) =>
            col.Item().PaddingVertical(space).LineHorizontal(LineWidth).LineColor(Line);

        private static void SectionLabel(ColumnDescriptor col, string text) =>
            col.Item().PaddingBottom(6).Text(text.ToUpperInvariant())
                .FontSize(7.5f).Bold().FontColor(Label).LetterSpacing(0.15f);

        private static void Row(ColumnDescriptor col, string label, string value)
        {
            col.Item().PaddingBottom(3).Row(row =>
            {
                row.RelativeItem().Text(label).FontSize(9).FontColor(Soft);
                row.AutoItem().AlignRight().Text(value).FontSize(9).FontColor(Ink);
            });
        }
    }
}
