using Microsoft.AspNetCore.Http;

namespace GaoApp.Web.Security;

/// <summary>
/// Kiểm tra đồng thời dung lượng, extension, MIME và chữ ký đầu file.
/// Không cho SVG/HTML/script được lưu dưới webroot.
/// </summary>
public static class UploadSecurityValidator
{
    public const long ProductImageMaxBytes = 10L * 1024 * 1024;
    public const long DisplayMediaMaxBytes = 50L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> AllowedTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".mp4"] = "video/mp4",
            [".webm"] = "video/webm"
        };

    public static Task<string?> ValidateProductImageAsync(
        IFormFile file,
        CancellationToken ct)
        => ValidateAsync(file, ProductImageMaxBytes, allowVideo: false, ct);

    public static Task<string?> ValidateDisplayMediaAsync(
        IFormFile file,
        CancellationToken ct)
        => ValidateAsync(file, DisplayMediaMaxBytes, allowVideo: true, ct);

    private static async Task<string?> ValidateAsync(
        IFormFile file,
        long maxBytes,
        bool allowVideo,
        CancellationToken ct)
    {
        if (file.Length <= 0)
            return "File rỗng.";

        if (file.Length > maxBytes)
            return $"File vượt quá giới hạn {maxBytes / 1024 / 1024} MB.";

        var extension = Path.GetExtension(Path.GetFileName(file.FileName));
        if (string.IsNullOrWhiteSpace(extension) ||
            !AllowedTypes.TryGetValue(extension, out var expectedContentType) ||
            (!allowVideo && expectedContentType.StartsWith("video/", StringComparison.Ordinal)))
        {
            return allowVideo
                ? "Chỉ chấp nhận JPG, PNG, GIF, WEBP, MP4 hoặc WEBM."
                : "Chỉ chấp nhận JPG, PNG, GIF hoặc WEBP.";
        }

        var actualContentType = file.ContentType ?? string.Empty;
        var parameterSeparator = actualContentType.IndexOf(';');
        if (parameterSeparator >= 0)
        {
            actualContentType = actualContentType[..parameterSeparator];
        }

        actualContentType = actualContentType.Trim();

        if (!string.Equals(actualContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
            return "Content-Type không khớp với loại file.";

        var header = new byte[16];
        await using var input = file.OpenReadStream();
        var bytesRead = await input.ReadAsync(header.AsMemory(0, header.Length), ct);

        if (!HasExpectedSignature(extension, header, bytesRead))
            return "Nội dung file không khớp với phần mở rộng.";

        return null;
    }

    private static bool HasExpectedSignature(string extension, byte[] bytes, int length)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" =>
                length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,

            ".png" =>
                length >= 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A,

            ".gif" =>
                length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 &&
                bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61,

            ".webp" =>
                length >= 12 && IsAscii(bytes, 0, "RIFF") && IsAscii(bytes, 8, "WEBP"),

            ".mp4" =>
                length >= 12 && IsAscii(bytes, 4, "ftyp"),

            ".webm" =>
                length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 &&
                bytes[2] == 0xDF && bytes[3] == 0xA3,

            _ => false
        };
    }

    private static bool IsAscii(byte[] bytes, int offset, string expected)
    {
        if (bytes.Length < offset + expected.Length)
            return false;

        for (var i = 0; i < expected.Length; i++)
        {
            if (bytes[offset + i] != expected[i])
                return false;
        }

        return true;
    }
}
