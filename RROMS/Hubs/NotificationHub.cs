using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace RROMS.Hubs;

// Owner: Member 4 (US37). Real-time push for the customer's notification bell.
// Built on the shared SignalR setup (builder.Services.AddSignalR() in Program.cs, Member 3 / US27).
// NotificationService sends a short "new notification" signal to one customer with Clients.User(userId);
// no groups are needed. The bell then reads the details from /Customer/Notifications?handler=Summary,
// which only returns the signed-in customer's own notifications, so nothing private travels in the signal.
[Authorize(Policy = "CustomerOnly")]
public sealed class NotificationHub : Hub
{
    public const string Path = "/hubs/notifications";
    public const string ReceivedEvent = "NotificationReceived";
}
