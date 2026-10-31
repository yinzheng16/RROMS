using System.Net;
using System.Text;
using RROMS.Models;

namespace RROMS.Services
{
    // EN07 / US03 (Member 1): one YORU-style email layout for every email the system sends.
    // Gmail-safe: tables + inline styles only. The logo and emblem are embedded in the email itself
    // (cid:yoru-logo / cid:yoru-emblem, attached by EmailSender), so they show in every inbox,
    // not only on the developer's own computer.
    // Member 4 (US38) can use EmailTemplates.Build(...) for the reservation / order / payment emails.
    public static class EmailTemplates
    {
        public record Row(string Label, string Value);

        public static string Build(
            string kicker,                 // small red line above the title, e.g. "Account security"
            string title,                  // "Your password was changed"
            string name,                   // "Chan Yin Zheng" (greeting)
            string introHtml,              // already-safe HTML (use Encode for user text)
            IEnumerable<Row>? rows = null, // the white facts box
            string? warnHtml = null,       // pink "Wasn't you?" box
            string? buttonText = null,
            string? buttonUrl = null,
            string? smallText = null,      // grey line under the button
            string? footerLine = null)     // address · phone
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><body style=\"margin:0;padding:0;background:#ece6dc\">");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#ece6dc;padding:24px 12px\"><tr><td align=\"center\">");
            sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;width:100%;background:#F5F0E7;border:1px solid #E4DACA;border-radius:10px;overflow:hidden\">");

            // Header: the same logo as the navbar, and the tagline.
            sb.Append("<tr><td align=\"center\" style=\"background:#191919;padding:22px 24px 16px;border-bottom:3px solid #C83E2D\">");
            sb.Append($"<img src=\"cid:yoru-logo\" width=\"180\" alt=\"{Encode(Brand.FullName)}\" style=\"display:block;border:0;width:180px;height:auto\" />");
            sb.Append($"<div style=\"margin-top:8px;font:12px Arial,sans-serif;letter-spacing:4px;color:#B79B72\">{Encode(Brand.TaglineJp)} · {Encode(Brand.Tagline.ToUpperInvariant())}</div>");
            sb.Append("</td></tr>");

            // Title
            sb.Append("<tr><td style=\"padding:26px 30px 4px\">");
            sb.Append($"<div style=\"font:700 11px Arial,sans-serif;letter-spacing:3px;text-transform:uppercase;color:#B23526\">{Encode(kicker)}</div>");
            sb.Append($"<h1 style=\"margin:6px 0 10px;font:600 25px Georgia,'Times New Roman',serif;color:#191919\">{Encode(title)}</h1>");
            sb.Append("</td></tr>");

            // Greeting + text
            sb.Append("<tr><td style=\"padding:0 30px;font:14.5px/1.6 Arial,sans-serif;color:#2b2b2b\">");
            var greetName = string.IsNullOrWhiteSpace(name) ? "there" : name;
            sb.Append("Hi ").Append(Encode(greetName)).Append(",<br />").Append(introHtml);
            sb.Append("</td></tr>");

            // Facts box
            var list = rows?.ToList();
            if (list is { Count: > 0 })
            {
                sb.Append("<tr><td style=\"padding:16px 30px 0\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#ffffff;border:1px solid #E4DACA;border-radius:10px\">");
                for (var i = 0; i < list.Count; i++)
                {
                    var border = i == 0 ? "" : "border-top:1px solid #f0e9dc;";
                    sb.Append($"<tr><td style=\"{border}padding:10px 14px;font:13.5px Arial,sans-serif;color:#5C5F55\">{Encode(list[i].Label)}</td>");
                    sb.Append($"<td align=\"right\" style=\"{border}padding:10px 14px;font:13.5px Arial,sans-serif;color:#191919\">{Encode(list[i].Value)}</td></tr>");
                }
                sb.Append("</table></td></tr>");
            }

            if (!string.IsNullOrEmpty(warnHtml))
            {
                sb.Append("<tr><td style=\"padding:14px 30px 0\"><div style=\"padding:12px 14px;border-radius:10px;background:#fff1ee;border:1px solid #f0c3b9;font:13.5px/1.5 Arial,sans-serif;color:#8f2a1d\">");
                sb.Append(warnHtml);
                sb.Append("</div></td></tr>");
            }

            if (!string.IsNullOrEmpty(buttonText) && !string.IsNullOrEmpty(buttonUrl))
            {
                sb.Append("<tr><td style=\"padding:18px 30px 4px\">");
                sb.Append($"<a href=\"{Encode(buttonUrl)}\" style=\"display:inline-block;background:#C83E2D;color:#ffffff;text-decoration:none;font:700 14.5px Arial,sans-serif;padding:12px 24px;border-radius:8px\">{Encode(buttonText)}</a>");
                sb.Append("</td></tr>");
            }

            sb.Append("<tr><td style=\"padding:12px 30px 22px;font:12.5px/1.55 Arial,sans-serif;color:#5C5F55\">");
            sb.Append(Encode(smallText ?? "You get this email to keep your account safe. We never ask for your password by email."));
            sb.Append("</td></tr>");

            // Footer
            sb.Append("<tr><td align=\"center\" style=\"background:#191919;padding:18px 24px;font:12px/1.6 Arial,sans-serif;color:#bfae92\">");
            sb.Append("<img src=\"cid:yoru-emblem\" width=\"34\" height=\"34\" alt=\"\" style=\"display:block;margin:0 auto 6px;border:0\" />");
            sb.Append($"<span style=\"font:13px Georgia,serif;letter-spacing:4px;color:#D9BF92\">{Encode(Brand.TaglineJp)}</span><br />");
            sb.Append(Encode(footerLine ?? Brand.FullName));
            sb.Append("</td></tr>");

            sb.Append("</table></td></tr></table></body></html>");
            return sb.ToString();
        }

        public static string Encode(string? text) => WebUtility.HtmlEncode(text ?? "");

        // "customer@rroms.test" -> "c••••••r@rroms.test"
        public static string Mask(string? email)
        {
            if (string.IsNullOrEmpty(email)) return "";
            var at = email.IndexOf('@');
            if (at <= 1) return email;
            var name = email[..at];
            var middle = new string('•', Math.Max(1, name.Length - 2));
            return name[0] + middle + (name.Length > 1 ? name[^1].ToString() : "") + email[at..];
        }
    }
}
