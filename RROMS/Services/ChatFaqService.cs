using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // US10 (Owner: Member 1): the Admin manages the questions and answers the chatbot uses (US08).
    // Changes reach the chatbot straight away: it reads the shown FAQs on every question.
    public class ChatFaqService : IChatFaqService
    {
        public const int PageSize = 8;

        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ChatFaqService> _logger;

        public ChatFaqService(ApplicationDbContext db, IMemoryCache cache, ILogger<ChatFaqService> logger)
        {
            _db = db;
            _cache = cache;
            _logger = logger;
        }

        // The chatbot remembers recent answers; a new "version" makes it forget them after an FAQ change.
        private void ForgetAnswers() => _cache.Set(ChatbotService.DataVersionKey, Guid.NewGuid().ToString("N"));

        public async Task<FaqListPage> GetFaqsAsync(FaqListQuery query)
        {
            var all = _db.ChatFaqs.AsNoTracking();

            var stats = new FaqStats(
                await all.CountAsync(),
                await all.CountAsync(f => f.IsActive),
                await all.CountAsync(f => !f.IsActive));

            // Number = the FAQ's place in the full list (same order as the chatbot's FAQ list).
            var order = await all.OrderBy(f => f.Id).Select(f => f.Id).ToListAsync();
            var numbers = order.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);

            var filtered = all;
            var s = query.Search?.Trim();
            if (!string.IsNullOrEmpty(s))
                filtered = filtered.Where(f => f.Question.Contains(s) || f.Answer.Contains(s) || (f.Keywords != null && f.Keywords.Contains(s)));

            if (string.Equals(query.Status, "shown", StringComparison.OrdinalIgnoreCase)) filtered = filtered.Where(f => f.IsActive);
            else if (string.Equals(query.Status, "hidden", StringComparison.OrdinalIgnoreCase)) filtered = filtered.Where(f => !f.IsActive);

            var matches = await filtered.CountAsync();
            var pageCount = Math.Max(1, (int)Math.Ceiling(matches / (double)PageSize));
            var page = Math.Clamp(query.Page, 1, pageCount);

            var rows = await filtered.OrderBy(f => f.Id)
                .Skip((page - 1) * PageSize).Take(PageSize)
                .ToListAsync();

            return new FaqListPage(
                rows.Select(f => new FaqRow(f.Id, numbers.GetValueOrDefault(f.Id), f.Question, f.Answer, SplitKeywords(f.Keywords), f.IsActive)).ToList(),
                matches, page, pageCount, PageSize, stats);
        }

        public async Task<FaqResult> SaveAsync(FaqInput input)
        {
            var question = Tidy(input.Question);
            var answer = (input.Answer ?? "").Replace("\r\n", "\n").Trim();
            answer = Regex.Replace(answer, @"[ \t]+", " ");
            answer = Regex.Replace(answer, @"\n{3,}", "\n\n");

            if (question.Length == 0) return new(false, "Please type the question.", "Question");
            if (question.Length is < 5 or > 200) return new(false, "The question must be 5 to 200 characters.", "Question");
            if (answer.Length == 0) return new(false, "Please type the answer.", "Answer");
            if (answer.Length is < 2 or > 1000) return new(false, "The answer must be 2 to 1000 characters.", "Answer");

            var keywords = SplitKeywords(input.Keywords);
            var keywordText = string.Join(", ", keywords);
            if (keywordText.Length > 200) return new(false, "Keywords can be up to 200 characters in total.", "Keywords");
            if (keywords.Any(k => k.Length > 40)) return new(false, "Each keyword can be up to 40 characters.", "Keywords");

            var duplicate = await _db.ChatFaqs.AnyAsync(f => f.Id != input.Id && f.Question == question);
            if (duplicate) return new(false, "There is already an FAQ with this question.", "Question");

            ChatFaq faq;
            if (input.Id == 0)
            {
                faq = new ChatFaq();
                _db.ChatFaqs.Add(faq);
            }
            else
            {
                faq = await _db.ChatFaqs.FindAsync(input.Id) ?? throw new KeyNotFoundException();
            }

            faq.Question = question;
            faq.Answer = answer;
            faq.Keywords = keywordText.Length == 0 ? null : keywordText;
            faq.IsActive = input.IsActive;
            await _db.SaveChangesAsync();
            ForgetAnswers();

            _logger.LogInformation("Chatbot FAQ {Id} {Action}.", faq.Id, input.Id == 0 ? "added" : "updated");
            return new(true, input.Id == 0 ? "FAQ added. The chatbot can use it now." : "FAQ saved.", Id: faq.Id);
        }

        public async Task<FaqResult> SetShownAsync(int id, bool shown)
        {
            var faq = await _db.ChatFaqs.FindAsync(id);
            if (faq is null) return new(false, "That FAQ no longer exists.");
            faq.IsActive = shown;
            await _db.SaveChangesAsync();
            ForgetAnswers();
            return new(true, shown ? "FAQ shown in the chatbot." : "FAQ hidden. The chatbot won't use it.");
        }

        public async Task<FaqResult> DeleteAsync(int id)
        {
            var faq = await _db.ChatFaqs.FindAsync(id);
            if (faq is null) return new(false, "That FAQ no longer exists.");
            _db.ChatFaqs.Remove(faq);
            await _db.SaveChangesAsync();
            ForgetAnswers();
            _logger.LogInformation("Chatbot FAQ {Id} deleted.", id);
            return new(true, "FAQ deleted.");
        }

        // "Parking,  car park ,parking" -> ["parking", "car park"]
        private static IReadOnlyList<string> SplitKeywords(string? text) =>
            (text ?? "").Split(new[] { ',', '，', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k => Regex.Replace(k, @"\s+", " ").ToLowerInvariant())
                .Where(k => k.Length > 0)
                .Distinct()
                .ToList();

        private static string Tidy(string? text) => Regex.Replace(text ?? "", @"\s+", " ").Trim();
    }
}
