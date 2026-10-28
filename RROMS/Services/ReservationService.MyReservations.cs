// Owner: Member 2 — US13. Add alongside your existing partial ReservationService.
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

public partial class ReservationService : IMyReservationsService
{
    public async Task<IReadOnlyList<MyReservationItem>> GetMyUpcomingReservationsAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Array.Empty<MyReservationItem>();

        // Same local restaurant-time convention as the existing US12 service.
        // Include arrivals within the existing grace period, but do not allow cancellation
        // once ReservedAt is reached. Expired/terminal reservations are not a history tab.
        var now = DateTime.Now;
        var cutoff = now.AddMinutes(-BookingRules.NoShowGraceMinutes);
        var rows = await _db.Reservations
            .AsNoTracking()
            .Include(r => r.Table)
                .ThenInclude(t => t!.TableTypeDefinition)
            .Where(r => r.UserId == userId &&
                (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed) &&
                r.ReservedAt >= cutoff)
            .OrderBy(r => r.ReservedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        // Area names are read once (the table only stores the area id).
        var areaIds = rows.Where(r => r.Table != null).Select(r => r.Table!.DiningAreaId).Distinct().ToList();
        var areaNames = await _db.DiningAreas.AsNoTracking()
            .Where(a => areaIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        return rows.Select(r => new MyReservationItem(
            r.Id,
            r.ReferenceNo,
            r.ReservedAt,
            r.Guests,
            r.Table?.TableTypeDefinition?.Name ?? "Table",
            r.Table?.TableTypeDefinition?.JapaneseName,
            r.SpecialRequest,
            r.MinimumSpendAccepted ?? 0m,
            r.MinimumSpendAcceptedAt.HasValue,
            r.Status.ToString(),
            r.ReservedAt > now,
            r.Table != null && areaNames.TryGetValue(r.Table.DiningAreaId, out var area) ? area : null,
            // The customer only sees the table number once staff have confirmed the booking.
            r.Status == ReservationStatus.Confirmed ? r.Table?.TableNumber : null)).ToArray();
    }

    public async Task<MyReservationCancelResult> CancelMyReservationAsync(
        string userId, int reservationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || reservationId <= 0)
            return new(false, "not_found", "This reservation could not be found.");

        var now = DateTime.Now;

        // Which status are we cancelling from? (needed for the history row)
        var from = await _db.Reservations.AsNoTracking()
            .Where(r => r.Id == reservationId && r.UserId == userId && r.ReservedAt > now &&
                (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
            .Select(r => (ReservationStatus?)r.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (from is not null)
        {
            // The status change and its history row are saved together, or not at all.
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            // One conditional SQL UPDATE: ownership, time and the status we just read are checked
            // together. A concurrent staff update to Seated/Completed cannot be overwritten.
            // Requires EF Core 7+ (SQL Server), and deliberately does not reset Table.Status:
            // the existing US12 availability query already excludes Cancelled reservations.
            var changed = await _db.Reservations
                .Where(r => r.Id == reservationId && r.UserId == userId &&
                    r.ReservedAt > now && r.Status == from.Value)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, ReservationStatus.Cancelled), cancellationToken);

            if (changed == 1)
            {
                _db.ReservationStatusLogs.Add(new ReservationStatusLog
                {
                    ReservationId = reservationId,
                    FromStatus = from.Value,
                    ToStatus = ReservationStatus.Cancelled,
                    ChangedByUserId = userId,
                    ChangedByRole = "Customer",
                    ChangedAt = now,
                    Note = "Cancelled by customer"
                });
                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return new(true, "cancelled", "Your reservation has been cancelled.");
            }
        }

        var current = await _db.Reservations.AsNoTracking()
            .Where(r => r.Id == reservationId && r.UserId == userId)
            .Select(r => new { r.Status, r.ReservedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (current == null)
            return new(false, "not_found", "This reservation could not be found.");
        // A duplicate/retried POST is safe, including when the first response was lost.
        if (current.Status == ReservationStatus.Cancelled)
            return new(true, "already_cancelled", "This reservation is already cancelled.");
        if (current.ReservedAt <= DateTime.Now)
            return new(false, "too_late", "You cannot cancel after the scheduled reservation time.");

        return new(false, "status_changed", "This reservation can no longer be cancelled. Please refresh to see its latest status.");
    }
}