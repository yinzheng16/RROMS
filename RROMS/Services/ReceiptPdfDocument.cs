using System.Globalization;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 4 (US35 - PDF payment receipt)
    // The one receipt for a Paid payment: customers download it from
    // My Payments, staff open it from the Bill page to print it.
    //
    // Design follows the staff printed BILL (staff-billing.css, "US35: PRINT
    // TYPOGRAPHY"): A4, centred logo + YORU + I Z A K A Y A, gold dividers,
    // section labels, item rows with "qty × price" under the name, muted
    // labels with right-aligned amounts, red total, centred footer.
    // Sizes are the print CSS sizes in points (1px = 0.75pt).
    //
    // Fonts:
    // - Display (Shippori Mincho): YORU, RECEIPT, the Japanese thank-you line
    // - Body (Zen Kaku Gothic New): everything else
    internal static class ReceiptPdfDocument
    {
        // Colours from the staff print styles.
        private const string Ink = "#24201C";
        private const string Muted = "#77716B";
        // Row labels and "qty × price": a little darker than Muted so they read clearly.
        private const string Soft = "#5C5650";
        private const string Label = "#6F5B47";      // section labels
        // Lines, exactly as on the print bill (staff-billing.css), all 1px (0.75pt):
        // - dividers, top of the item list, under the last item, above Total:
        //   solid #B49A76
        // - between two items: dashed #D8CCB8
        private const string Line = "#B49A76";
        private const string DashLine = "#D8CCB8";
        private const float LineWidth = 0.75f;
        private const string Red = "#A94736";
        private const string Gold = "#9B8062";

        // The website's fonts (site.css). If their .ttf files are in
        // Resources/Fonts they are embedded, so the PDF looks the same on any
        // computer; otherwise the Windows Japanese fonts are used. Each list
        // ends with Yu Gothic / MS Gothic so Japanese never shows as boxes.
        private static readonly string[] Display = { "Shippori Mincho", "Yu Mincho", "MS Mincho", "Yu Gothic", "MS Gothic" };
        private static readonly string[] Body = { "Zen Kaku Gothic New", "Yu Gothic", "Meiryo", "MS Gothic" };

        private static readonly object FontLock = new();
        private static bool _fontsReady;

        private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-MY");

        static ReceiptPdfDocument()
        {
            // QuestPDF Community licence: free for this student project.
            // Set here instead of Program.cs so the shared startup file stays unchanged.
            QuestPDF.Settings.License = LicenseType.Community;

            // Since QuestPDF 2026.9 system fonts are off by default;
            // they are only the fallback when the embedded fonts are missing.
            QuestPDF.Settings.UseSystemFonts = true;

            // The font lists above are fallbacks: use the first one that exists
            // instead of stopping the PDF when one of them is not installed.
            QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
        }

        // Registers the .ttf / .otf files in <content root>/Resources/Fonts once.
        public static void EnsureFonts(string contentRootPath)
        {
            if (_fontsReady) return;

            lock (FontLock)
            {
                if (_fontsReady) return;

                string folder = Path.Combine(contentRootPath, "Resources", "Fonts");
                if (Directory.Exists(folder))
                {
                    foreach (string file in Directory.EnumerateFiles(folder)
                        .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
                    {
                        using var stream = File.OpenRead(file);
                        FontManager.RegisterFont(stream);
                    }
                }

                _fontsReady = true;
            }
        }

        public static byte[] Create(PaymentReceipt r, byte[]? logo)
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
                        Header(col, r, logo);
                        Divider(col, 15);
                        Title(col, r);
                        Items(col, r);
                        Totals(col, r);
                        Divider(col, 10);
                        PaymentDetails(col, r);
                        Divider(col, 10);
                        Footer(col, r);
                    });
                });
            })
            .WithMetadata(new DocumentMetadata
            {
                Title = $"{Brand.FullName} receipt {r.ReceiptNo}",
                Author = Brand.FullName
            })
            .GeneratePdf();
        }

        // Logo, YORU, I Z A K A Y A, SST no., address / phone
        private static void Header(ColumnDescriptor col, PaymentReceipt r, byte[]? logo)
        {
            col.Item().AlignCenter().Column(h =>
            {
                if (logo != null)
                    h.Item().AlignCenter().Width(64).Image(logo);

                h.Item().PaddingTop(8).AlignCenter().Text(Brand.Name)
                    .FontFamily(Display).Bold().FontSize(25.5f).LetterSpacing(0.09f);

                h.Item().AlignCenter().Text(string.Join(" ", Brand.Sub.ToCharArray()))
                    .FontSize(8.25f).FontColor(Gold).LetterSpacing(0.45f);

                if (r.SstRegistrationNo != null)
                    h.Item().PaddingTop(9).AlignCenter().Text($"SST No. {r.SstRegistrationNo}").FontSize(8);

                string contact = string.Join("  ·  ", new[] { r.Address, r.Phone }.Where(s => s != null));
                if (contact.Length > 0)
                    h.Item().PaddingTop(2).AlignCenter().Text(contact).FontSize(8).FontColor(Muted);
            });
        }

        // RECEIPT, Japanese Dining, then receipt / bill no., table or order, paid time
        private static void Title(ColumnDescriptor col, PaymentReceipt r)
        {
            col.Item().AlignCenter().Text("RECEIPT")
                .FontFamily(Display).Bold().FontSize(12).LetterSpacing(0.19f);
            col.Item().AlignCenter().Text("Japanese Dining").FontSize(8).FontColor(Muted);

            col.Item().PaddingTop(16).PaddingBottom(14).Column(info =>
            {
                Row(info, "Receipt No.", r.ReceiptNo);
                if (r.BillNo != null)
                    Row(info, "Bill No.", r.BillNo);
                Row(info, r.OrderType == OrderType.DineIn ? "Dine-in" : "Takeaway", Visit(r));
                Row(info, "Paid", When(r.PaidAt));
            });
        }

        // ORDERED ITEMS: name + amount, "qty × unit price" underneath
        private static void Items(ColumnDescriptor col, PaymentReceipt r)
        {
            SectionLabel(col, "Ordered Items");
            GoldLine(col.Item());

            for (int i = 0; i < r.Lines.Count; i++)
            {
                var line = r.Lines[i];

                col.Item()
                    .PaddingVertical(6)
                    .Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(line.Name).Medium();
                            c.Item().Text($"{line.Quantity} × {Money(line.UnitPrice)}").FontSize(7.9f).FontColor(Soft);
                        });
                        row.AutoItem().AlignMiddle().Text(Money(line.LineTotal)).Medium();
                    });

                // Last item: solid gold; between items: dashed, like the print bill.
                if (i == r.Lines.Count - 1)
                    GoldLine(col.Item());
                else
                    col.Item().LineHorizontal(LineWidth).LineColor(DashLine)
                        .LineDashPattern(new[] { 2.25f, 2.25f });
            }
        }

        // Subtotal / Service Charge / Tax, then Total in red
        private static void Totals(ColumnDescriptor col, PaymentReceipt r)
        {
            col.Item().PaddingTop(12).Column(t =>
            {
                Row(t, "Subtotal", Money(r.Subtotal));
                Row(t, "Service Charge", Money(r.ServiceCharge));
                Row(t, "Tax", Money(r.TaxAmount));
            });

            Divider(col, 7.5f);

            col.Item().Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text("Total").FontSize(11.25f).Bold();
                row.AutoItem().AlignMiddle().Text(Money(r.TotalAmount)).FontSize(11.25f).Bold().FontColor(Red);
            });
        }

        // PAYMENT: method, amount, reference (card / online), cashier (counter)
        private static void PaymentDetails(ColumnDescriptor col, PaymentReceipt r)
        {
            SectionLabel(col, "Payment");

            Row(col, "Method", MethodLabel(r.Method));
            Row(col, "Amount paid", Money(r.AmountPaid));
            if (r.TransactionRef != null)
                Row(col, "Reference", r.TransactionRef);
            if (r.CashierName != null)
                Row(col, "Cashier", r.CashierName);
        }

        // ありがとうございます, footer note, YORU IZAKAYA, notice
        private static void Footer(ColumnDescriptor col, PaymentReceipt r)
        {
            col.Item().PaddingTop(5).AlignCenter().Column(f =>
            {
                f.Item().AlignCenter().Text("ありがとうございます")
                    .FontFamily(Display).Bold().FontSize(12).FontColor(Gold);
                f.Item().PaddingTop(4).AlignCenter()
                    .Text(r.ReceiptFooterNote ?? "Thank you for dining with us.")
                    .FontSize(8).FontColor(Muted);
                f.Item().PaddingTop(9).AlignCenter().Text(Brand.FullName)
                    .FontSize(8).Bold().FontColor(Red).LetterSpacing(0.19f);
                f.Item().PaddingTop(12).AlignCenter()
                    .Text("Payment received. Computer-generated receipt, no signature required.")
                    .FontSize(7.5f).Italic().FontColor(Muted);
            });
        }

        // ---------- Small helpers ----------

        private static void Divider(ColumnDescriptor col, float space) =>
            GoldLine(col.Item().PaddingVertical(space));

        private static void GoldLine(IContainer container) =>
            container.LineHorizontal(LineWidth).LineColor(Line);

        // Same as the print CSS h6: small, bold, upper case, spaced out
        private static void SectionLabel(ColumnDescriptor col, string text) =>
            col.Item().PaddingBottom(6).Text(text.ToUpperInvariant())
                .FontSize(7.5f).Bold().FontColor(Label).LetterSpacing(0.15f);

        // Every label / value row (Receipt No., Bill No., Dine-in, Paid, Subtotal,
        // Service Charge, Tax, Method, Amount paid, Reference, Cashier) looks like
        // the print bill's Subtotal row: body font 9pt (12px), soft label on the
        // left, value on the right, so all values share one right edge.
        private static void Row(ColumnDescriptor col, string label, string value)
        {
            col.Item().PaddingBottom(3).Row(row =>
            {
                row.RelativeItem().Text(label).FontSize(9).FontColor(Soft);
                row.AutoItem().AlignRight().Text(value).FontSize(9).FontColor(Ink);
            });
        }

        // "Table TFT001 · 2 rounds" / "Order YR2610..."
        private static string Visit(PaymentReceipt r)
        {
            if (r.OrderType == OrderType.DineIn)
            {
                string table = string.IsNullOrWhiteSpace(r.TableNumber) ? "Table" : $"Table {r.TableNumber}";
                return r.OrderCount > 1 ? $"{table} · {r.OrderCount} rounds" : table;
            }

            return string.IsNullOrWhiteSpace(r.OrderNo) ? "Order" : $"Order {r.OrderNo}";
        }

        private static string Money(decimal amount) =>
            "RM " + amount.ToString("N2", CultureInfo.InvariantCulture);

        private static string When(DateTime at) => at.ToString("d MMM yyyy, h:mm tt", En);

        private static string MethodLabel(PaymentMethod method) => method switch
        {
            PaymentMethod.Cash => "Cash",
            PaymentMethod.Card => "Card",
            PaymentMethod.Online => "Online",
            _ => method.ToString()
        };
    }
}
