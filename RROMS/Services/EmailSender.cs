using System.Net;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using MimeKit;
using RROMS.Models;

namespace RROMS.Services
{
    // EN07 Shared email sender (Owner: Member 1). Agreed with Member 4 on 26 Oct.
    //
    // Every module sends email through the standard IEmailSender:
    //     await _email.SendEmailAsync(toAddress, subject, htmlBody);
    // Identity's own pages (e.g. Forgot password) use it too.
    //
    // No Gmail App password in User Secrets -> the email is NOT sent; it is written to the
    //   Output window and shown on /Dev/Emails (Development only), links included.
    // Password set -> sent through Gmail SMTP with MailKit.
    // A failed send is logged and never throws, so a booking, order or payment never stops because of email.
    public class EmailSender : IEmailSender
    {
        private static readonly Regex LinkPattern = new("href\\s*=\\s*[\"']([^\"']+)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly EmailSettings _settings;
        private readonly EmailOutbox _outbox;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IOptions<EmailSettings> settings, EmailOutbox outbox,
                           IWebHostEnvironment env, ILogger<EmailSender> logger)
        {
            _settings = settings.Value;
            _outbox = outbox;
            _env = env;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            htmlMessage ??= "";
            subject ??= "";
            var links = FindLinks(htmlMessage);

            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogWarning("Email not sent: no recipient address (subject: {Subject}).", subject);
                return;
            }

            // ---------- No password: write it out instead of sending ----------
            if (!_settings.CanSend)
            {
                if (_env.IsDevelopment())
                {
                    _logger.LogInformation(
                        "EMAIL (not sent: no Gmail App password in User Secrets)\n  To: {To}\n  Subject: {Subject}\n  Links:\n{Links}\n  Text:\n{Text}\n  Open /Dev/Emails to see it.",
                        email, subject,
                        links.Count == 0 ? "    (none)" : string.Join("\n", links.Select(l => "    " + l)),
                        Indent(ToPlainText(htmlMessage)));
                    _outbox.Add(new OutboxEmail(DateTime.Now, email, subject, htmlMessage, links,
                        "Not sent: no password (Output only)", true));
                }
                else
                {
                    _logger.LogWarning("Email not sent to {To}: Email:Password is not set.", email);
                }
                return;
            }

            // ---------- Send through Gmail ----------
            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
                message.To.Add(MailboxAddress.Parse(email.Trim()));
                message.Subject = subject;
                message.Body = new BodyBuilder
                {
                    HtmlBody = htmlMessage,
                    TextBody = ToPlainText(htmlMessage)   // for mail apps that don't show HTML
                }.ToMessageBody();

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var client = new SmtpClient { Timeout = 15000 };

                var security = _settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                await client.ConnectAsync(_settings.Host, _settings.Port, security, timeout.Token);

                // Google shows the App password as "abcd efgh ijkl mnop"; the spaces are not part of it.
                var userName = string.IsNullOrWhiteSpace(_settings.UserName) ? _settings.FromAddress : _settings.UserName;
                await client.AuthenticateAsync(userName, _settings.Password!.Replace(" ", ""), timeout.Token);

                await client.SendAsync(message, timeout.Token);
                await client.DisconnectAsync(true, timeout.Token);

                _logger.LogInformation("Email sent to {To}: {Subject}", email, subject);
                if (_env.IsDevelopment())
                    _outbox.Add(new OutboxEmail(DateTime.Now, email, subject, htmlMessage, links, "Sent through Gmail", true));
            }
            catch (Exception ex)
            {
                // Logged, never thrown: the caller carries on.
                _logger.LogError(ex, "Email to {To} failed ({Subject}): {Reason}", email, subject, ex.Message);
                if (_env.IsDevelopment())
                    _outbox.Add(new OutboxEmail(DateTime.Now, email, subject, htmlMessage, links,
                        "Failed: " + Explain(ex), false));
            }
        }

        // ---------- helpers ----------

        private static List<string> FindLinks(string html) =>
            LinkPattern.Matches(html)
                .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
                .Where(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

        // A plain-text copy of the HTML: links kept as "text (link)", tags removed.
        private static string ToPlainText(string html)
        {
            var text = Regex.Replace(html, "<a[^>]*href\\s*=\\s*[\"']([^\"']+)[\"'][^>]*>(.*?)</a>", "$2 ($1)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<(br|/p|/div|/h[1-6]|/li|/tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<(style|script)[^>]*>.*?</\\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<[^>]+>", "");
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, "[ \\t]+", " ");
            text = Regex.Replace(text, "\\s*\\n\\s*", "\n").Trim();
            return text;
        }

        private static string Indent(string text) =>
            string.Join("\n", text.Split('\n').Select(l => "    " + l));

        // A short reason a person can act on.
        private static string Explain(Exception ex) => ex switch
        {
            AuthenticationException => "Gmail refused the login. Check the App password in User Secrets.",
            SmtpCommandException s when s.StatusCode == SmtpStatusCode.MailboxUnavailable => "The address doesn't exist.",
            ParseException => "The email address is not valid.",
            OperationCanceledException => "Gmail did not answer in time. Check the internet connection.",
            System.Net.Sockets.SocketException => "Could not reach smtp.gmail.com. Check the internet connection.",
            _ => ex.Message
        };
    }
}
