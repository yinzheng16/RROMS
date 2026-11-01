namespace RROMS.Models
{
    // US08 (Member 1): AI chatbot settings, read from the "Ai" section.
    // Model and address live in appsettings.json.
    // The API key lives ONLY in User Secrets ("Ai:ApiKey"), never in appsettings or GitHub.
    // Works with any OpenAI-compatible service: Google Gemini (default) or Groq, by changing BaseUrl + Model.
    public class AiSettings
    {
        public const string Section = "Ai";

        public string? ApiKey { get; set; }        // User Secrets only

        // false = test the chatbot without the AI (FAQ keywords only), e.g. "Ai": { "Enabled": false } in User Secrets.
        public bool Enabled { get; set; } = true;

        // Flash-Lite answers fastest; if that model can't be used, the fallback model is tried once.
        public string Model { get; set; } = "gemini-flash-lite-latest";
        public string? FallbackModel { get; set; } = "gemini-flash-latest";
        public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";

        // Gemini "thinking" models: keep thinking short so replies are fast. Empty = don't send it.
        public string? ReasoningEffort { get; set; } = "low";

        public int TimeoutSeconds { get; set; } = 15;

        // The same question (without earlier messages) is answered from memory for this long: instant and saves quota.
        public int CacheMinutes { get; set; } = 10;

        // Each visitor may ask this many questions in this many minutes (protects the free quota).
        public int MaxQuestions { get; set; } = 15;
        public int PerMinutes { get; set; } = 5;

        // No key = the chatbot still works, answering from FAQ keywords only.
        public bool CanUseAi => Enabled && !string.IsNullOrWhiteSpace(ApiKey);
    }
}
