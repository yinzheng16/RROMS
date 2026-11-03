using PhoneNumbers;

namespace RROMS.Services
{
    // Turns a saved phone number (+60321426688) into something people can read.
    // Saving and validating is still done by IAccountService.FormatPhoneNumber.
    public static class PhoneDisplay
    {
        private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

        // "+60321426688" -> "+60 3-2142 6688" (footer, Home "Visit us", bills)
        public static string Pretty(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
            try
            {
                return Util.Format(Util.Parse(phone, "MY"), PhoneNumberFormat.INTERNATIONAL);
            }
            catch (NumberParseException)
            {
                return phone;   // old or odd data: show as saved
            }
        }

        // "+60321426688" -> "03-2142 6688" (the box next to the +60 picker)
        public static string? National(string? phone, string? countryIso)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            try
            {
                return Util.Format(Util.Parse(phone, countryIso ?? "MY"), PhoneNumberFormat.NATIONAL);
            }
            catch (NumberParseException)
            {
                return phone;
            }
        }
    }
}
