using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using RROMS.Models;

namespace RROMS.TagHelpers;

[HtmlTargetElement("body")]
public sealed class CustomerCartUxTagHelper(IFileVersionProvider versions) : TagHelper
{
    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var http = ViewContext.HttpContext;
        if (!http.User.IsInRole(Roles.Customer)) return;
        var pathBase = http.Request.PathBase;
        var root = pathBase.Value ?? "";
        var css = new TagBuilder("link");
        css.TagRenderMode = TagRenderMode.SelfClosing;
        css.Attributes["rel"] = "stylesheet";
        css.Attributes["href"] = versions.AddFileVersionToPath(pathBase, root + "/css/customer-cart-ux.css");
        var script = new TagBuilder("script");
        script.Attributes["src"] = versions.AddFileVersionToPath(pathBase, root + "/js/customer-cart-ux.js");
        script.Attributes["defer"] = "defer";
        script.Attributes["data-cart-ux"] = "";
        output.PostContent.AppendHtml(css);
        output.PostContent.AppendHtml(script);
    }
}
