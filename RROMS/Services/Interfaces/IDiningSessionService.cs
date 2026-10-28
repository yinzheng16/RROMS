using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // Owner: Member 2 (Tables & Reservations)
    // Minimal read/claim API for Member 3 (ordering). Opening and closing sessions
    // is added later in US17 / US54.
    public interface IDiningSessionService
    {
        // The open session at this table, or null if none.
        Task<DiningSession?> GetOpenSessionForTableAsync(int tableId);

        // All tables that currently have an open session (for the order page table list).
        Task<List<OpenSessionTableDto>> GetOpenSessionTablesAsync();

        // Lets a customer order on this session.
        // - Session has an owner: allowed only if it is the same customer.
        // - Session has no owner (walk-in): the first customer to order claims it.
        Task<SessionClaimResult> ClaimOrValidateSessionAsync(int sessionId, string userId);
    }

    public record OpenSessionTableDto(int SessionId, int TableId, string TableNumber, DateTime OpenedAt, bool HasCustomer);

    // Code: ok, claimed, taken, closed, not_found
    public record SessionClaimResult(bool Success, string Code, string Message);
}