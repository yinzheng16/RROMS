using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // Owner: Member 2 (Tables & Reservations)
    // Read/claim API for Member 3 (ordering) + open/close (US17).
    public interface IDiningSessionService
    {
        // The open session at this table, or null if none.
        Task<DiningSession?> GetOpenSessionForTableAsync(int tableId);

        // All tables that currently have an open session (for the order page table list).
        Task<List<OpenSessionTableDto>> GetOpenSessionTablesAsync();

        // Lets a customer order on this session.
        // - Session has an owner: allowed only if it is the same customer.
        // - Session has no owner: the first customer to order claims it.
        Task<SessionClaimResult> ClaimOrValidateSessionAsync(int sessionId, string userId);

        // ---- US17 ----

        // Marks the reservation Seated, opens the dining session for its table (owner = the booking's
        // customer) and attaches the orders the customer placed before arriving. All or nothing.
        // role: "Staff" or "Admin" (written to the status history).
        Task<SessionOpenResult> OpenSessionForReservationAsync(
            int reservationId, string changedByUserId, string role, CancellationToken ct = default);

        // Guests have left: closes the session, sets its reservation to Completed and frees the table.
        // Payment alone never calls this. Safe to call twice (second call returns "already_closed").
        Task<SessionCloseResult> CloseSessionAsync(
            int sessionId, string? changedByUserId = null, string role = "System", CancellationToken ct = default);

        // The open session that belongs to a reservation, or null.
        Task<DiningSession?> GetOpenSessionForReservationAsync(int reservationId, CancellationToken ct = default);
    }

    public record OpenSessionTableDto(int SessionId, int TableId, string TableNumber, DateTime OpenedAt, bool HasCustomer);

    // Code: ok, claimed, taken, closed, not_found
    public record SessionClaimResult(bool Success, string Code, string Message);

    // Code: opened, already_seated, not_found, status_changed, no_table, wrong_day, too_early, table_busy
    public record SessionOpenResult(bool Success, string Code, string Message, int? SessionId = null,
        int AttachedOrders = 0, string? TableNumber = null);

    // Code: closed, already_closed, not_found
    public record SessionCloseResult(bool Success, string Code, string Message, int? ReservationId = null,
        string? TableNumber = null);
}
