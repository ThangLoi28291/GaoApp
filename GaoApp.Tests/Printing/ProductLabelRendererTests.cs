using System.Drawing;
using System.Runtime.Versioning;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Infrastructure.Printing;
using ZXing;

namespace GaoApp.Tests.Printing;

[SupportedOSPlatform("windows")]
public sealed class ProductLabelRendererTests
{
    public static IEnumerable<object[]> LayoutCases() =>
        from layout in ProductLabelLayouts.All
        from dpi in new[] { 203, 300 }
        from width in new[] { 35, 50, 80 }
        where layout.Key == "promotion-shelf" ? width >= 50 : width <= 50
        from barcode in new[] { "8938505974194", "AB1234" }
        select new object[] { layout.Key, dpi, width, barcode };

    [Theory, MemberData(nameof(LayoutCases))]
    public void Every_layout_and_paper_decodes_automatic_barcodes_at_printer_resolution(string layout, int dpi, int width, string barcode)
    {
        var design = new ProductLabelDesign { Layout = layout, Columns = 1, WidthMm = width, HeightMm = width == 35 ? 22 : width == 80 ? 50 : 30 };
        using var stream = new MemoryStream(ProductLabelRenderer.Preview(design, ProductLabelRenderer.SampleFor(design) with { Barcode = barcode }, dpi));
        using var bitmap = new Bitmap(stream);
        var result = Decode(bitmap);
        Assert.Equal(barcode, result.Text);
        Assert.Equal(barcode == "AB1234" ? BarcodeFormat.CODE_128 : BarcodeFormat.EAN_13, result.BarcodeFormat);
    }

    [Theory]
    [InlineData("8938505974194", "EAN13")]
    [InlineData("0123456789012", "EAN13")]
    [InlineData("8938505974195", "CODE128")]
    [InlineData("893850597419", "CODE128")]
    [InlineData("96385074", "CODE128")]
    [InlineData("000123", "CODE128")]
    [InlineData("ABc-12", "CODE128")]
    [InlineData("A938505974194", "CODE128")]
    public void Auto_detection_preserves_exact_value_and_checks_EAN_checksum(string barcode, string expectedFormat)
    {
        Assert.Equal(expectedFormat, ProductLabelRenderer.DetectBarcodeFormat(barcode));
        var design = new ProductLabelDesign { WidthMm = 80, HeightMm = 30, Columns = 1 };
        using var bitmap = ProductLabelRenderer.RenderRow(design, [ProductLabelRenderer.Sample with { Barcode = barcode }], 203, 0, 0);
        var decoded = Decode(bitmap);
        Assert.Equal(barcode, decoded.Text);
        Assert.Equal(expectedFormat == "EAN13" ? BarcodeFormat.EAN_13 : BarcodeFormat.CODE_128, decoded.BarcodeFormat);
    }

    [Fact]
    public void Mixed_product_TSPL_row_uses_individual_formats_for_all_layouts()
    {
        foreach (var layout in ProductLabelLayouts.All)
        {
            var design = new ProductLabelDesign { Layout = layout.Key, WidthMm = 50, HeightMm = 30, Columns = 2 };
            var payload = new LabelPrintPayload(design, new("Test", "Test", 203, 108, 0, 0),
                [new(ProductLabelRenderer.SampleFor(design), 1), new(ProductLabelRenderer.SampleFor(design) with { Barcode = "8938505974195" }, 1)]);
            var commands = ProductLabelRenderer.Commands(payload).ToArray();
            Assert.Equal(2, commands.Length);
            var raster = commands[1]; int headerEnd = 0;
            for (int comma = 0; comma < 5; comma++) headerEnd = Array.IndexOf(raster, (byte)',', headerEnd) + 1;
            int width = ProductLabelRenderer.Dots(design.RollWidthMm, 203), height = ProductLabelRenderer.Dots(design.HeightMm, 203), stride = (width + 7) / 8;
            using var bitmap = new Bitmap(width, height);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                bitmap.SetPixel(x, y, (raster[headerEnd + y * stride + x / 8] & (128 >> (x % 8))) == 0 ? Color.Black : Color.White);
            for (int column = 0; column < 2; column++)
            {
                int x = ProductLabelRenderer.Dots(design.LeftMarginMm + column * (design.WidthMm + design.ColumnGapMm), 203);
                using var label = bitmap.Clone(new Rectangle(x, 0, ProductLabelRenderer.Dots(design.WidthMm, 203), height), bitmap.PixelFormat);
                var decoded = Decode(label);
                Assert.Equal(payload.Items[column].Product.Barcode, decoded.Text);
                Assert.Equal(column == 0 ? BarcodeFormat.EAN_13 : BarcodeFormat.CODE_128, decoded.BarcodeFormat);
            }
        }
    }

