using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 3. Retained when a category or dish is deleted.
    // This ledger prevents issuing an old menu code to a different record.
    public class MenuCodeSequence
    {
        [Key, StringLength(12)]
        public string Prefix { get; set; } = string.Empty;

        public int LastItemNumber { get; set; }
    }
}
