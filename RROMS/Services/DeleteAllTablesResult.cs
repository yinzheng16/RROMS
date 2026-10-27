namespace RROMS.Services;

// Result of "Delete all tables" for one table type.
public record DeleteAllTablesResult(int Deleted, int Kept, string? Error)
{
    public bool Success => Error is null;
}
