using System.Globalization;

namespace GaoApp.Web.Areas.Admin.Models.Purchases;

/// <summary>
/// Quantity formatting is intentionally different from money formatting.
/// It keeps at most three meaningful decimals and never renders integer
/// quantities as 3,000 (which is ambiguous to Vietnamese users).
/// </summary>
public static class PurchaseQuantityFormatter
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>Read-only Vietnamese display: 3; 2,5; 1.250,5.</summary>
    public static string Display(decimal value)
        => value.ToString("#,0.###", Vietnamese);

    /// <summary>Editable text without group separators: 3000; 2,5.</summary>
    public static string Input(decimal value)
        => value.ToString("0.###", Vietnamese);

    /// <summary>Canonical value for hidden fields, data attributes and number inputs.</summary>
    public static string Invariant(decimal value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);
}
