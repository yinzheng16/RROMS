namespace RROMS.Models
{
    // Owner: Member 5 (US43). View models for the admin feedback page; not stored in the database.

    // Numbers shown in the Summary section of /Admin/Feedback.
    // CountByRating[1]..CountByRating[5] = number of reviews with that rating (index 0 is unused).
    public record FeedbackSummary(int Total, double Average, int[] CountByRating, int NeedsReply)
    {
        public static FeedbackSummary Empty => new(0, 0, new int[6], 0);
    }

    // Filters the admin can apply to the feedback list.
    // Status: null = all, "needs" = not replied yet, "replied" = already replied.
    // Sort: null/"newest" = newest first, "lowest" = lowest rating first.
    public record FeedbackFilter(int? Rating, string? Status, string? Search, string? Sort);
}
