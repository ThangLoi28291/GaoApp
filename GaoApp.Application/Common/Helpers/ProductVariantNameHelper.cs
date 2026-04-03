using System.Globalization;
using System.Text;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Common.Helpers;

public static class ProductVariantNameHelper
{
    /// <summary>
    /// Build tên variant dạng:
    /// {Tên sản phẩm} size l vị cam hương bưởi
    /// </summary>
    public static string Build(Product product, IEnumerable<ProductVariantAttributeValue> attributeMappings)
    {
        var productName = product?.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(productName))
            return string.Empty;

        var items = (attributeMappings ?? Enumerable.Empty<ProductVariantAttributeValue>())
            .Where(x => !x.IsDeleted && x.AttributeValue != null)
            .Select(x => new VariantNamePart
            {
                AttributeId = x.AttributeValue!.AttributeId,
                AttributeName = x.AttributeValue.Attribute?.Name?.Trim() ?? string.Empty,
                ValueName = x.AttributeValue.Name?.Trim() ?? string.Empty,
                SortOrder = x.AttributeValue.Attribute?.SortOrder ?? 0
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.AttributeName) && !string.IsNullOrWhiteSpace(x.ValueName))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.AttributeId)
            .ToList();

        if (!items.Any())
            return productName;

        var suffix = string.Join(" ", items.Select(x => $"{NormalizeLabel(x.AttributeName)} {x.ValueName}".Trim()));
        return $"{productName} {suffix}".Trim();
    }

    /// <summary>
    /// Build từ danh sách part trung gian.
    /// Dùng khi chưa attach xong entity mapping vào variant.
    /// </summary>
    public static string Build(string productName, IEnumerable<VariantNamePart> parts)
    {
        productName = (productName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(productName))
            return string.Empty;

        var items = (parts ?? Enumerable.Empty<VariantNamePart>())
            .Where(x => !string.IsNullOrWhiteSpace(x.AttributeName) && !string.IsNullOrWhiteSpace(x.ValueName))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.AttributeId)
            .ToList();

        if (!items.Any())
            return productName;

        var suffix = string.Join(" ", items.Select(x => $"{NormalizeLabel(x.AttributeName)} {x.ValueName}".Trim()));
        return $"{productName} {suffix}".Trim();
    }

    public static string NormalizeForSearch(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        input = input.Trim().ToLowerInvariant();

        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var ch in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace('đ', 'd')
            .Replace('Đ', 'D');
    }

    private static string NormalizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return string.Empty;

        return label.Trim().ToLowerInvariant();
    }
}

public sealed class VariantNamePart
{
    public int AttributeId { get; set; }
    public string AttributeName { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}