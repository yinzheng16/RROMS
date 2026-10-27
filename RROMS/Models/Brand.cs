namespace RROMS.Models
{
    // The restaurant's brand, in one place (Owner: Member 1).
    // Used by the layout, bills / receipts (Member 4) and emails (EN07).
    // Not an admin setting: the name also appears in the site's images and text,
    // so it is changed here by the developers only.
    public static class Brand
    {
        public const string Name = "YORU";
        public const string Sub = "IZAKAYA";
        public const string FullName = "YORU IZAKAYA";

        public const string TaglineJp = "夜はこれから";
        public const string Tagline = "The night is still young.";

        public const string Logo = "/images/yoru-logo-dark.png";
        public const string Favicon = "/images/yoru-icon.png";
    }
}
