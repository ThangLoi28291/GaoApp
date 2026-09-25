using System.Text;

namespace GaoApp.Application.Common.Helpers;

public static class InputInvoiceItemIdentityNormalizer
{
    public static string? NormalizeCode(string? value)
        => Normalize(value);

    public static string? NormalizeText(string? value)
        => Normalize(value);

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var result = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character) || character == '\u00A0')
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }

            result.Append(char.ToUpperInvariant(character));
        }

        return result.Length == 0 ? null : result.ToString();
    }
}
