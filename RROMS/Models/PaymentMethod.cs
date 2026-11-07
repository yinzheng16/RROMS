namespace RROMS.Models
{
    // Owner: Member 4 (Payments, Billing & Notifications)
    // Saved in the database as the WORD (e.g. "Paid"), not a number.
    // Never rename or delete an existing value (old records keep the old word).
    // Adding a new value anywhere in the list is safe.
    public enum PaymentMethod
    {
        Cash,
        Card,
        Online
    }
}
