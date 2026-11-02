using RROMS.Models;

namespace RROMS.Data
{
    // US10 (Member 1): starter FAQs for the chatbot, added once when the table is empty.
    // Admins change them on Admin → Restaurant → Chatbot FAQs. Hours, prices and dishes are NOT written here:
    // the chatbot always reads those live from the opening hours and the menu.
    public static class ChatFaqSeed
    {
        public static void Seed(ApplicationDbContext db)
        {
            if (db.ChatFaqs.Any()) return;

            db.ChatFaqs.AddRange(
                new ChatFaq
                {
                    Question = "Do I need a reservation to dine in?",
                    Answer = "Yes, dine-in is by reservation. Book a table online on our booking page (log in first). You can also order takeaway.",
                    Keywords = "walk in, walk-in, reservation, without booking, no booking"
                },
                new ChatFaq
                {
                    Question = "Is the food halal?",
                    Answer = "YORU is not halal-certified: our kitchen uses pork and we serve alcohol. Seafood and vegetable dishes are marked on the menu.",
                    Keywords = "halal, pork, muslim, alcohol"
                },
                new ChatFaq
                {
                    Question = "Do you have vegetarian options?",
                    Answer = "Yes, we have salads, vegetable skewers and tofu dishes. Please tell our staff about any allergies when you order.",
                    Keywords = "vegetarian, vegan, no meat, allergy, allergies"
                },
                new ChatFaq
                {
                    Question = "Which payment methods do you accept?",
                    Answer = "Cash, card, or online payment.",
                    Keywords = "pay, payment, card, cash, online, credit, debit"
                },
                new ChatFaq
                {
                    Question = "Can I celebrate a birthday at YORU?",
                    Answer = "Of course! Add it to the special request box when you book, and our team will do their best to make it special.",
                    Keywords = "birthday, celebrate, celebration, anniversary, cake"
                },
                new ChatFaq
                {
                    Question = "Can I cancel or change my booking?",
                    Answer = "Log in and open My reservations to see or cancel an upcoming booking.",
                    Keywords = "cancel, change booking, modify, reschedule, my reservation"
                },
                new ChatFaq
                {
                    Question = "Is there a dress code?",
                    Answer = "No strict dress code: smart casual is perfect.",
                    Keywords = "dress code, dress, attire, wear"
                },
                new ChatFaq
                {
                    Question = "Where can I park?",
                    Answer = "There is a public car park a short walk from the restaurant. Street parking nearby is limited in the evening.",
                    Keywords = "parking, park, car park, car"
                });

            db.SaveChanges();
        }
    }
}
