using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // US05 / US07 (Owner: Member 1): one row each time an Admin changes a user account
    // (created, role changed, deactivated, reactivated, password link sent).
    // Never edited, never deleted. Names are copied in, so the log still reads well after a rename.
    public class UserAdminLog
    {
        public int Id { get; set; }

        // The account that was changed (users are never deleted, only deactivated).
        [Required]
        public string TargetUserId { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string TargetName { get; set; } = string.Empty;

        public UserAdminAction Action { get; set; }

        // e.g. "Customer → Staff", "as Staff", "set-password link"
        [StringLength(200)]
        public string? Detail { get; set; }

        // The Admin who did it.
        public string? DoneByUserId { get; set; }

        [Required, StringLength(100)]
        public string DoneByName { get; set; } = string.Empty;

        public DateTime DoneAt { get; set; }
    }
}
