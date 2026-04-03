using GaoApp.Application.Common.Results;

namespace GaoApp.Application.Features.ProductAttributes;

public static class ProductAttributeErrors
{
    public static readonly Error InvalidId =
        new("ProductAttribute.InvalidId", "Id thuộc tính không hợp lệ.");

    public static readonly Error InvalidInput =
        new("ProductAttribute.InvalidInput", "Dữ liệu đầu vào không hợp lệ.");

    public static readonly Error NotFound =
        new("ProductAttribute.NotFound", "Thuộc tính không tồn tại hoặc đã bị xóa.");

    public static readonly Error DuplicateCode =
        new("ProductAttribute.DuplicateCode", "Mã thuộc tính đã tồn tại.");

    public static readonly Error DuplicateName =
        new("ProductAttribute.DuplicateName", "Tên thuộc tính đã tồn tại.");

    public static readonly Error CreateFailed =
        new("ProductAttribute.CreateFailed", "Tạo thuộc tính thất bại.");

    public static readonly Error UpdateFailed =
        new("ProductAttribute.UpdateFailed", "Cập nhật thuộc tính thất bại.");

    public static readonly Error ToggleStatusFailed =
        new("ProductAttribute.ToggleStatusFailed", "Cập nhật trạng thái thuộc tính thất bại.");

    public static readonly Error DeleteFailed =
        new("ProductAttribute.DeleteFailed", "Xóa thuộc tính thất bại.");

    public static readonly Error ConcurrencyConflict =
        new("ProductAttribute.ConcurrencyConflict", "Dữ liệu đã bị thay đổi bởi người khác. Vui lòng tải lại trang.");
}