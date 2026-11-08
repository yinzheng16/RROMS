// Owner: Member 1 (Chan Yin Zheng). UNIT TESTS: one class at a time, with its other parts replaced by
// fakes or a temporary in-memory database. Test IDs match Table 6.x in the individual report.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;
using RROMS.Services.Interfaces;

namespace RROMS.Tests.Member1;

// ---------- US01: phone number check (AccountService.FormatPhoneNumber) ----------
public class PhoneNumberTests
{
    // The phone check does not use the database or Identity, so they are not needed here.
    private readonly AccountService _accounts = new(null!, null!);

    [Fact]
    public void UT01_ValidMalaysianMobile_IsSavedInInternationalFormat()
    {
        Assert.Equal("+60123456789", _accounts.FormatPhoneNumber("MY", "012-345 6789"));
    }

    [Fact]
    public void UT02_TooShortNumber_IsRejected()
    {
        Assert.Null(_accounts.FormatPhoneNumber("MY", "1234"));
    }

    [Fact]
    public void UT03_Letters_AreRejected()
    {
        Assert.Null(_accounts.FormatPhoneNumber("MY", "abcdefg"));
    }
}

// ---------- US52: reset links work for one hour ----------
public class PasswordResetTokenTests
{
    [Fact]
    public void UT04_ResetLinkProvider_LastsOneHour()
    {
        var options = new PasswordResetTokenProviderOptions();
        Assert.Equal("PasswordReset", options.Name);
        Assert.Equal(TimeSpan.FromHours(1), options.TokenLifespan);
    }
}

// ---------- US10: chatbot FAQ validation (ChatFaqService) ----------
public class ChatFaqServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly ChatFaqService _faqs;

    public ChatFaqServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _faqs = new ChatFaqService(_db, _cache, NullLogger<ChatFaqService>.Instance);
    }

    [Fact]
    public async Task UT05_ValidFaq_IsSavedWithTidyKeywords()
    {
        var result = await _faqs.SaveAsync(new FaqInput(0, "Is there parking nearby?", "Yes, at Lot 10.", "Parking,  car park ,parking", true));

        Assert.True(result.Ok);
        var saved = await _db.ChatFaqs.SingleAsync();
        Assert.Equal("parking, car park", saved.Keywords);
    }

    [Fact]
    public async Task UT06_TooShortQuestion_IsRejected()
    {
        var result = await _faqs.SaveAsync(new FaqInput(0, "Hi?", "Hello.", null, true));

        Assert.False(result.Ok);
        Assert.Equal("Question", result.Field);
        Assert.Equal(0, await _db.ChatFaqs.CountAsync());
    }

    [Fact]
    public async Task UT07_DuplicateQuestion_IsRejected()
    {
        await _faqs.SaveAsync(new FaqInput(0, "Is the food halal?", "No, we serve pork.", null, true));
        var result = await _faqs.SaveAsync(new FaqInput(0, "Is the food halal?", "Another answer.", null, true));

        Assert.False(result.Ok);
        Assert.Contains("already", result.Message);
        Assert.Equal(1, await _db.ChatFaqs.CountAsync());
    }

    [Fact]
    public async Task UT08_SavingAnFaq_ClearsTheChatbotAnswerCache()
    {
        _cache.Set(ChatbotService.DataVersionKey, "old-version");

        await _faqs.SaveAsync(new FaqInput(0, "Do you have vegetarian dishes?", "Yes, salads and tofu.", "vegetarian", true));

        Assert.NotEqual("old-version", _cache.Get<string>(ChatbotService.DataVersionKey));
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); _cache.Dispose(); }
}

// ---------- US08: chatbot without the AI (FAQ fallback and question limit) ----------
public class ChatbotFallbackTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly ChatbotService _bot;

    public ChatbotFallbackTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.ChatFaqs.Add(new ChatFaq { Question = "Is there parking nearby?", Answer = "Yes, at Lot 10 next door.", Keywords = "parking, car park", IsActive = true });
        _db.ChatFaqs.Add(new ChatFaq { Question = "Do you sell gift cards?", Answer = "Hidden answer.", Keywords = "gift card", IsActive = false });
        _db.SaveChanges();

        // AI switched off, as with no API key: every answer must come from the FAQs and built-in answers.
        var ai = new AiSettings { Enabled = false, MaxQuestions = 15, PerMinutes = 5 };
        _bot = new ChatbotService(_db, new FakeHomeService(), null!, null!, _cache, Options.Create(ai), NullLogger<ChatbotService>.Instance);
    }

    [Fact]
    public async Task UT09_AiOff_AnswersFromMatchingFaq()
    {
        var reply = await _bot.AskAsync("Is there parking nearby?", null, "visitor-1");

        Assert.Equal("faq", reply.Source);
        Assert.Equal("Yes, at Lot 10 next door.", reply.Text);
    }

    [Fact]
    public async Task UT10_HiddenFaq_IsNeverUsed()
    {
        var reply = await _bot.AskAsync("Do you sell a gift card?", null, "visitor-2");

        Assert.NotEqual("faq", reply.Source);
        Assert.DoesNotContain("Hidden answer", reply.Text);
    }

    [Fact]
    public async Task UT11_UnknownQuestion_GivesFriendlyReplyWithPhone()
    {
        var reply = await _bot.AskAsync("qwerty zxcvb", null, "visitor-3");

        Assert.Contains("not sure", reply.Text);
        Assert.Contains(FakeHomeService.Phone, reply.Text);
    }

    [Fact]
    public async Task UT12_SixteenthQuestionInFiveMinutes_IsLimited()
    {
        for (var i = 0; i < 15; i++)
            Assert.NotEqual("limit", (await _bot.AskAsync("Is there parking nearby?", null, "visitor-4")).Source);

        var sixteenth = await _bot.AskAsync("Is there parking nearby?", null, "visitor-4");

        Assert.Equal("limit", sixteenth.Source);
        // Another visitor is not affected.
        Assert.Equal("faq", (await _bot.AskAsync("Is there parking nearby?", null, "visitor-5")).Source);
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); _cache.Dispose(); }
}