    [Fact]
    public void Designs_are_distinct_keep_visibility_controls_and_reject_unscannable_codes()
    {
        var previews = new HashSet<string>();
        foreach (var layout in ProductLabelLayouts.All)
        {
            var design = layout.Key == "promotion-shelf" ? ProductLabelLayouts.DefaultDesign(layout.Key) : new ProductLabelDesign { Layout = layout.Key };
            previews.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ProductLabelRenderer.Preview(design))));
            for (int mask = 1; mask < 16; mask++)
            {
                var hidden = design with { ShowName = (mask & 1) != 0, ShowBarcode = (mask & 2) != 0, ShowBarcodeText = false,
                    ShowUnit = (mask & 4) != 0, ShowPrice = (mask & 8) != 0 };
                if (!hidden.ShowName && !hidden.ShowBarcode && !hidden.ShowUnit && !hidden.ShowPrice) continue;
                var product = ProductLabelRenderer.SampleFor(hidden) with { Barcode = hidden.ShowBarcode ? ProductLabelRenderer.Sample.Barcode : "" };
                Assert.NotEmpty(ProductLabelRenderer.Preview(hidden, product));
            }
            Assert.Throws<ValidationAppException>(() => ProductLabelRenderer.Preview(design, ProductLabelRenderer.SampleFor(design) with { Barcode = layout.Key == "promotion-shelf" ? new string('1', 64) : "8938505974195" }));
        }
        Assert.Equal(ProductLabelLayouts.All.Count, previews.Count);
        Assert.Throws<ValidationAppException>(() => new ProductLabelDesign { Layout = "unknown" }.Validate());
        Assert.Throws<ValidationAppException>(() => ProductLabelRenderer.EncodeBarcode("AUTO", "á123"));
        Assert.Throws<ValidationAppException>(() => ProductLabelRenderer.EncodeBarcode("AUTO", ""));
        Assert.Equal("CODE128", ProductLabelRenderer.DetectBarcodeFormat("８938505974194"));
        var legacy = LabelJson.Read<ProductLabelDesign>("{\"barcodeFormat\":\"EAN8\"}");
        Assert.Equal("standard", legacy.Layout); Assert.Equal("EAN8", legacy.BarcodeFormat);
        Assert.NotEmpty(ProductLabelRenderer.Preview(legacy));
    }

    [Theory]
    [InlineData(203, "AUTO", true)] [InlineData(300, "AUTO", true)]
    [InlineData(203, "EAN13", true)] [InlineData(300, "EAN13", true)]
    [InlineData(203, "AUTO", false)] [InlineData(300, "AUTO", false)]
    public void Ean13_has_extended_guards_and_grouped_digits_without_changing_the_encoded_value(int dpi, string format, bool showText)
    {
        var design = new ProductLabelDesign { WidthMm = 50, HeightMm = 30, Columns = 1, BarcodeFormat = format,
            ShowName = false, ShowUnit = false, ShowPrice = false, ShowBarcodeText = showText };
        using var bitmap = ProductLabelRenderer.RenderRow(design, [ProductLabelRenderer.Sample], dpi, 0, 0);
        Assert.Equal(ProductLabelRenderer.Sample.Barcode, Decode(bitmap).Text);
        bool Black(int x, int y) => bitmap.GetPixel(x, y).ToArgb() == Color.Black.ToArgb();
        int top = Enumerable.Range(0, bitmap.Height).First(y => Enumerable.Range(0, bitmap.Width).Any(x => Black(x, y)));
        int start = Enumerable.Range(0, bitmap.Width).First(x => Black(x, top));
        int module = 1; while (Black(start + module, top)) module++;
        int Length(int column)
        {
            int height = 0; while (top + height < bitmap.Height && Black(column, top + height)) height++;
            return height;
        }
        var bars = ProductLabelRenderer.EncodeBarcode("EAN13", ProductLabelRenderer.Sample.Barcode);
        int dataBar = Enumerable.Range(3, 42).First(i => bars[i, 0]);
        int bodyHeight = Length(start + dataBar * module);
        foreach (int guard in new[] { 0, 2, 46, 48, 92, 94 })
            Assert.Equal(bodyHeight + 5 * module, Length(start + guard * module));
        bool InkInDigitCell(int left) => Enumerable.Range(left, 7 * module).Any(x =>
            Enumerable.Range(top + bodyHeight + 1, bitmap.Height - top - bodyHeight - 1).Any(y => Black(x, y)));
        Assert.Equal(showText, InkInDigitCell(start - 12 * module));
        if (showText)
            for (int i = 0; i < 6; i++)
            {
                Assert.True(InkInDigitCell(start + (3 + i * 7) * module));
                Assert.True(InkInDigitCell(start + (50 + i * 7) * module));
            }
    }

    private static Result Decode(Bitmap bitmap)
    {
        var pixels = new byte[bitmap.Width * bitmap.Height * 3];
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y); int i = (y * bitmap.Width + x) * 3;
            pixels[i] = color.R; pixels[i + 1] = color.G; pixels[i + 2] = color.B;
        }
        var result = new BarcodeReaderGeneric { Options = new ZXing.Common.DecodingOptions { TryHarder = true,
            PossibleFormats = new[] { BarcodeFormat.EAN_13, BarcodeFormat.CODE_128 } } }
            .Decode(new RGBLuminanceSource(pixels, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.RGB24));
        Assert.NotNull(result); return result;
    }

    [Theory]
    [InlineData(203)]
    [InlineData(300)]
    public void Large_retail_preset_fits_long_names_and_prices_without_losing_barcode(int dpi)
    {
        var design = ProductLabelLayouts.DefaultDesign("retail-large");
        Assert.Equal(50, design.WidthMm); Assert.Equal(30, design.HeightMm); Assert.Equal(1, design.Columns);
        foreach (var price in new[] { 12500m, 1234567.89m, 9999999.99m })
        {
            using var bitmap = ProductLabelRenderer.RenderRow(design,
                [ProductLabelRenderer.Sample with { Name = "Sữa tươi tiệt trùng không đường nguyên chất hộp 1 lít", Price = price }], dpi, 0, 0);
            Assert.Equal(ProductLabelRenderer.Dots(30, dpi), bitmap.Height);
            Assert.Equal(ProductLabelRenderer.Sample.Barcode, Decode(bitmap).Text);
        }
    }

    [Theory]
    [InlineData("CODE128")]
    [InlineData("EAN13")]
    [InlineData("EAN8")]
    [InlineData("CODE39")]
    public void Printed_raster_decodes_to_the_exact_product_barcode(string format)
    {
        var design = new ProductLabelDesign { BarcodeFormat = format, WidthMm = format == "CODE128" ? 50 : 35, Columns = 1 };
        var expected = ProductLabelRenderer.SampleFor(design).Barcode;
        using var stream = new MemoryStream(ProductLabelRenderer.Preview(design));
        using var bitmap = new Bitmap(stream);
        var pixels = new byte[bitmap.Width * bitmap.Height * 3];
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y); int i = (y * bitmap.Width + x) * 3;
            pixels[i] = color.R; pixels[i + 1] = color.G; pixels[i + 2] = color.B;
        }
        var result = new BarcodeReaderGeneric { Options = new ZXing.Common.DecodingOptions { TryHarder = true } }
            .Decode(new RGBLuminanceSource(pixels, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.RGB24));
        Assert.NotNull(result); Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void Paper_and_columns_are_independent_and_invalid_fit_is_rejected()
    {
        new ProductLabelDesign { WidthMm = 35, Columns = 1 }.Validate();
        new ProductLabelDesign { WidthMm = 50, HeightMm = 30, Columns = 2 }.Validate();
        Assert.Throws<ValidationAppException>(() => new ProductLabelDesign { WidthMm = 50, Columns = 3 }.Validate());
        Assert.Throws<ValidationAppException>(() => new ProductLabelDesign { Columns = 0 }.Validate());
        Assert.Throws<ValidationAppException>(() => ProductLabelRenderer.Preview(new ProductLabelDesign { FontSize = 16 }));
        Assert.Throws<ValidationAppException>(() => ProductLabelRenderer.EncodeBarcode("EAN13", "8938505974195"));
        Assert.Throws<ValidationAppException>(() => ProductLabelRenderer.EncodeBarcode("EAN13", "893850597419"));
        Assert.Throws<ValidationAppException>(() => ProductLabelRenderer.EncodeBarcode("CODE39", "lowercase"));
        Assert.Throws<ValidationAppException>(() => ProductLabelRenderer.EncodeBarcode("CODE128", "a\r\nPRINT 1,1"));
    }

    [Fact]
    public void Odd_quantity_leaves_the_last_column_blank_and_tspl_is_monochrome()
    {
        var d = new ProductLabelDesign();
        var payload = new LabelPrintPayload(d, new("Test", "Test", 203, 108, 0, 0), [new(ProductLabelRenderer.Sample, 3)]);
        var commands = ProductLabelRenderer.Commands(payload).ToArray();
        Assert.Equal(3, commands.Length); // one setup + two physical rows
        Assert.StartsWith("SIZE 74 mm,22 mm", System.Text.Encoding.ASCII.GetString(commands[0]));
        using var row = ProductLabelRenderer.RenderRow(d, [ProductLabelRenderer.Sample], 203, 0, 0);
        int secondColumn = ProductLabelRenderer.Dots(d.LeftMarginMm + d.WidthMm + d.ColumnGapMm, 203);
        for (int y = 0; y < row.Height; y++)
            for (int x = secondColumn; x < row.Width; x++) Assert.Equal(Color.White.ToArgb(), row.GetPixel(x, y).ToArgb());
        var last = commands[^1];
        int headerEnd = 0;
        for (int comma = 0; comma < 5; comma++) headerEnd = Array.IndexOf(last, (byte)',', headerEnd) + 1;
        int stride = (row.Width + 7) / 8;
        for (int y = 0; y < row.Height; y++)
            for (int x = (secondColumn + 7) / 8; x < stride; x++) Assert.Equal(255, last[headerEnd + y * stride + x]);
    }
}
