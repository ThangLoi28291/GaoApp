using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Purchases;

/// <summary>Bounded JPEG evidence. Never accepts SVG, HTML, URLs or arbitrary uploaded files.</summary>
public static class ReceiptIntakePhoto
{
    public const int MaxBytes = 256 * 1024;
    public static byte[]? Parse(string? dataUrl)
    {
        if (string.IsNullOrEmpty(dataUrl)) return null;
        const string prefix = "data:image/jpeg;base64,";
        if (!dataUrl.StartsWith(prefix, StringComparison.Ordinal) || dataUrl.Length > MaxBytes * 4 / 3 + 30)
            throw Invalid();
        byte[] data;
        try { data = Convert.FromBase64String(dataUrl[prefix.Length..]); }
        catch (FormatException) { throw Invalid(); }
        if (data.Length is < 20 or > MaxBytes || data[0] != 0xff || data[1] != 0xd8 || data[^2] != 0xff || data[^1] != 0xd9)
            throw Invalid();
        // Inspect segment boundaries and dimensions before persisting the raster.
        var hasFrame = false;
        for (var p = 2; p + 3 < data.Length;)
        {
            if (data[p++] != 0xff) throw Invalid();
            while (p < data.Length && data[p] == 0xff) p++;
            if (p + 2 >= data.Length) throw Invalid();
            var marker = data[p++];
            if (marker == 0xda) return hasFrame ? data : throw Invalid();
            var length = (data[p] << 8) | data[p + 1];
            if (length < 2 || p + length > data.Length) throw Invalid();
            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (length < 8) throw Invalid();
                var height = (data[p + 3] << 8) | data[p + 4];
                var width = (data[p + 5] << 8) | data[p + 6];
                if (width is < 1 or > 1600 || height is < 1 or > 1600) throw Invalid();
                hasFrame = true;
            }
            p += length;
        }
        throw Invalid();
    }
    private static BusinessRuleException Invalid() => new("Ảnh bao bì phải là JPEG, tối đa 256 KB và 1600 × 1600 pixel. Vui lòng chọn lại ảnh.");
}
