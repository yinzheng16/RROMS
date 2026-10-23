// Owner: Member 2 (Tables & Reservations)
namespace RROMS.Models;

public static class TableTypeInfo
{
    public static string Label(TableType t) => t switch
    {
        TableType.Counter => "Counter seat",
        TableType.Double => "Table for two",
        TableType.Standard => "Table (3-6 guests)",
        TableType.Vip => "VIP room",
        TableType.Vvip => "VVIP room",
        _ => t.ToString()
    };

    public static string Japanese(TableType t) => t switch
    {
        TableType.Counter => "カウンター席",
        TableType.Double => "二名席",
        TableType.Standard => "テーブル席",
        TableType.Vip => "個室",
        TableType.Vvip => "特別個室",
        _ => ""
    };

    public static string Rm(decimal amount) => $"RM {amount:N0}";
}
