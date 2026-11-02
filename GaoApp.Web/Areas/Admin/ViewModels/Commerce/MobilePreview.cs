namespace GaoApp.Web.Areas.Admin.ViewModels.Commerce;

public sealed record MobileOrder(string Id, string Customer, string Initials, string Time, int Lines, decimal Amount, string Status, bool Paid);
public sealed record MobileProduct(int Id, string Name, string Pack, string Mark, string Color, string Category, decimal Price, int Stock, string Barcode);
public sealed record MobileReceivingLine(int Id, string Name, string Pack, int Expected, int Quantity, bool Checked);

/// <summary>Fixed UI fixture, independent of tenant data, menu configuration and business services.</summary>
public sealed record MobilePreview(
    string Store,
    IReadOnlyList<MobileOrder> Orders,
    IReadOnlyList<MobileProduct> Products,
    IReadOnlyList<MobileReceivingLine> Receiving,
    IReadOnlyList<CommerceNavigationItem> Navigation)
{
    public static MobilePreview CreateDemo() => new(
        "Gạo Mart · Trung tâm",
        [
            new("DH-1048", "Nguyễn Minh Anh", "MA", "09:42", 2, 1240000, "pending", false),
            new("DH-1047", "Thực phẩm sạch Hương Việt", "HV", "09:28", 8, 3680000, "paid", true),
            new("DH-1046", "Nguyễn Thị Thảo", "TT", "09:10", 4, 2130000, "shipping", false),
            new("DH-1045", "Công ty TNHH Phân phối Thương mại và Dịch vụ Nông sản Việt Nam", "NS", "08:55", 17, 12480000, "completed", true)
        ],
        [
            new(1, "Gạo ST25", "Bao 5 kg", "ST25", "blue", "rice", 185000, 24, "893000000001"),
            new(2, "Gạo Jasmine", "Bao 10 kg", "JASMINE", "teal", "rice", 270000, 18, "893000000002"),
            new(3, "Nếp sáp", "Bao 5 kg", "NẾP SÁP", "amber", "sticky", 160000, 15, "893000000003"),
            new(4, "Gạo lứt", "Túi 1 kg", "GẠO LỨT", "rose", "rice", 45000, 30, "893000000004"),
            new(5, "Gạo tám thơm", "Bao 5 kg", "TÁM THƠM", "violet", "rice", 145000, 20, "893000000005"),
            new(6, "Nếp cái hoa vàng", "Túi 2 kg", "HOA VÀNG", "sand", "sticky", 90000, 16, "893000000006")
        ],
        [
            new(1, "Gạo ST25", "bao 5 kg", 12, 12, true),
            new(2, "Gạo Jasmine", "bao 10 kg", 6, 6, true),
            new(3, "Nếp sáp", "bao 5 kg", 4, 4, true),
            new(4, "Gạo tẻ", "bao 25 kg", 10, 0, false),
            new(5, "Gạo tám thơm", "bao 5 kg", 8, 0, false),
            new(6, "Gạo lứt", "túi 1 kg", 20, 0, false),
            new(7, "Nếp cái hoa vàng", "túi 2 kg", 10, 0, false),
            new(8, "Gạo Séng Cù", "bao 5 kg", 6, 0, false)
        ],
        [
            new("home", "Tổng quan", "m2-home", "m2-home"),
            new("orders", "Đơn hàng", "m2-receipt", "m2-orders"),
            new("receiving", "Nhập hàng", "m2-box", "m2-receiving"),
            new("pos", "Bán hàng", "m2-bag", "m2-pos")
        ]);
}
