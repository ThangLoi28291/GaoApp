using GaoApp.Application.Common.Results;

namespace GaoApp.Application.Features.AttributeValues;

public static class AttributeValueErrors
{
    public static readonly Error InvalidId =
        new("AttributeValue.InvalidId", "Id giá trị thuộc tính không hợp lệ.");

    public static readonly Error InvalidInput =
        new("AttributeValue.InvalidInput", "Dữ liệu đầu vào không hợp lệ.");

    public static readonly Error InvalidAttribute =
        new("AttributeValue.InvalidAttribute", "Vui lòng chọn thuộc tính.");

    public static readonly Error AttributeNotFound =
        new("AttributeValue.AttributeNotFound", "Thuộc tính không tồn tại.");

    public static readonly Error NotFound =
        new("AttributeValue.NotFound", "Giá trị thuộc tính không tồn tại hoặc đã bị xóa.");

    public static readonly Error DuplicateCode =
        new("AttributeValue.DuplicateCode", "Mã giá trị đã tồn tại trong thuộc tính này.");

    public static readonly Error DuplicateName =
        new("AttributeValue.DuplicateName", "Tên giá trị đã tồn tại trong thuộc tính này.");

    public static readonly Error CreateFailed =
        new("AttributeValue.CreateFailed", "Tạo giá trị thuộc tính thất bại.");

    public static readonly Error UpdateFailed =
        new("AttributeValue.UpdateFailed", "Cập nhật giá trị thuộc tính thất bại.");

    public static readonly Error ToggleStatusFailed =
        new("AttributeValue.ToggleStatusFailed", "Cập nhật trạng thái giá trị thuộc tính thất bại.");

    public static readonly Error DeleteFailed =
        new("AttributeValue.DeleteFailed", "Xóa giá trị thuộc tính thất bại.");

    public static readonly Error ConcurrencyConflict =
        new("AttributeValue.ConcurrencyConflict", "Dữ liệu đã bị thay đổi bởi người khác. Vui lòng tải lại trang.");
}