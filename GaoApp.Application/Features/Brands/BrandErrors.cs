using GaoApp.Application.Common.Results;

namespace GaoApp.Application.Features.Brands;

/// <summary>
/// Danh sách lỗi chuẩn cho Brand.
/// </summary>
public static class BrandErrors
{
    public static readonly Error InvalidId =
        new("Brand.InvalidId", "Id thương hiệu không hợp lệ.");

    public static readonly Error NotFound =
        new("Brand.NotFound", "Thương hiệu không tồn tại hoặc đã bị xóa.");

    public static readonly Error DuplicateCode =
        new("Brand.DuplicateCode", "Mã thương hiệu đã tồn tại.");

    public static readonly Error DuplicateName =
        new("Brand.DuplicateName", "Tên thương hiệu đã tồn tại.");

    public static readonly Error CreateFailed =
        new("Brand.CreateFailed", "Tạo thương hiệu thất bại.");

    public static readonly Error UpdateFailed =
        new("Brand.UpdateFailed", "Cập nhật thương hiệu thất bại.");

    public static readonly Error ToggleStatusFailed =
        new("Brand.ToggleStatusFailed", "Cập nhật trạng thái thương hiệu thất bại.");

    public static readonly Error DeleteFailed =
        new("Brand.DeleteFailed", "Xóa thương hiệu thất bại.");

    public static readonly Error ConcurrencyConflict =
        new("Brand.ConcurrencyConflict", "Dữ liệu đã bị thay đổi bởi người khác. Vui lòng tải lại trang.");

    public static readonly Error InvalidInput =
        new("Brand.InvalidInput", "Dữ liệu đầu vào không hợp lệ.");
}