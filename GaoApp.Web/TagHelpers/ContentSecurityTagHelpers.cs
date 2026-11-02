using GaoApp.Web.Security;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace GaoApp.Web.TagHelpers;

// Applies only to script elements compiled from trusted Razor source. Do not
// rewrite response HTML: that would give injected scripts a trusted nonce too.
[HtmlTargetElement("script")]
public sealed class ScriptNonceTagHelper : TagHelper
{
    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
        => output.Attributes.SetAttribute("nonce", ContentSecurityPolicy.Nonce(ViewContext.HttpContext));
}

[HtmlTargetElement("head")]
public sealed class ContentSecurityHeadTagHelper(Microsoft.AspNetCore.Mvc.ViewFeatures.IFileVersionProvider versions) : TagHelper
{
    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var http = ViewContext.HttpContext;
        var script = new TagBuilder("script");
        script.Attributes["src"] = versions.AddFileVersionToPath(http.Request.PathBase, http.Request.PathBase + "/Admin/js/csp-actions.js");
        script.Attributes["nonce"] = ContentSecurityPolicy.Nonce(http);
        output.PostContent.AppendHtml(script);
    }
}
