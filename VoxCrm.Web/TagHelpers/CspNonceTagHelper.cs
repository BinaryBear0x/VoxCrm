using Microsoft.AspNetCore.Razor.TagHelpers;

namespace VoxCrm.Web.TagHelpers;

[HtmlTargetElement("script")]
[HtmlTargetElement("style")]
public sealed class CspNonceTagHelper(IHttpContextAccessor httpContextAccessor) : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (httpContextAccessor.HttpContext?.Items["CspNonce"] is string nonce)
            output.Attributes.SetAttribute("nonce", nonce);
    }
}
