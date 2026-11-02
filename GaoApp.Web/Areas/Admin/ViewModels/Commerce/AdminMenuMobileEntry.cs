using GaoApp.Application.DTOs.AdminMenus;

namespace GaoApp.Web.Areas.Admin.ViewModels.Commerce;

// Presentation projection only; the administrative service remains the source of store menu/options.
public sealed record AdminMenuMobileEntry(AdminMenuItemDto Item, int? ParentId, string ParentTitle, int Depth)
{
    public static IReadOnlyList<AdminMenuMobileEntry> Flatten(IEnumerable<AdminMenuItemDto> menus,
        int? parentId = null, string parentTitle = "Menu gốc", int depth = 0)
    {
        var result = new List<AdminMenuMobileEntry>();
        var visited = new HashSet<int>();
        if (parentId.HasValue) visited.Add(parentId.Value);
        void Visit(IEnumerable<AdminMenuItemDto> items, int? parent, string title, int level)
        {
            foreach (var item in items.OrderBy(x => x.SortOrder))
            {
                if (!visited.Add(item.Id)) continue;
                result.Add(new(item, parent, title, level));
                Visit(item.Children, item.Id, item.Title, level + 1);
            }
        }
        Visit(menus, parentId, parentTitle, depth);
        return result;
    }
}
