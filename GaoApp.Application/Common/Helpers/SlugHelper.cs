using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GaoApp.Application.Common.Helpers;

public static class SlugHelper
{
    public static string Slugify(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var s = input.Trim().ToLowerInvariant().Replace("đ", "d");
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);

        s = sb.ToString().Normalize(NormalizationForm.FormC);
        s = Regex.Replace(s, @"[^a-z0-9]+", "-").Trim('-');
        s = Regex.Replace(s, @"-+", "-");
        return s;
    }
}
