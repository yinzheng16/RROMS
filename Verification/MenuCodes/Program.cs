using RROMS.Services;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var catalogue = new (string Name, string Code)[] {
    ("Otsumami · Small Bites", "OT"), ("Sarada · Salads", "SA"),
    ("Sashimi · Raw Seafood", "SS"), ("Sushi", "SU"),
    ("Makimono · Sushi Rolls", "MA"), ("Yakitori · Chicken Skewers", "YT"),
    ("Yakimono · Grilled Dishes", "YM"), ("Agemono · Fried Dishes", "AG"),
    ("Tempura", "TE"), ("Donmono · Rice Bowls", "DO"), ("Gohan · Rice Dishes", "GO"),
    ("Menmono · Noodles", "ME"), ("Wanmono · Soups & Steamed Dishes", "WA"),
    ("Kanmi · Desserts", "KA"), ("Arukōru · Alcoholic Drinks", "AL"),
    ("Sofuto Dorinku · Non-Alcoholic Drinks", "SD")
};
var issued = new HashSet<string>();
foreach (var (name, expected) in catalogue.Reverse())
{
    string code = MenuCodeRules.CategoryCode(name, issued);
    Check(code == expected, name + " should receive " + expected);
    Check(issued.Add(code), "Category code collision");
}
Check(MenuCodeRules.CategoryCode("  sashimi · Other English subtitle ", Array.Empty<string>()) == "SS", "Case and subtitle");
Check(MenuCodeRules.CategoryCode("Aruko\u0304ru", Array.Empty<string>()) == "AL", "Decomposed macron");
Check(MenuCodeRules.CategoryCode("Non-Alcoholic Drinks", Array.Empty<string>()) == "SD", "English alias");
Check(MenuCodeRules.CategoryCode("Sakana", Array.Empty<string>()) == "SK", "Reserved SA must remain available for Sarada");
issued.Add("SK");
Check(MenuCodeRules.CategoryCode("Sake", issued) == "SE", "Unknown-name collision");
Check(MenuCodeRules.CategoryCode("Sashimi · Specials", issued) == "SH", "Same known family must not duplicate SS");
Check(MenuCodeRules.CategoryCode("刺身", issued).Length == 2, "Non-Latin fallback");
var allPairs = (from left in Enumerable.Range('A', 26)
                from right in Enumerable.Range('A', 26)
                select string.Concat((char)left, (char)right)).ToArray();
Check(MenuCodeRules.CategoryCode("Specials", allPairs) == "MCA", "Exhausted two-letter namespace");
Check(MenuCodeRules.CategoryCode("Specials", allPairs.Append("MCA")) == "MCB", "Large-namespace collision");

var one = MenuCodeRules.NextItemCode("SS", 0);
var two = MenuCodeRules.NextItemCode("SS", one.Number);
// Deleting the SS02 record does not remove or rewind this stored high-water mark.
var three = MenuCodeRules.NextItemCode("SS", two.Number);
Check(one.Code == "SS01" && two.Code == "SS02" && three.Code == "SS03", "Non-reused numbers");
Check(MenuCodeRules.NextItemCode("SS", 99).Code == "SS100", "Numbers expand after 99");
Check(MenuCodeRules.NextItemCode("SA", 0).Code == "SA01", "Separate category sequence");
try { MenuCodeRules.NextItemCode("SS", int.MaxValue); throw new Exception("Missing overflow check"); }
catch (OverflowException) { }
Console.WriteLine("PASS: all 16 prefixes, accent/case/English recognition, reserved prefixes, unknown collisions, exhausted namespace and monotonic item numbers.");
