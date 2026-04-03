using GaoApp.Application.Common.Results;

namespace GaoApp.Application.Features.Units;

public static class UnitErrors
{
    public static readonly Error InvalidId =
        new("Unit.InvalidId", "Id đơn vị không hợp lệ.");

    public static readonly Error InvalidInput =
        new("Unit.InvalidInput", "Dữ liệu đầu vào không hợp lệ.");

    public static readonly Error NotFound =
        new("Unit.NotFound", "Đơn vị không tồn tại hoặc đã bị xóa.");

    public static readonly Error DuplicateCode =
        new("Unit.DuplicateCode", "Mã đơn vị đã tồn tại.");

    public static readonly Error DuplicateName =
        new("Unit.DuplicateName", "Tên đơn vị đã tồn tại.");

    public static readonly Error CreateFailed =
        new("Unit.CreateFailed", "Tạo đơn vị thất bại.");

    public static readonly Error UpdateFailed =
        new("Unit.UpdateFailed", "Cập nhật đơn vị thất bại.");

    public static readonly Error ToggleStatusFailed =
        new("Unit.ToggleStatusFailed", "Cập nhật trạng thái đơn vị thất bại.");

    public static readonly Error DeleteFailed =
        new("Unit.DeleteFailed", "Xóa đơn vị thất bại.");

    public static readonly Error ConcurrencyConflict =
        new("Unit.ConcurrencyConflict", "Dữ liệu đã bị thay đổi bởi người khác. Vui lòng tải lại trang.");
}