using GaoApp.Application.Common.Results;

namespace GaoApp.Application.DTOs.Categories;

/// <summary>
/// Danh sách lỗi chuẩn cho Category.
/// </summary>
public static class CategoryErrors
{
    public static readonly Error InvalidInput =
        new("Category.InvalidInput", "Dữ liệu đầu vào không hợp lệ.");

    public static readonly Error NotFound =
        new("Category.NotFound", "Không tìm thấy danh mục.");

    public static readonly Error ParentNotFound =
        new("Category.ParentNotFound", "Danh mục cha không tồn tại.");

    public static readonly Error DuplicateCode =
        new("Category.DuplicateCode", "Mã danh mục đã tồn tại.");

    public static readonly Error DuplicateName =
        new("Category.DuplicateName", "Tên danh mục đã tồn tại.");

    public static readonly Error CreateFailed =
        new("Category.CreateFailed", "Tạo danh mục thất bại.");

    public static readonly Error UpdateFailed =
        new("Category.UpdateFailed", "Cập nhật danh mục thất bại.");

    public static readonly Error ToggleStatusFailed =
        new("Category.ToggleStatusFailed", "Đổi trạng thái danh mục thất bại.");

    public static readonly Error DeleteFailed =
        new("Category.DeleteFailed", "Xóa danh mục thất bại.");
}