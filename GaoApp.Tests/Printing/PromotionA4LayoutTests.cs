using System.Drawing;
using System.Runtime.Versioning;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Infrastructure.Printing;
using ZXing;

namespace GaoApp.Tests.Printing;

[SupportedOSPlatform("windows")]
public sealed class PromotionA4LayoutTests
{
    private static LabelPrintPayload Payload(int quantity) => new(PromotionA4Layout.DefaultDesign(),
        new("Brother", "Brother DCP-B7640DW", 300, 210, 0, 0),
        Enumerable.Range(1, quantity).Select(i => new LabelPrintItem(ProductLabelRenderer.SampleFor(PromotionA4Layout.DefaultDesign())
            with { VariantId = i, Name = $"TEM {i} · Sữa tươi không đường", Barcode = i == 1 ? "8938505974194" : $"LABEL-{i}" }, 1)).ToList());

    [Theory]
    [InlineData(1, 1)] [InlineData(4, 1)] [InlineData(5, 1)] [InlineData(10, 1)] [InlineData(11, 2)] [InlineData(25, 3)]
    public void Sheets_fill_by_row_and_do_not_duplicate_unused_slots(int quantity, int expectedPages)
    {
        var payload = Payload(quantity);
        Assert.Equal(expectedPages, PromotionA4Layout.PageCount(payload.Items));
        for (int page = 1; page <= expectedPages; page++)
        {
            var slots = PromotionA4Layout.Page(payload.Items, page);
            Assert.Equal(Math.Min(10, quantity - (page - 1) * 10), slots.Count);
            for (int index = 0; index < slots.Count; index++)
            {
                var slot = slots[index];
                Assert.Equal((page - 1) * 10 + index + 1, slot.Product.VariantId);
                Assert.Equal(index / 2, slot.Row); Assert.Equal(index % 2, slot.Column);
                Assert.Equal(index % 2 == 0 ? 10m : 110m, slot.Xmm);
                Assert.Equal(13.5m + index / 2 * 55m, slot.Ymm);
            }
        }
    }

    [Theory]
    [InlineData(4)] [InlineData(5)] [InlineData(10)] [InlineData(11)]
    public void Preview_keeps_physical_A4_geometry_scannable_codes_and_blank_unused_cells(int quantity)
    {
        var payload = Payload(quantity);
        var output = Path.GetFullPath(Path.Combine("TestResults", "promotion-a4"));
        Directory.CreateDirectory(output);
        for (int page = 1; page <= PromotionA4Layout.PageCount(payload.Items); page++)
        {
            var png = PromotionA4Layout.Preview(payload, page);
            File.WriteAllBytes(Path.Combine(output, $"preview-{quantity}-page-{page}.png"), png);
            using var stream = new MemoryStream(png);
            using var sheet = new Bitmap(stream);
            Assert.Equal(ProductLabelRenderer.Dots(210, 300), sheet.Width);
            Assert.Equal(ProductLabelRenderer.Dots(297, 300), sheet.Height);
            var slots = PromotionA4Layout.Page(payload.Items, page);
            foreach (var slot in slots)
            {
                using var label = sheet.Clone(new Rectangle(ProductLabelRenderer.Dots(slot.Xmm, 300), ProductLabelRenderer.Dots(slot.Ymm, 300),
                    ProductLabelRenderer.Dots(90, 300), ProductLabelRenderer.Dots(50, 300)), sheet.PixelFormat);
                var luminance = new RGBLuminanceSource(ToRgb(label), label.Width, label.Height, RGBLuminanceSource.BitmapFormat.RGB24);
                var decoded = new BarcodeReaderGeneric { AutoRotate = true, Options = new() { TryHarder = true } }.Decode(luminance);
                Assert.NotNull(decoded);
                Assert.Equal(slot.Product.Barcode, decoded.Text);
                Assert.Equal(slot.Product.VariantId == 1 ? BarcodeFormat.EAN_13 : BarcodeFormat.CODE_128, decoded.BarcodeFormat);
            }
            for (int index = slots.Count; index < 10; index++)
            {
                int left = ProductLabelRenderer.Dots(10 + index % 2 * 100, 300), top = ProductLabelRenderer.Dots(13.5m + index / 2 * 55, 300);
                for (int y = top + 5; y < top + ProductLabelRenderer.Dots(50, 300) - 5; y += 12)
                for (int x = left + 5; x < left + ProductLabelRenderer.Dots(90, 300) - 5; x += 12)
                    Assert.Equal(Color.White.ToArgb(), sheet.GetPixel(x, y).ToArgb());
            }
        }
    }

    [Fact]
    public void Quantities_continue_in_order_across_pages_and_invalid_A4_jobs_cannot_use_thermal_commands()
    {
        var p = Payload(3);
        var items = p.Items.Select(x => x with { Quantity = 4 }).ToList();
        Assert.Equal(new[] { 1, 1, 1, 1, 2, 2, 2, 2, 3, 3 }, PromotionA4Layout.Page(items, 1).Select(x => x.Product.VariantId));
        Assert.Equal(new[] { 3, 3 }, PromotionA4Layout.Page(items, 2).Select(x => x.Product.VariantId));
        Assert.Throws<ValidationAppException>(() => PromotionA4Layout.Page(items, 3));
        Assert.Throws<ValidationAppException>(() => PromotionA4Layout.Quantity([items[0] with { Quantity = 1001 }]));
        Assert.Throws<ValidationAppException>(() => (p.Design with { Columns = 1 }).Validate());
        Assert.Throws<ValidationAppException>(() => ProductLabelRenderer.Commands(p).ToArray());
        Assert.Throws<ValidationAppException>(() => PromotionA4Layout.Preview(p with { Printer = p.Printer with { Dpi = 203 } }, 1));
        Assert.Throws<ValidationAppException>(() => PromotionA4Layout.Preview(p with { Printer = p.Printer with { OffsetXmm = 6 } }, 1));
        Assert.DoesNotContain("paperMode", LabelJson.Write(ProductLabelLayouts.DefaultDesign("promotion-shelf")));
        Assert.False(LabelJson.Read<ProductLabelDesign>("{\"layout\":\"promotion-shelf\"}").IsA4);
    }

    private static byte[] ToRgb(Bitmap bitmap)
    {
        var bytes = new byte[bitmap.Width * bitmap.Height * 3];
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y); int index = (y * bitmap.Width + x) * 3;
            bytes[index] = color.R; bytes[index + 1] = color.G; bytes[index + 2] = color.B;
        }
        return bytes;
    }
}
