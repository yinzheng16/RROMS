namespace RROMS.Models
{
    // EN07 (Member 1): Gmail SMTP settings, read from the "Email" section.
    // Host, port and addresses live in appsettings.json.
    // The App password lives ONLY in User Secrets ("Email:Password"), never in appsettings or GitHub.
    public class EmailSettings
    {
        public const string Section = "Email";

        public string Host { get; set; } = "smtp.gmail.com";
        public int Port { get; set; } = 587;
        public string FromAddress { get; set; } = "";
        public string FromName { get; set; } = Brand.FullName;
        public string? UserName { get; set; }      // empty = same as FromAddress
        public string? Password { get; set; }      // User Secrets only

        // No password (or no sender address) = emails are written to the Output window instead.
        public bool CanSend => !string.IsNullOrWhiteSpace(Password) && !string.IsNullOrWhiteSpace(FromAddress);
    }
}
