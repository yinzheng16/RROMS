namespace RROMS.Models
{
    // Owner: Member 1. Country dialling codes for the phone number dropdown.
    // Used by Register (US01) and can be reused by Edit profile (US04).
    public static class PhoneCountries
    {
        public record Country(string Iso, string Name, string DialCode);

        // Malaysia first (our main customers), then the rest A-Z.
        public static readonly IReadOnlyList<Country> All = new List<Country>
        {
            new("MY", "Malaysia", "+60"),
            new("AU", "Australia", "+61"),
            new("BD", "Bangladesh", "+880"),
            new("BN", "Brunei", "+673"),
            new("KH", "Cambodia", "+855"),
            new("CA", "Canada", "+1"),
            new("CN", "China", "+86"),
            new("FR", "France", "+33"),
            new("DE", "Germany", "+49"),
            new("HK", "Hong Kong", "+852"),
            new("IN", "India", "+91"),
            new("ID", "Indonesia", "+62"),
            new("IT", "Italy", "+39"),
            new("JP", "Japan", "+81"),
            new("LA", "Laos", "+856"),
            new("MO", "Macau", "+853"),
            new("MM", "Myanmar", "+95"),
            new("NP", "Nepal", "+977"),
            new("NZ", "New Zealand", "+64"),
            new("PK", "Pakistan", "+92"),
            new("PH", "Philippines", "+63"),
            new("SA", "Saudi Arabia", "+966"),
            new("SG", "Singapore", "+65"),
            new("KR", "South Korea", "+82"),
            new("LK", "Sri Lanka", "+94"),
            new("TW", "Taiwan", "+886"),
            new("TH", "Thailand", "+66"),
            new("AE", "United Arab Emirates", "+971"),
            new("GB", "United Kingdom", "+44"),
            new("US", "United States", "+1"),
            new("VN", "Vietnam", "+84"),
        };

        public static Country? Find(string? iso) =>
            All.FirstOrDefault(c => c.Iso == iso);
    }
}
