using GaoApp.Domain.Enums;

namespace GaoApp.Application.Common;

public static class BarcodeTypeDisplayHelper
{
    public static string ToDisplayText(BarcodeType type)
    {
        return type switch
        {
            BarcodeType.Internal => "Nội bộ",
            BarcodeType.External => "Mã nhà sản xuất",
            BarcodeType.Supplier => "Mã nhà cung cấp",
            BarcodeType.Packaging => "Mã đóng gói",
            BarcodeType.Legacy => "Mã hệ thống cũ",
            _ => type.ToString()
        };
    }
}