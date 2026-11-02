using RROMS.Models;
namespace RROMS.Services;

// Shared by the service and UI. The server always validates independently of the button shown.
public static class OrderStatusRules
{
    // One icon set for every order-status display (staff board, staff drawer, customer My Orders,
    // order confirmation). Fixed markup only, so it is safe to render with Html.Raw.
    private const string Petal = "M24 24C4 17 14-1 24 8C34-1 44 17 24 24Z";
    public static string IconShapes(OrderStatus status) => status switch {
        OrderStatus.Pending => "<path d=\"M13 7h24v34H13ZM19 16h12M19 23h12M19 30h8\" />",
        OrderStatus.Preparing => "<path d=\"M14 20c-5 0-8-3-8-7s3-7 7-7c2 0 4 1 5 2a7 7 0 0 1 12 0c1-1 3-2 5-2 4 0 7 3 7 7s-3 7-8 7v18H14V20ZM14 32h20\" />",
        OrderStatus.ReadyToServe => "<path d=\"M7 35h34M10 30a14 14 0 0 1 28 0H10ZM22 13a2 2 0 1 1 4 0\" />",
        OrderStatus.Served => "<path d=\"M13 27h28M16 23a11 11 0 0 1 22 0H16ZM25 10a2 2 0 1 1 4 0M5 34l7 7 7-4 12-2 9-8M13 31l6-3 8 1c3 .5 3 4 0 4h-7M5 34l7-7 6 2\" />",
        OrderStatus.Completed => string.Concat(new[] { 0, 72, 144, 216, 288 }.Select(a => $"<path d=\"{Petal}\" transform=\"rotate({a} 24 24)\" />")) + "<circle cx=\"24\" cy=\"24\" r=\"3\" />",
        OrderStatus.Cancelled => "<path d=\"M15 15l18 18M33 15 15 33\" />",
        _ => "" };
    public static string IconSvg(OrderStatus status, string cssClass, int size, double strokeWidth)
        => $"<svg class=\"{cssClass}\" viewBox=\"0 0 48 48\" width=\"{size}\" height=\"{size}\" fill=\"none\" stroke=\"currentColor\" style=\"stroke-width:{strokeWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">{IconShapes(status)}</svg>";

    public static readonly IReadOnlyList<OrderStatus> Stages = Array.AsReadOnly(new[] {
        OrderStatus.Pending, OrderStatus.Preparing, OrderStatus.ReadyToServe,
        OrderStatus.Served, OrderStatus.Completed, OrderStatus.Cancelled });
    public static OrderStatus? Next(OrderStatus status) => status switch {
        OrderStatus.Pending => OrderStatus.Preparing,
        OrderStatus.Preparing => OrderStatus.ReadyToServe,
        OrderStatus.ReadyToServe => OrderStatus.Served,
        OrderStatus.Served => OrderStatus.Completed, _ => null };
    public static bool CanCancel(OrderStatus status) => status is OrderStatus.Pending or OrderStatus.Preparing;
    public static bool Active(OrderStatus status) => status is OrderStatus.Pending or OrderStatus.Preparing or OrderStatus.ReadyToServe;
    public static bool CanMove(OrderStatus from, OrderStatus to)
        => Next(from) == to || to == OrderStatus.Cancelled && CanCancel(from);
    public static string? Validate(OrderStatus from, OrderStatus to, string? reason)
    {
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to) || !CanMove(from, to))
            return "That status change is not allowed. Refresh the order to see its next step.";
        if ((reason?.Trim().Length ?? 0) > 250) return "Keep the reason to 250 characters or fewer.";
        if (to == OrderStatus.Cancelled && from == OrderStatus.Preparing && string.IsNullOrWhiteSpace(reason))
            return "Enter a reason to cancel an order that is already being prepared.";
        return null;
    }
    public static string Label(OrderStatus status) => status == OrderStatus.ReadyToServe ? "Ready to Serve" : status.ToString();
    public static string Action(OrderStatus status) => status switch {
        OrderStatus.Pending => "Accept order", OrderStatus.Preparing => "Mark ready",
        OrderStatus.ReadyToServe => "Mark served", OrderStatus.Served => "Complete order", _ => "" };
    public static OrderStatus View(string? value) => Enum.TryParse<OrderStatus>(value, true, out var stage)
        && Enum.IsDefined(stage) ? stage : OrderStatus.Pending;
}
