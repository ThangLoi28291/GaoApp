using System.Text.Json;
using GaoApp.Application.DTOs.AdminMenus;

namespace GaoApp.Application.Services.AdminMenus;

public static class MenuVisibilityRules
{
    public const int PosTerminals = -1;
    public const int Kiosks = -2;
    public static HashSet<int> Parse(string? json) => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<HashSet<int>>(json) ?? [];

    // A hidden/ineligible ancestor hides its subtree. Empty containers disappear as well.
    public static HashSet<int> VisibleIds(IEnumerable<MenuVisibilityNode> nodes, ISet<int> hidden)
    {
        var all = nodes.ToList();
        var visible = new HashSet<int>();
        var visited = new HashSet<int>();
        bool Visit(MenuVisibilityNode node)
        {
            if (!visited.Add(node.Id) || hidden.Contains(node.Id) || !node.Available) return false;
            var children = all.Where(x => x.ParentId == node.Id).ToList();
            var hasVisibleChild = false;
            foreach (var child in children) hasVisibleChild |= Visit(child);
            if (node.IsFolder && !hasVisibleChild) return false;
            visible.Add(node.Id);
            return true;
        }
        foreach (var root in all.Where(x => x.ParentId == null)) Visit(root);
        return visible;
    }
}
