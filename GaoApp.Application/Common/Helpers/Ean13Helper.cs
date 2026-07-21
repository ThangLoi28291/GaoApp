using System;
using System.Linq;

namespace GaoApp.Application.Common.Helpers;

public static class Ean13Helper
{
    /// <summary>
    /// Sinh barcode nội bộ dạng EAN-13.
    /// Mặc định prefix = "20" (bạn có thể đổi "21", "29"... nếu muốn).
    /// Format 12 số: prefix(2) + store(3) + variant(7) = 12, rồi + check digit = 13.
    /// </summary>
    public static string GenerateInternal(int storeId, int variantId, string prefix2 = "20")
    {
        if (string.IsNullOrWhiteSpace(prefix2) || prefix2.Length != 2 || prefix2.Any(c => c < '0' || c > '9'))
            throw new ArgumentException("prefix2 must be exactly 2 digits.", nameof(prefix2));

        if (storeId < 0 || storeId > 999) throw new ArgumentOutOfRangeException(nameof(storeId));
        if (variantId < 0 || variantId > 9_999_999) throw new ArgumentOutOfRangeException(nameof(variantId));

        var base12 = $"{prefix2}{storeId:000}{variantId:0000000}";
        var check = ComputeCheckDigit(base12);
        return base12 + check.ToString();
    }

    // base12 must be exactly 12 digits
    public static int ComputeCheckDigit(string base12)
    {
        if (string.IsNullOrWhiteSpace(base12) || base12.Length != 12 || base12.Any(c => c < '0' || c > '9'))
            throw new ArgumentException("EAN base must be 12 digits.", nameof(base12));

        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            int digit = base12[i] - '0';
            sum += ((i + 1) % 2 == 0) ? digit * 3 : digit;
        }

        int mod = sum % 10;
        return (10 - mod) % 10;
    }
}
