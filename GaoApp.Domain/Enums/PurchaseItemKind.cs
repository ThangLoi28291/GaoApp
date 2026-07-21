namespace GaoApp.Domain.Enums;

/// <summary>
/// Nguồn nhận diện mặt hàng trên chứng từ mua.
/// FreeText giữ nguyên mô tả ban đầu cho tới khi được liên kết danh mục lúc nhận hàng.
/// </summary>
public enum PurchaseItemKind
{
    Catalog = 1,
    FreeText = 2
}
