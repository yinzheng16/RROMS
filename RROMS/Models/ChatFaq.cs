using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 1 (Identity, Access & AI Chatbot)
    // Shared IDs and foreign keys: do not rename or delete without telling the team.
    public class ChatFaq
    {
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string Question { get; set; } = string.Empty;

        [Required, StringLength(1000)]
        public string Answer { get; set; } = string.Empty;

        // Comma-separated words used by the FAQ fallback search.
        [StringLength(200)]
        public string? Keywords { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
