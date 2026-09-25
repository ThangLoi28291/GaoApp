using System.Globalization;

namespace GaoApp.Application.Services.Inventory;

public static class InputInvoiceVatRateNormalizer
{
    public static bool TryNormalize(string? raw, out decimal rate, out string? reason)
    {
        rate = 0m;
        if (string.IsNullOrWhiteSpace(raw))
        {
            reason = "XML không cung cấp thuế suất số có thể đối chiếu.";
            return false;
        }
        var text = raw.Trim().TrimEnd('%').Trim().Replace(',', '.');
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture,
                out rate) || rate < 0m || rate > 100m)
        {
            reason = "Thuế suất XML là nhóm đặc biệt hoặc không phải giá trị số hỗ trợ.";
            return false;
        }
        reason = null;
        return true;
    }
}
