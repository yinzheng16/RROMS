using System.Collections.Concurrent;

namespace RROMS.Services
{
    // EN07 (Member 1): the last 20 emails, kept in memory in Development only,
    // so /Dev/Emails can show them (with their links) while no Gmail password is set.
    // Cleared every time the app restarts. Nothing is saved to the database.
    public record OutboxEmail(DateTime At, string To, string Subject, string Html,
                              IReadOnlyList<string> Links, string Status, bool Ok);

    public class EmailOutbox
    {
        private const int Keep = 20;
        private readonly ConcurrentQueue<OutboxEmail> _emails = new();

        public void Add(OutboxEmail email)
        {
            _emails.Enqueue(email);
            while (_emails.Count > Keep && _emails.TryDequeue(out _)) { }
        }

        // Newest first.
        public IReadOnlyList<OutboxEmail> All() => _emails.Reverse().ToList();

        public void Clear() => _emails.Clear();
    }
}
