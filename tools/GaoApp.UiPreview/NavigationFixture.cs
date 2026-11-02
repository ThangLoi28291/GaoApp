using GaoApp.Application.Common;
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Interfaces.Services.AdminMenus;
using GaoApp.Application.Interfaces.Services.Security;

// In-memory presentation fixtures only. All writes throw; never registered by the real application.
public sealed class NavigationFixture(IHttpContextAccessor accessor) : IAdminMenuService, IAdminMenuPinService, ITenantContext, IStoreAdminAccess
{
    private HttpContext Context => accessor.HttpContext!;
    public int? StoreId => Context.Request.Query["case"] == "tenantless" ? null : 1;
    public bool IsHostAdmin => false;
    public string? Subdomain => "ui-preview";
    public Task<bool> IsAdminAsync(CancellationToken ct = default) => Task.FromResult(Context.Request.Query["admin"] == "1");
    public Task RequireAsync(CancellationToken ct = default) => throw new NotSupportedException("Read-only UI fixture.");
    public Task<IReadOnlyList<int>> GetConfiguredIdsAsync(int storeId, int userId, CancellationToken ct = default)
    {
        if (storeId != 1 || userId != 99) throw new InvalidOperationException("Fixture scope was not preserved.");
        return Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());
    }
    public Task<AdminMenuPinWorkspace> GetWorkspaceAsync(int storeId, int userId, CancellationToken ct = default) => throw new NotSupportedException("Read-only UI fixture.");
    public Task<AdminMenuPinPreview> PreviewAsync(int storeId, int userId, string type, int id, IReadOnlyList<int> menuIds, CancellationToken ct = default) => throw new NotSupportedException("Read-only UI fixture.");
    public Task<AdminMenuPinWorkspace> SaveAsync(int storeId, int userId, SaveAdminMenuPinsRequest request, CancellationToken ct = default) => throw new NotSupportedException("Read-only UI fixture.");
    public Task<List<AdminMenuItemDto>> GetForRenderAsync(int storeId, int userId, CancellationToken ct = default)
    {
        if (storeId != 1 || userId != 99) throw new InvalidOperationException("Fixture scope was not preserved.");
        Context.Items["NavigationReadCalls"] = (int?)Context.Items["NavigationReadCalls"] + 1 ?? 1;
        var name = Context.Request.Query["case"].ToString();
        var orders = Item(12, "Đơn hàng", "/admin/ui-foundation/navigation", "bx bx-receipt");
        if (name == "empty") return Task.FromResult(new List<AdminMenuItemDto>());
        if (name == "one") return Task.FromResult(new List<AdminMenuItemDto> { orders });
        if (name == "filtered") return Task.FromResult(new List<AdminMenuItemDto> { Folder(10, "Bán hàng", orders) });
        return Task.FromResult(new List<AdminMenuItemDto>
        {
            Item(1, "Tổng quan", "/Admin/Home", "bx bx-home-alt"),
            Folder(10, "Bán hàng", Item(11, "Bán tại quầy", "/admin/pos", "bx bx-shopping-bag"), orders,
                Item(13, "Khách hàng", "/Admin/Customers", "bx bx-user")),
            Folder(20, "Hàng hóa & kho", Item(21, "Nhập hàng nhanh", "/admin/warehouse-receiving", "bx bx-package"),
                Folder(22, "Quản lý hàng hóa", Item(23, "Sản phẩm", "/Admin/Products", "bx bx-box"),
                    Item(24, "Kiểm kê và đối chiếu số lượng theo từng quy cách tại các kho của cửa hàng", "/Admin/StockCounts", "bx bx-list-check"))),
            Folder(30, "Theo dõi & quản lý", Item(31, "Báo cáo bán hàng", "/Admin/SalesReport", "bx bx-bar-chart-alt-2"),
                Item(32, "Mẫu phiếu in", "/admin/receipt-templates", "bx bx-printer")),
            Item(40, name == "encoded" ? "<img src=x onerror=alert(1)> & Đơn hàng" : "Tùy chọn cửa hàng", "/Admin/Settings", "bx bx-cog")
        });
    }
    public static AdminMenuItemDto Item(int id, string title, string url, string icon = "bx bx-circle")
        => new() { Id = id, Title = title, Url = url, Icon = icon, IsActive = true, SortOrder = id };
    public static AdminMenuItemDto Folder(int id, string title, params AdminMenuItemDto[] children)
        => new() { Id = id, Title = title, IsActive = true, SortOrder = id, Children = children.ToList() };
    public Task<List<AdminMenuItemDto>> GetForAdminAsync(int s, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AdminMenuItemDto?> GetByIdAsync(int s, int id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AdminMenuFormOptionsDto> GetFormOptionsAsync(int s, int? id = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> CreateAsync(int s, int? u, SaveAdminMenuItemRequest r, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> UpdateAsync(int s, int? u, SaveAdminMenuItemRequest r, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<(bool Success, string? ErrorMessage)> DeleteAsync(int s, int id, int? u, CancellationToken ct = default) => throw new NotSupportedException();
}
