namespace RROMS.Models
{
    // US07 (Owner: Member 1): what an Admin did to a user account. Saved as text (e.g. "Deactivated").
    public enum UserAdminAction
    {
        Created,
        RoleChanged,
        Deactivated,
        Reactivated,
        PasswordLinkSent,
        Removed
    }
}
