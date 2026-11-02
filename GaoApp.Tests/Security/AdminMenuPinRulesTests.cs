using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Services.AdminMenus;

namespace GaoApp.Tests.Security;

public sealed class AdminMenuPinRulesTests
{
    [Fact]
    public void Choices_use_existing_order_labels_and_leaf_destinations_without_mutating_the_tree()
    {
        var tree = new[] { new AdminMenuItemDto { Id = 1, Title = "Kho", IsActive = true, Children = [
            new() { Id = 3, Title = "Sản phẩm", IsActive = true, SortOrder = 20, Controller = "Product" },
            new() { Id = 2, Title = "Nhập hàng", IsActive = true, SortOrder = 10, Url = "/admin/warehouse-receiving" },
            new() { Id = 4, Title = "Đã tắt", IsActive = false, Controller = "Product" } ] },
            new AdminMenuItemDto { Id = 5, Title = "Trống", IsActive = true } };
        var choices = AdminMenuPinRules.Choices(tree, false);
        Assert.Equal(new[] { 2, 3 }, choices.Select(x => x.Id).ToArray());
        Assert.Equal("Kho / Nhập hàng", choices[0].Path);
        Assert.Equal(new[] { 3, 2, 4 }, tree[0].Children.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void Receipt_admin_rule_applies_at_all_levels_and_cycles_do_not_add_duplicate_choices()
    {
        var parent = new AdminMenuItemDto { Id = 1, Title = "Mẫu in", IsActive = true, Controller = "ReceiptTemplates" };
        parent.Children.Add(new() { Id = 2, Title = "In", IsActive = true, Url = "/admin/receipt-templates/example" });
        var cycle = new AdminMenuItemDto { Id = 3, Title = "Nhóm", IsActive = true }; cycle.Children.Add(cycle);
        Assert.Empty(AdminMenuPinRules.Choices([parent, cycle], false));
        Assert.Equal(2, Assert.Single(AdminMenuPinRules.Choices([parent, cycle], true)).Id);
    }

    [Fact]
    public void Invalid_duplicate_foreign_folder_and_more_than_four_ids_are_rejected()
    {
        int[] eligible = [1, 2, 3, 4, 5];
        Assert.Throws<ArgumentException>(() => AdminMenuPinRules.Validate([1, 2, 3, 4, 5], eligible));
        Assert.Throws<ArgumentException>(() => AdminMenuPinRules.Validate([1, 1], eligible));
        Assert.Throws<ArgumentException>(() => AdminMenuPinRules.Validate([0], eligible));
        Assert.Throws<ArgumentException>(() => AdminMenuPinRules.Validate([99], eligible));
        AdminMenuPinRules.Validate([], eligible);
        AdminMenuPinRules.Validate([4, 1, 3, 2], eligible);
        Assert.Equal(new[] { 4, 1, 3, 2 }, AdminMenuPinRules.Parse("[4,1,3,2,5]"));
        Assert.Empty(AdminMenuPinRules.Parse("{broken"));
    }
}
