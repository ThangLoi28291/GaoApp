using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Infrastructure.Printing;

public sealed record ProductLabelDesign
{
    // Absent in old job snapshots: keep their serialized payload/hash unchanged.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [RegularExpression("^(roll|a4)$")] public string? PaperMode { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool IsA4 => PaperMode == "a4";
    [Required, StringLength(100)] public string Name { get; init; } = "Tem sản phẩm";
    [Range(20, 108)] public decimal WidthMm { get; init; } = 35;
    [Range(15, 150)] public decimal HeightMm { get; init; } = 22;
    [Range(1, 5)] public int Columns { get; init; } = 2;
    [Range(0, 10)] public decimal ColumnGapMm { get; init; } = 2;
    [Range(0, 10)] public decimal RowGapMm { get; init; } = 2;
    [Range(0, 10)] public decimal LeftMarginMm { get; init; } = 1;
    [Range(0, 10)] public decimal RightMarginMm { get; init; } = 1;
    [RegularExpression("^(one|received|custom)$")] public string QuantityMode { get; init; } = "received";
    // Stored with the template; old job snapshots remain readable without this assignment.
    public int? PrinterId { get; init; }
    public bool ShowPrintButton { get; init; } = true;
    // Explicit formats remain readable for immutable jobs created before automatic detection.
    [RegularExpression("^(AUTO|CODE128|EAN13|EAN8|CODE39)$")] public string BarcodeFormat { get; init; } = "AUTO";
    [Required] public string Layout { get; init; } = "standard";
    [Range(6, 16)] public int FontSize { get; init; } = 8;
    public bool ShowName { get; init; } = true;
    public bool ShowBarcode { get; init; } = true;
    public bool ShowBarcodeText { get; init; } = true;
    public bool ShowUnit { get; init; } = true;
    public bool ShowPrice { get; init; } = true;
    public decimal RollWidthMm => LeftMarginMm + Columns * WidthMm + (Columns - 1) * ColumnGapMm + RightMarginMm;

    public void Validate(decimal? maxWidth = null)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(this, new ValidationContext(this), errors, true)
            || string.IsNullOrWhiteSpace(Name) || QuantityMode is not ("one" or "received" or "custom")
            || PaperMode is not (null or "roll" or "a4")
            || BarcodeFormat is not ("AUTO" or "CODE128" or "EAN13" or "EAN8" or "CODE39")
            || !ProductLabelLayouts.All.Any(x => x.Key == Layout))
            throw new ValidationAppException("Cấu hình mẫu tem không hợp lệ. " + string.Join(" ", errors.Select(x => x.ErrorMessage)));
        if (IsA4) PromotionA4Layout.ValidateDesign(this);
        decimal allowedWidth = maxWidth ?? (IsA4 ? PromotionA4Layout.PaperWidthMm : 108m);
        if (RollWidthMm > allowedWidth)
            throw new ValidationAppException($"Tổng chiều rộng {RollWidthMm:0.##} mm vượt vùng in {allowedWidth:0.##} mm. Giảm số cột, khổ tem hoặc lề.");
        if (!ShowName && !ShowBarcode && !ShowUnit && !ShowPrice)
            throw new ValidationAppException("Mẫu cần hiển thị ít nhất một nội dung.");
    }
}

public sealed record ProductLabelLayout(string Key, string Name, string Description);
public static class ProductLabelLayouts
{
    public static readonly IReadOnlyList<ProductLabelLayout> All = Array.AsReadOnly(new[]
    {
        new ProductLabelLayout("standard", "Cân đối", "Tên phía trên, giá phía dưới."),
        new ProductLabelLayout("price-first", "Giá nổi bật", "Giá lớn ở đầu, mã vạch phía dưới."),
        new ProductLabelLayout("price-tag", "Giá nền đen", "Ô giá tương phản, đơn vị bên cạnh."),
        new ProductLabelLayout("framed", "Khung thanh lịch", "Viền mảnh, tên và giá căn trái."),
        new ProductLabelLayout("retail-large", "Siêu thị · 50 × 30", "Tên đậm, giá lớn, đơn vị riêng và mã vạch phía dưới."),
        new ProductLabelLayout("promotion-shelf", "Khuyến mãi · 50 × 30", "Mã vạch bên trái, giá lẻ gạch ngang rõ và giá sau giảm bên phải.")
    });

    public static ProductLabelDesign DefaultDesign(string key) => key is "retail-large" or "promotion-shelf"
        ? new ProductLabelDesign { Name = key == "promotion-shelf" ? "Tem khuyến mãi 50 × 30" : "Tem siêu thị 50 × 30", Layout = key, WidthMm = 50, HeightMm = 30, Columns = 1, FontSize = key == "promotion-shelf" ? 8 : 9 }
        : new ProductLabelDesign { Layout = key, Columns = 1 };
}

public sealed record LabelProduct(int VariantId, string Name, string Barcode, string Unit, decimal Price, decimal ReceivedQuantity, string? Problem = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LabelPromotionDetails? Promotion { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageUrl { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? UnitId { get; init; }
}
public sealed record LabelPromotionDetails(decimal RetailPrice, decimal MinimumQuantity, DateTime StartAtUtc, DateTime EndAtUtc, bool ShowPeriod = true);
public sealed record LabelTaskLine(LabelProduct Product, int Required, int Printed = 0, bool Removed = false)
{
    public bool Handled { get; init; }
    public bool Skipped { get; init; }
    public int Sent { get; init; }
    public List<LabelLineAction> Actions { get; init; } = [];
    public bool Done => Handled || Printed > 0;
}
public sealed record LabelLineAction(Guid RequestId, string Action, string Reason, int? UserId, string UserName, DateTime AtUtc, string ProductName);
public sealed record LabelQuantity(int VariantId, int Quantity,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? UnitId = null);
public sealed record LabelPrintItem(LabelProduct Product, int Quantity)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool CountsForProgress { get; init; }
}
public sealed record LabelPrinterSnapshot(string Name, string WindowsPrinterName, int Dpi, decimal PrintableWidthMm, decimal OffsetXmm, decimal OffsetYmm);
public sealed record LabelPrintPayload(ProductLabelDesign Design, LabelPrinterSnapshot Printer, List<LabelPrintItem> Items)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ProductProgress { get; init; }
}
public static class LabelJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)!;
}
