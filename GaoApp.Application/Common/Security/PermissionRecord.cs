namespace GaoApp.Application.Common.Security;

/// <summary>
/// Record mô tả 1 permission chuẩn trong hệ thống.
/// Dùng để seed DB, render UI, group checkbox, validate convention...
/// </summary>
public sealed class PermissionRecord
{
    /// <summary>
    /// Mã permission chuẩn. Ví dụ: catalog.category.view
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Tên hiển thị cho admin UI. Ví dụ: Xem danh mục.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Nhóm chính dùng để render UI. Ví dụ: Catalog, Inventory, POS...
    /// </summary>
    public required string GroupName { get; init; }

    /// <summary>
    /// Module logic. Ví dụ: catalog, inventory, security...
    /// </summary>
    public required string Module { get; init; }

    /// <summary>
    /// Entity logic. Ví dụ: category, product, role...
    /// </summary>
    public required string Entity { get; init; }

    /// <summary>
    /// Action logic. Ví dụ: view, create, update...
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// Mô tả ngắn.
    /// </summary>
    public string? Description { get; init; }
}