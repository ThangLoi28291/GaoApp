using GaoApp.Application.Common.Results;

namespace GaoApp.Application.Features.Promotions;

public static class PromotionErrors
{
    public static readonly Error InvalidMixedQuantity =
        new("Promotion.InvalidMixedQuantity", "Số lượng gốc mỗi thùng phải lớn hơn 0.");

    public static readonly Error InvalidMixedMembers =
        new("Promotion.InvalidMixedMembers", "Chọn ít nhất 2 mã hàng khác nhau, đang hoạt động và có cùng đơn vị gốc để ghép vị.");

    public static readonly Error InvalidId =
        new("Promotion.InvalidId", "Mã chương trình không hợp lệ.");

    public static readonly Error NotFound =
        new("Promotion.NotFound", "Không tìm thấy chương trình khuyến mãi.");

    public static readonly Error InvalidInput =
        new("Promotion.InvalidInput", "Dữ liệu chương trình không hợp lệ.");

    public static readonly Error DuplicateName =
        new("Promotion.DuplicateName", "Tên chương trình đã tồn tại.");

    public static readonly Error InvalidDate =
        new("Promotion.InvalidDate", "Ngày kết thúc phải lớn hơn ngày bắt đầu.");

    public static readonly Error ProductItemRequired =
        new("Promotion.ProductItemRequired", "Vui lòng chọn ít nhất 1 sản phẩm áp dụng.");

    public static readonly Error ComboRuleRequired =
        new("Promotion.ComboRuleRequired", "Combo phải có ít nhất 2 sản phẩm.");

    public static readonly Error InvalidComboPrice =
        new("Promotion.InvalidComboPrice", "Giá combo phải lớn hơn 0.");

    public static readonly Error InvalidBuyGet =
        new("Promotion.InvalidBuyGet", "Số lượng mua và số lượng tặng phải lớn hơn 0.");

    public static readonly Error CreateFailed =
        new("Promotion.CreateFailed", "Tạo chương trình khuyến mãi thất bại.");

    public static readonly Error UpdateFailed =
        new("Promotion.UpdateFailed", "Cập nhật chương trình khuyến mãi thất bại.");

    public static readonly Error DeleteFailed =
        new("Promotion.DeleteFailed", "Xóa chương trình khuyến mãi thất bại.");

    public static readonly Error ToggleFailed =
        new("Promotion.ToggleFailed", "Cập nhật trạng thái chương trình thất bại.");

    public static readonly Error DuplicateFailed =
        new("Promotion.DuplicateFailed", "Nhân bản chương trình thất bại.");

    public static readonly Error ConcurrencyConflict =
        new("Promotion.ConcurrencyConflict", "Dữ liệu đã bị thay đổi bởi người khác. Vui lòng tải lại.");
}
