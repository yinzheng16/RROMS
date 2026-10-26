using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.TagHelpers;

// Owner: Member 3. Enhances the EXISTING customer cart anchor. Shared layout and
// login partial remain owned by Member 1 and do not need to be replaced.
[HtmlTargetElement("a", Attributes = "href")]
public sealed class CartNavigationTagHelper(ICustomerCartService carts) : TagHelper
{
    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;
    public override int Order => 1000;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var http = ViewContext.HttpContext;
        if (!http.User.IsInRole(Roles.Customer)) return;
        string href = output.Attributes["href"]?.Value?.ToString() ?? "";
        if (!string.Equals(href, http.Request.PathBase + "/Customer/Cart", StringComparison.OrdinalIgnoreCase)) return;
        string? userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return;
        int count = await carts.CountAsync(userId, http.RequestAborted);
        output.Attributes.SetAttribute("data-cart-navigation", "");
        output.Attributes.SetAttribute("aria-label", $"My cart, {count} items");
        var badge = new TagBuilder("span");
        badge.AddCssClass("nav-badge");
        badge.Attributes["data-cart-count"] = "";
        badge.Attributes["aria-hidden"] = "true";
        if (count == 0) badge.Attributes["hidden"] = "hidden";
        badge.InnerHtml.Append(count > 99 ? "99+" : count.ToString());
        output.PostContent.AppendHtml(badge);
    }
}
