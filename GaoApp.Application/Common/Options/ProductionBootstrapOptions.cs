using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.Common.Options;

/// <summary>
/// Cấu hình khởi tạo một lần cho database production hoàn toàn trống.
/// Giá trị nhạy cảm phải được cấp qua secret store hoặc environment variable,
/// không lưu trong appsettings của repository.
/// </summary>
public sealed class ProductionBootstrapOptions
{
    public const string SectionName = "ProductionBootstrap";

    private static readonly Regex CodePattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex SubdomainPattern = new(
        "^[a-z0-9](?:[a-z0-9-]{0,58}[a-z0-9])?$",
        RegexOptions.CultureInvariant);

    public bool Enabled { get; set; }

    public string? StoreName { get; set; }
    public string? StoreSubdomain { get; set; }

    public string? LegalEntityCode { get; set; }
    public string? LegalEntityName { get; set; }
    public string? LegalEntityLegalName { get; set; }
    public string? LegalEntityTaxCode { get; set; }

    public string? WarehouseCode { get; set; }
    public string? WarehouseName { get; set; }

    public string? TerminalCode { get; set; }
    public string? TerminalName { get; set; }

    public string? AdminUserName { get; set; }
    public string? AdminFullName { get; set; }
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }

    /// <summary>
    /// Rule duy nhất cho cấu hình bootstrap. Khi bootstrap bị tắt, các giá trị
    /// còn lại không bắt buộc và không được sử dụng.
    /// </summary>
    public bool HasValidConfiguration()
    {
        if (!Enabled)
        {
            return true;
        }

        var normalizedSubdomain = StoreSubdomain?.Trim().ToLowerInvariant();

        return HasLength(StoreName, 1, 200)
            && HasLength(normalizedSubdomain, 1, 60)
            && SubdomainPattern.IsMatch(normalizedSubdomain!)
            && !string.Equals(
                normalizedSubdomain,
                "admin",
                StringComparison.OrdinalIgnoreCase)
            && HasCode(LegalEntityCode, 50)
            && HasLength(LegalEntityName, 1, 200)
            && HasLength(LegalEntityLegalName, 1, 300)
            && HasOptionalLength(LegalEntityTaxCode, 50)
            && HasCode(WarehouseCode, 50)
            && HasLength(WarehouseName, 1, 200)
            && HasCode(TerminalCode, 30)
            && HasLength(TerminalName, 1, 150)
            && HasCode(AdminUserName, 100)
            && HasLength(AdminFullName, 1, 200)
            && HasOptionalEmail(AdminEmail)
            && HasStrongPassword(AdminPassword);
    }

    private static bool HasCode(string? value, int maximumLength)
    {
        var normalized = value?.Trim();

        return HasLength(normalized, 1, maximumLength)
            && CodePattern.IsMatch(normalized!);
    }

    private static bool HasLength(
        string? value,
        int minimumLength,
        int maximumLength)
    {
        var length = value?.Trim().Length ?? 0;
        return length >= minimumLength && length <= maximumLength;
    }

    private static bool HasOptionalLength(string? value, int maximumLength)
        => string.IsNullOrWhiteSpace(value)
            || value.Trim().Length <= maximumLength;

    private static bool HasOptionalEmail(string? value)
        => string.IsNullOrWhiteSpace(value)
            || value.Trim().Length <= 200
            && new EmailAddressAttribute().IsValid(value.Trim());

    private static bool HasStrongPassword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length is < 16 or > 256
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        return value.Any(char.IsUpper)
            && value.Any(char.IsLower)
            && value.Any(char.IsDigit)
            && value.Any(ch => !char.IsLetterOrDigit(ch));
    }
}
