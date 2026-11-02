using System.Text.RegularExpressions;

namespace GaoApp.Tests.Security;

internal static class CspHtmlAssert
{
    internal static void Equivalent(string first, string second)
    {
        const string pattern = "nonce=\"([^\"]+)\"";
        var a = Regex.Matches(first, pattern).Select(x => System.Net.WebUtility.HtmlDecode(x.Groups[1].Value)).Distinct().ToArray();
        var b = Regex.Matches(second, pattern).Select(x => System.Net.WebUtility.HtmlDecode(x.Groups[1].Value)).Distinct().ToArray();
        Assert.Single(a); Assert.Single(b);
        Assert.True(Convert.FromBase64String(a[0]).Length >= 16);
        Assert.True(Convert.FromBase64String(b[0]).Length >= 16);
        Assert.NotEqual(a[0], b[0]);
        Assert.Equal(Regex.Replace(first, pattern, "nonce=\"per-response\""),
            Regex.Replace(second, pattern, "nonce=\"per-response\""));
    }
}
