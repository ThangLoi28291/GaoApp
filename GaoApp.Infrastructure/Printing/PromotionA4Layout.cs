using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.Versioning;
using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Infrastructure.Printing;

public sealed record PromotionA4Preview(LabelPrintPayload Payload, int Page = 1);
public sealed record A4LabelSlot(LabelProduct Product, int Index, int Row, int Column, decimal Xmm, decimal Ymm);

public static class PromotionA4Layout
{
    public const decimal PaperWidthMm = 210, PaperHeightMm = 297;
    public const decimal LabelWidthMm = 90, LabelHeightMm = 50;
    public const decimal LeftMm = 10, TopMm = 13.5m, ColumnGapMm = 10, RowGapMm = 5;
    public const int Columns = 2, Rows = 5, Capacity = Columns * Rows;

    public static ProductLabelDesign DefaultDesign() => ProductLabelLayouts.DefaultDesign("promotion-shelf") with
    {
        Name = "A4 · 10 tem khuyến mãi", PaperMode = "a4", WidthMm = LabelWidthMm, HeightMm = LabelHeightMm,
        Columns = Columns, ColumnGapMm = ColumnGapMm, RowGapMm = RowGapMm, LeftMarginMm = LeftMm, RightMarginMm = LeftMm
    };

    public static void ValidateDesign(ProductLabelDesign design)
    {
        if (design.Layout != "promotion-shelf" || design.WidthMm != LabelWidthMm || design.HeightMm != LabelHeightMm ||
            design.Columns != Columns || design.ColumnGapMm != ColumnGapMm || design.RowGapMm != RowGapMm ||
            design.LeftMarginMm != LeftMm || design.RightMarginMm != LeftMm)
            throw new ValidationAppException("Mẫu A4 dùng tem 90 × 50 mm, 2 cột × 5 hàng, lề trái/phải 10 mm, cách cột 10 mm và cách hàng 5 mm.");
    }

    public static int Quantity(IReadOnlyList<LabelPrintItem> items)
    {
        if (items is null || items.Count is < 1 or > 500 || items.Any(x => x is null || x.Product is null || x.Quantity is < 1 or > 1000))
            throw new ValidationAppException("Mỗi lệnh A4 cần từ 1 đến 1.000 tem, tối đa 500 sản phẩm.");
        long total = items.Sum(x => (long)x.Quantity);
        if (total > 1000) throw new ValidationAppException("Mỗi lệnh A4 tối đa 1.000 tem.");
        return (int)total;
    }
    public static int PageCount(IReadOnlyList<LabelPrintItem> items) => (Quantity(items) + Capacity - 1) / Capacity;

    // Row-major: 1,2 / 3,4 / 5,6. Repeated quantities remain adjacent; unused slots stay blank.
    public static IReadOnlyList<A4LabelSlot> Page(IReadOnlyList<LabelPrintItem> items, int page)
    {
        if (page < 1 || page > PageCount(items)) throw new ValidationAppException("Trang xem mẫu A4 không hợp lệ.");
        int skip = (page - 1) * Capacity;
        var products = items.SelectMany(x => Enumerable.Repeat(x.Product, x.Quantity)).Skip(skip).Take(Capacity);
        return products.Select((product, index) => new A4LabelSlot(product, skip + index, index / Columns, index % Columns,
            LeftMm + index % Columns * (LabelWidthMm + ColumnGapMm), TopMm + index / Columns * (LabelHeightMm + RowGapMm))).ToArray();
    }

    [SupportedOSPlatform("windows")]
    public static Bitmap Render(LabelPrintPayload payload, int page)
    {
        if (!payload.Design.IsA4) throw new ValidationAppException("Yêu cầu này cần mẫu giấy A4.");
        payload.Design.Validate(PaperWidthMm);
        if (payload.Printer.Dpi != 300 || payload.Printer.OffsetXmm is < -5 or > 5 || payload.Printer.OffsetYmm is < -5 or > 5)
            throw new ValidationAppException("Mẫu A4 dùng 300 DPI; dịch ngang/dọc trong khoảng -5 đến 5 mm.");
        var slots = Page(payload.Items, page);
        int dpi = payload.Printer.Dpi;
        var bitmap = new Bitmap(ProductLabelRenderer.Dots(PaperWidthMm, dpi), ProductLabelRenderer.Dots(PaperHeightMm, dpi), PixelFormat.Format24bppRgb);
        try
        {
            bitmap.SetResolution(dpi, dpi);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            using var cut = new Pen(Color.FromArgb(165, 165, 165), Math.Max(1, ProductLabelRenderer.Dots(.1m, dpi)));
            foreach (var slot in slots)
            {
                int x = ProductLabelRenderer.Dots(slot.Xmm + payload.Printer.OffsetXmm, dpi);
                int y = ProductLabelRenderer.Dots(slot.Ymm + payload.Printer.OffsetYmm, dpi);
                int width = ProductLabelRenderer.Dots(LabelWidthMm, dpi), height = ProductLabelRenderer.Dots(LabelHeightMm, dpi);
                var state = graphics.Save();
                graphics.SetClip(new Rectangle(x, y, width, height));
                ProductLabelRenderer.DrawLabel(graphics, payload.Design, slot.Product, x, y, dpi);
                graphics.Restore(state);
                graphics.DrawRectangle(cut, x, y, width, height);
            }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    [SupportedOSPlatform("windows")]
    public static byte[] Preview(LabelPrintPayload payload, int page)
    {
        ProductLabelRenderer.ValidatePayload(payload);
        using var bitmap = Render(payload, page);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
