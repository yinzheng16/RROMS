namespace RROMS.Models
{
    // US33 (Member 4): Stripe test-mode settings, read from the "Stripe" section.
    // The secret key and webhook secret live ONLY in User Secrets
    // ("Stripe:SecretKey", "Stripe:WebhookSecret"), never in appsettings or GitHub.
    // No secret key = online payment is switched off and the
    // "Pay online" button is hidden; the rest of the site still works.
    public class StripeSettings
    {
        public const string Section = "Stripe";

        public string? SecretKey { get; set; }         // sk_test_... (User Secrets only)
        public string? WebhookSecret { get; set; }     // whsec_... from "stripe listen" (optional)

        // Stripe currency code for Malaysian ringgit.
        public string Currency { get; set; } = "myr";

        // An online payment not finished within this time is treated as failed.
        public int CheckoutMinutes { get; set; } = 30;

        // Only test keys are accepted, so a live key can never charge real money.
        public bool IsEnabled =>
            !string.IsNullOrWhiteSpace(SecretKey) &&
            SecretKey.StartsWith("sk_test_", StringComparison.Ordinal);

        public bool HasWebhookSecret => !string.IsNullOrWhiteSpace(WebhookSecret);
    }
}
