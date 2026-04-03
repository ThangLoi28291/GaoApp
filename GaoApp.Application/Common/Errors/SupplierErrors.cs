using GaoApp.Application.Common.Results;

namespace GaoApp.Application.Common.Errors;

public static class SupplierErrors
{
    public static Error NotFound(int id) =>
        new("Supplier.NotFound", $"Không tìm thấy nhà cung cấp với ID = {id}.");

    public static Error DuplicateCode(string code) =>
        new("Supplier.DuplicateCode", $"Mã nhà cung cấp '{code}' đã tồn tại.");

    public static Error DuplicateName(string name) =>
        new("Supplier.DuplicateName", $"Tên nhà cung cấp '{name}' đã tồn tại.");

    public static readonly Error CreateFailed =
        new("Supplier.CreateFailed", "Tạo nhà cung cấp thất bại.");

    public static readonly Error UpdateFailed =
        new("Supplier.UpdateFailed", "Cập nhật nhà cung cấp thất bại.");

    public static readonly Error ToggleStatusFailed =
        new("Supplier.ToggleStatusFailed", "Cập nhật trạng thái nhà cung cấp thất bại.");

    public static readonly Error DeleteFailed =
        new("Supplier.DeleteFailed", "Xóa nhà cung cấp thất bại.");

    public static readonly Error ConcurrencyConflict =
        new("Supplier.ConcurrencyConflict", "Dữ liệu đã bị thay đổi bởi người khác. Vui lòng tải lại trang.");

    public static readonly Error CodeRequired =
        new("Supplier.Code.Required", "Vui lòng nhập mã nhà cung cấp");
}