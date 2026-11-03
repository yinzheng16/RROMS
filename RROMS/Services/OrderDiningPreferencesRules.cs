namespace RROMS.Services;

// Owner: Member 3. Preserve the complete preference text within the existing
// order-remarks limit; never silently truncate allergy information or notes.
public static class OrderDiningPreferencesRules
{
    public const int RemarksMaxLength = 500;

    public static string? RemarksDefault(string? summary)
    {
        string? text = OrderPricingRules.NormalizeNote(summary);
        if (text is null || text.Length <= RemarksMaxLength) return text;

        // Profile summaries add labels to the stored text. Remove only optional
        // spaces between known chip names, keeping the customer's notes verbatim.
        int notesStart = text.StartsWith("Notes: ", StringComparison.Ordinal)
            ? 0 : text.IndexOf(". Notes: ", StringComparison.Ordinal);
        if (notesStart > 0) notesStart += 2;
        string header = notesStart >= 0 ? text[..notesStart] : text;
        string notes = notesStart >= 0 ? text[notesStart..] : "";
        string compact = header.Replace(", ", ",", StringComparison.Ordinal) + notes;
        if (compact.Length <= RemarksMaxLength) return compact;

        // Older profiles can contain a full 500-character free-text note.
        // The redundant Notes label is unnecessary inside an order-remarks box.
        if (compact.StartsWith("Notes: ", StringComparison.Ordinal) &&
            compact.Length - "Notes: ".Length <= RemarksMaxLength)
            return compact["Notes: ".Length..];

        // The view will show the complete summary separately, with no clipping.
        return null;
    }

    public static bool IsProfileCopy(string? remarks, string? summary)
    {
        string? note = OrderPricingRules.NormalizeNote(remarks);
        string? profile = OrderPricingRules.NormalizeNote(summary);
        return note is not null && profile is not null &&
            (string.Equals(note, profile, StringComparison.Ordinal) ||
             string.Equals(note, RemarksDefault(profile), StringComparison.Ordinal));
    }
}
