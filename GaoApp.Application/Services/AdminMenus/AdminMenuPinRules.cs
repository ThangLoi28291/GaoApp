using System.Text.Json;
using GaoApp.Application.DTOs.AdminMenus;

namespace GaoApp.Application.Services.AdminMenus;

// Input must be the existing permission/visibility-filtered tree, never the administrative tree.
public static class AdminMenuPinRules
{
    public const int Maximum = 4;

    public static IReadOnlyList<int> Parse(string? json)
    {
        try { return (JsonSerializer.Deserialize<int[]>(json ?? "[]") ?? []).Where(x => x > 0).Distinct().Take(Maximum).ToArray(); }
        catch (JsonException) { return []; }
    }

    public static IReadOnlyList<AdminMenuPinChoice> Choices(IEnumerable<AdminMenuItemDto> menus, bool administrator)
    {
        var result = new List<AdminMenuPinChoice>();
        var visited = new HashSet<int>();
        void Visit(IEnumerable<AdminMenuItemDto> items, string parent)
        {
            foreach (var item in items.OrderBy(x => x.SortOrder))
            {
                if (!item.IsActive || !visited.Add(item.Id)) continue;
                if (!administrator && (string.Equals(item.Controller, "ReceiptTemplates", StringComparison.OrdinalIgnoreCase)
                    || (item.Url?.StartsWith("/admin/receipt-templates", StringComparison.OrdinalIgnoreCase) ?? false))) continue;
                var path = string.IsNullOrEmpty(parent) ? item.Title : parent + " / " + item.Title;
                var before = result.Count;
                Visit(item.Children, path);
                if (result.Count == before && (!string.IsNullOrWhiteSpace(item.Url) || !string.IsNullOrWhiteSpace(item.Controller)))
                    result.Add(new(item.Id, item.Title, string.IsNullOrWhiteSpace(item.Icon) ? "bx bx-circle" : item.Icon, path));
            }
        }
        Visit(menus, "");
        return result;
    }

    public static void Validate(IReadOnlyList<int>? ids, IEnumerable<int> eligibleIds)
    {
        if (ids == null || ids.Count > Maximum || ids.Any(x => x <= 0) || ids.Distinct().Count() != ids.Count)
            throw new ArgumentException("Chọn tối đa 4 mục khác nhau từ menu hiện có.");
        var eligible = eligibleIds.ToHashSet();
        if (ids.Any(x => !eligible.Contains(x)))
            throw new ArgumentException("Có mục không còn khả dụng trong menu của bạn. Hãy bỏ mục đó hoặc tải lại.");
    }
}
