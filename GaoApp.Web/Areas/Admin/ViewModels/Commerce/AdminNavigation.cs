using GaoApp.Application.DTOs.AdminMenus;
using Microsoft.AspNetCore.WebUtilities;

namespace GaoApp.Web.Areas.Admin.ViewModels.Commerce;

public sealed record AdminNavigationLocation(string Area, string Controller, string Action, string Path, string Query);
public sealed record AdminNavigationNode(int Id, string Title, string Icon, string? Href, bool IsCurrent, IReadOnlyList<AdminNavigationNode> Children)
{
    public bool ContainsCurrent => IsCurrent || Children.Any(child => child.ContainsCurrent);
}

/// <summary>Presentation only. Input is the filtered GetForRenderAsync tree; never adds destinations or mutates configuration.</summary>
public static class AdminNavigation
{
    public static IReadOnlyList<AdminNavigationNode> Build(IEnumerable<AdminMenuItemDto> eligibleMenus,
        Func<AdminMenuItemDto, string?> resolveHref, AdminNavigationLocation location, bool canConfigureReceipts)
    {
        var visited = new HashSet<int>();
        var currentFound = false;
        IReadOnlyList<AdminNavigationNode> Project(IEnumerable<AdminMenuItemDto> menus)
        {
            var result = new List<AdminNavigationNode>();
            foreach (var menu in menus.OrderBy(item => item.SortOrder))
            {
                if (!menu.IsActive || !visited.Add(menu.Id)) continue;
                // Preserve the existing receipt-template administrator rule at every level.
                if (!canConfigureReceipts && (string.Equals(menu.Controller, "ReceiptTemplates", StringComparison.OrdinalIgnoreCase)
                    || (menu.Url?.StartsWith("/admin/receipt-templates", StringComparison.OrdinalIgnoreCase) ?? false))) continue;
                var children = Project(menu.Children);
                var hasDestination = !string.IsNullOrWhiteSpace(menu.Url) || !string.IsNullOrWhiteSpace(menu.Controller);
                var href = children.Count == 0 && hasDestination ? resolveHref(menu) : null;
                if (children.Count == 0 && string.IsNullOrWhiteSpace(href)) continue;
                var current = !currentFound && href is not null && IsCurrent(menu, href, location);
                currentFound |= current;
                result.Add(new(menu.Id, menu.Title,
                    string.IsNullOrWhiteSpace(menu.Icon) ? (children.Count > 0 ? "bx bx-folder" : "bx bx-circle") : menu.Icon,
                    href, current, children));
            }
            return result;
        }
        return Project(eligibleMenus);
    }

    private static bool IsCurrent(AdminMenuItemDto menu, string href, AdminNavigationLocation location)
    {
        if (string.IsNullOrWhiteSpace(menu.Url))
            return string.Equals(string.IsNullOrWhiteSpace(menu.Area) ? "Admin" : menu.Area, location.Area, StringComparison.OrdinalIgnoreCase)
                && string.Equals(menu.Controller, location.Controller, StringComparison.OrdinalIgnoreCase)
                && string.Equals(string.IsNullOrWhiteSpace(menu.Action) ? "Index" : menu.Action, location.Action, StringComparison.OrdinalIgnoreCase);
        if (!href.StartsWith('/') || href.StartsWith("//") || href.StartsWith("/\\")) return false;
        var address = href.Split('#', 2)[0].Split('?', 2);
        if (!string.Equals(address[0].TrimEnd('/'), location.Path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return false;
        var required = QueryHelpers.ParseQuery(address.Length > 1 ? address[1] : "");
        var actual = QueryHelpers.ParseQuery(location.Query);
        return required.All(pair => actual.TryGetValue(pair.Key, out var values) && pair.Value.SequenceEqual(values));
    }
}
