using System.Text;
using RROMS.Models;

namespace RROMS.Services
{
    // US39 (Member 4): the announcement / campaign email.
    // Same header and footer as every YORU email (EN07 EmailTemplates, Member 1), so it is clearly
    // the same restaurant, but the body is a small "poster" for the offer instead of a facts box:
    //   NEWS · お知らせ tag, big title, red date pill, then the greeting, the message and one big button.
    // Gmail-safe: tables + inline styles only; the logo / emblem are cid images attached by EmailSender.
    public static class AnnouncementEmail
    {
        public static string Build(
            string title,
            string message,
            string dates,           // "3 – 9 Nov 2026"
            string name,            // greeting
            string? buttonText,
            string? buttonUrl,      // absolute URL; null = no button
            string footerLine)
        {
            static string E(string? s) => EmailTemplates.Encode(s);
            var sb = new StringBuilder();

            sb.Append("<!doctype html><html><body style=\"margin:0;padding:0;background:#ece6dc\">");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#ece6dc;padding:24px 12px\"><tr><td align=\"center\">");
            sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;width:100%;background:#F5F0E7;border:1px solid #E4DACA;border-radius:10px;overflow:hidden\">");

            // Header (same as the shared layout)
            sb.Append("<tr><td align=\"center\" style=\"background:#191919;padding:22px 24px 16px;border-bottom:3px solid #C83E2D\">");
            sb.Append($"<img src=\"cid:yoru-logo\" width=\"180\" alt=\"{E(Brand.FullName)}\" style=\"display:block;border:0;width:180px;height:auto\" />");
            sb.Append($"<div style=\"margin-top:8px;font:12px Arial,sans-serif;letter-spacing:4px;color:#B79B72\">{E(Brand.TaglineJp)} · {E(Brand.Tagline.ToUpperInvariant())}</div>");
            sb.Append("</td></tr>");

            // Poster: tag, title, date pill (background colour first, for inboxes without gradients)
            sb.Append("<tr><td style=\"padding:24px 24px 0\">");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background-color:#3a150d;background-image:linear-gradient(135deg,#2a110b 0%,#4b1b11 55%,#6a2616 100%);border:1px solid #B79B72;border-radius:12px\">");
            sb.Append("<tr><td align=\"center\" style=\"padding:30px 28px 28px\">");
            sb.Append("<div style=\"display:inline-block;padding:4px 12px;border:1px solid #D9BF92;border-radius:4px;font:700 11px Arial,sans-serif;letter-spacing:4px;color:#ECD29E\">NEWS · お知らせ</div>");
            sb.Append($"<h1 style=\"margin:16px 0 12px;font:600 27px/1.25 Georgia,'Times New Roman',serif;color:#ffffff\">{E(title)}</h1>");
            sb.Append($"<div style=\"display:inline-block;padding:6px 16px;border-radius:999px;background:#C83E2D;font:700 13px Arial,sans-serif;letter-spacing:1px;color:#ffffff\">{E(dates)}</div>");
            sb.Append("<div style=\"width:60px;margin:18px auto 0;border-top:1px solid #B79B72;font-size:0;line-height:0\">&nbsp;</div>");
            sb.Append("</td></tr></table>");
            sb.Append("</td></tr>");

            // Greeting + message (line breaks kept)
            var greetName = string.IsNullOrWhiteSpace(name) ? "there" : name;
            var body = E(message).Replace("\r\n", "\n").Replace("\n", "<br />");
            sb.Append($"<tr><td style=\"padding:22px 34px 0;font:15px/1.7 Arial,sans-serif;color:#2b2b2b\">Hi {E(greetName)},<br />{body}</td></tr>");

            // One big button
            if (!string.IsNullOrEmpty(buttonText) && !string.IsNullOrEmpty(buttonUrl))
            {
                sb.Append("<tr><td align=\"center\" style=\"padding:22px 34px 6px\">");
                sb.Append($"<a href=\"{E(buttonUrl)}\" style=\"display:inline-block;padding:14px 38px;border-radius:999px;background:#C83E2D;color:#ffffff;text-decoration:none;font:700 15px Arial,sans-serif\">{E(buttonText)} &rarr;</a>");
                sb.Append("</td></tr>");
            }

            sb.Append("<tr><td align=\"center\" style=\"padding:12px 34px 24px;font:12.5px/1.55 Arial,sans-serif;color:#5C5F55\">You get this email because you have a YORU account.</td></tr>");

            // Footer (same as the shared layout)
            sb.Append("<tr><td align=\"center\" style=\"background:#191919;padding:18px 24px;font:12px/1.6 Arial,sans-serif;color:#bfae92\">");
            sb.Append("<img src=\"cid:yoru-emblem\" width=\"34\" height=\"34\" alt=\"\" style=\"display:block;margin:0 auto 6px;border:0\" />");
            sb.Append($"<span style=\"font:13px Georgia,serif;letter-spacing:4px;color:#D9BF92\">{E(Brand.TaglineJp)}</span><br />");
            sb.Append(E(footerLine));
            sb.Append("</td></tr>");

            sb.Append("</table></td></tr></table></body></html>");
            return sb.ToString();
        }
    }
}
