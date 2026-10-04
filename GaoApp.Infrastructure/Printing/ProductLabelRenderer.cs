using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using GaoApp.Application.Common.Exceptions;
using ZXing;

namespace GaoApp.Infrastructure.Printing;

[SupportedOSPlatform("windows")]
public static class ProductLabelRenderer
{
    public static readonly LabelProduct Sample = new(0, "Sữa tươi không đường", "8938505974194", "Hộp", 12500, 1);
    public static LabelProduct SampleFor(ProductLabelDesign design) => Sample with { Barcode = design.BarcodeFormat switch { "EAN8" => "96385074", "CODE39" => "ABC123", _ => Sample.Barcode } };
    public static int Dots(decimal mm, int dpi) => (int)Math.Round(mm * dpi / 25.4m);

    public static byte[] Preview(ProductLabelDesign design, LabelProduct? product = null, int dpi = 203)
    {
        design.Validate();
        using var bitmap = RenderRow(design, Enumerable.Repeat(product ?? SampleFor(design), design.Columns).ToArray(), dpi, 0, 0);
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    // The same raster is used by preview and the TSPL stream; Windows renders Vietnamese fonts.
    public static Bitmap RenderRow(ProductLabelDesign d, IReadOnlyList<LabelProduct> products, int dpi, decimal offsetX, decimal offsetY)
    {
        var bitmap = new Bitmap(Dots(d.RollWidthMm, dpi), Dots(d.HeightMm, dpi), PixelFormat.Format24bppRgb);
        try
        {
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Color.White);
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            for (var column = 0; column < products.Count; column++)
            {
                int x = Dots(d.LeftMarginMm + column * (d.WidthMm + d.ColumnGapMm) + offsetX, dpi);
                int y = Dots(offsetY, dpi);
                var state = g.Save();
                g.SetClip(new Rectangle(x, 0, Dots(d.WidthMm, dpi), bitmap.Height));
                DrawLabel(g, d, products[column], x, y, dpi);
                g.Restore(state);
            }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    private static void DrawLabel(Graphics g, ProductLabelDesign d, LabelProduct p, int x, int y, int dpi)
    {
        if (d.Layout == "standard") { DrawStandard(g, d, p, x, y, dpi); return; }
        if (d.Layout == "retail-large") { DrawRetailLarge(g, d, p, x, y, dpi); return; }
        int labelWidth = Dots(d.WidthMm, dpi), labelHeight = Dots(d.HeightMm, dpi);
        if (d.Layout == "framed")
        {
            int inset = Dots(0.35m, dpi);
            using var pen = new Pen(Color.Black, Math.Max(1, Dots(0.15m, dpi)));
            g.DrawRectangle(pen, x + inset, y + inset, labelWidth - 2 * inset - 1, labelHeight - 2 * inset - 1);
        }
        int pad = Dots(1, dpi), gap = Math.Max(2, Dots(0.25m, dpi));
        int width = labelWidth - pad * 2, bottom = y + labelHeight - pad;
        x += pad; y += pad;
        using var normal = new Font("Arial", d.FontSize * dpi / 72f, d.Layout == "framed" ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var price = new Font("Arial", (d.FontSize + (d.Layout == "price-first" ? 3 : 2)) * dpi / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font("Arial", 6 * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var left = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var singleLine = (StringFormat)center.Clone(); singleLine.FormatFlags |= StringFormatFlags.NoWrap;
        var textFormat = d.Layout is "framed" or "price-tag" ? left : center;
        int nameHeight = d.ShowName ? (int)Math.Ceiling(normal.GetHeight(g) * 2) : 0;
        int codeTextHeight = d.ShowBarcode && d.ShowBarcodeText ? (int)Math.Ceiling(small.GetHeight(g)) : 0;
        int footHeight = d.ShowPrice || d.ShowUnit ? (int)Math.Ceiling(price.GetHeight(g)) + (d.Layout == "price-tag" ? gap * 2 : 0) : 0;
        int barHeight = d.ShowBarcode ? bottom - y - nameHeight - codeTextHeight - footHeight - gap * 2 : 0;
        if (d.ShowBarcode && barHeight < Dots(5, dpi) || !d.ShowBarcode && y + nameHeight + footHeight + gap > bottom)
            throw new ValidationAppException("Nội dung không vừa chiều cao tem. Giảm cỡ chữ, ẩn bớt nội dung hoặc tăng chiều cao.");

        void DrawPrice(int top)
        {
            if (footHeight == 0) return;
            string amount = d.ShowPrice ? p.Price.ToString("#,0.##", CultureInfo.GetCultureInfo("vi-VN")) + " đ" : "";
            if (d.Layout == "price-tag" && d.ShowPrice)
            {
                int unitWidth = d.ShowUnit ? width / 3 : 0;
                int priceWidth = width - unitWidth - (unitWidth > 0 ? gap : 0);
                g.FillRectangle(Brushes.Black, x, top, priceWidth, footHeight);
                g.DrawString(amount, price, Brushes.White, new RectangleF(x + gap, top, priceWidth - 2 * gap, footHeight), singleLine);
                if (d.ShowUnit) g.DrawString(p.Unit, normal, Brushes.Black, new RectangleF(x + priceWidth + gap, top, unitWidth, footHeight), singleLine);
            }
            else
            {
                var footer = amount + (d.ShowUnit ? (d.ShowPrice ? " / " : "") + p.Unit : "");
                g.DrawString(footer, price, Brushes.Black, new RectangleF(x, top, width, footHeight), textFormat);
            }
        }
        if (d.Layout == "price-first" && footHeight > 0) { DrawPrice(y); y += footHeight + gap; }
        if (d.ShowName) { g.DrawString(p.Name, normal, Brushes.Black, new RectangleF(x, y, width, nameHeight), textFormat); y += nameHeight + gap; }
        if (d.ShowBarcode) DrawBarcode(g, d, p, x, y, width, barHeight, codeTextHeight, small, center);
        if (d.Layout != "price-first") DrawPrice(bottom - footHeight);
    }

    private static void DrawRetailLarge(Graphics g, ProductLabelDesign d, LabelProduct p, int x, int y, int dpi)
    {
        int pad = Dots(1, dpi), gap = Math.Max(2, Dots(0.5m, dpi));
        int width = Dots(d.WidthMm, dpi) - 2 * pad, bottom = y + Dots(d.HeightMm, dpi) - pad;
        x += pad; y += pad;
        using var name = new Font("Arial", d.FontSize * dpi / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font("Arial", 6 * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var unit = new Font("Arial", Math.Max(6, d.FontSize - 1) * dpi / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var left = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var singleLine = (StringFormat)left.Clone(); singleLine.FormatFlags |= StringFormatFlags.NoWrap;
        int nameHeight = d.ShowName ? (int)Math.Ceiling(name.GetHeight(g) * 2) : 0;
        int codeTextHeight = d.ShowBarcode && d.ShowBarcodeText ? (int)Math.Ceiling(small.GetHeight(g)) : 0;
        int priceHeight = d.ShowPrice || d.ShowUnit ? Dots(d.HeightMm >= 30 ? 8m : 5m, dpi) : 0;
        int barHeight = d.ShowBarcode ? bottom - y - nameHeight - priceHeight - codeTextHeight - gap * 2 : 0;
        if (d.ShowBarcode && barHeight < Dots(5, dpi) || !d.ShowBarcode && y + nameHeight + priceHeight + gap > bottom)
            throw new ValidationAppException("Nội dung không vừa chiều cao tem. Giảm cỡ chữ, ẩn bớt nội dung hoặc tăng chiều cao.");
        if (d.ShowName)
        {
            g.DrawString(p.Name, name, Brushes.Black, new RectangleF(x, y, width, nameHeight), left);
            y += nameHeight;
            using var rule = new Pen(Color.Black, Math.Max(1, Dots(0.15m, dpi)));
            g.DrawLine(rule, x, y, x + width, y);
            y += gap;
        }
        if (priceHeight > 0)
        {
            int unitWidth = d.ShowUnit ? width / 4 : 0;
            int priceWidth = width - (unitWidth > 0 ? unitWidth + gap : 0);
            if (d.ShowPrice)
            {
                string amount = p.Price.ToString("#,0.##", CultureInfo.GetCultureInfo("vi-VN")) + " đ";
                // Fit the entire amount, never truncate a price with an ellipsis.
                float size = (d.FontSize + 9) * dpi / 72f;
                while (true)
                {
                    using var price = new Font("Arial", size, FontStyle.Bold, GraphicsUnit.Pixel);
                    if (g.MeasureString(amount, price).Width <= priceWidth && price.GetHeight(g) <= priceHeight)
                    {
                        g.DrawString(amount, price, Brushes.Black, new RectangleF(x, y, priceWidth, priceHeight), singleLine);
                        break;
                    }
                    size -= 0.5f;
                    if (size < 6 * dpi / 72f)
                        throw new ValidationAppException("Giá bán quá dài cho vùng giá trên tem. Tăng khổ tem hoặc ẩn đơn vị.");
                }
            }
            if (d.ShowUnit)
            {
                var box = new RectangleF(d.ShowPrice ? x + width - unitWidth : x, y, d.ShowPrice ? unitWidth : width, priceHeight);
                g.DrawString(p.Unit, unit, Brushes.Black, box, center);
            }
            y += priceHeight + gap;
        }
        if (d.ShowBarcode) DrawBarcode(g, d, p, x, bottom - barHeight - codeTextHeight, width, barHeight, codeTextHeight, small, center);
    }

    // Keep the original layout for previously queued payloads with no layout property.
    private static void DrawStandard(Graphics g, ProductLabelDesign d, LabelProduct p, int x, int y, int dpi)
    {
        var pad = Dots(1, dpi);
        int width = Dots(d.WidthMm, dpi) - pad * 2;
        int bottom = y + Dots(d.HeightMm, dpi) - pad;
        x += pad; y += pad;
        using var normal = new Font("Arial", d.FontSize * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var price = new Font("Arial", (d.FontSize + 2) * dpi / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font("Arial", 6 * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        int nameHeight = d.ShowName ? (int)Math.Ceiling(normal.GetHeight(g) * 2) : 0;
        int codeTextHeight = d.ShowBarcode && d.ShowBarcodeText ? (int)Math.Ceiling(small.GetHeight(g)) : 0;
        int footHeight = d.ShowPrice || d.ShowUnit ? (int)Math.Ceiling(price.GetHeight(g)) : 0;
        int barHeight = d.ShowBarcode ? bottom - y - nameHeight - codeTextHeight - footHeight - 4 : 0;
        if (d.ShowBarcode && barHeight < Dots(5, dpi) || !d.ShowBarcode && y + nameHeight + footHeight > bottom)
            throw new ValidationAppException("Nội dung không vừa chiều cao tem. Giảm cỡ chữ, ẩn bớt nội dung hoặc tăng chiều cao.");
        if (d.ShowName)
        {
            g.DrawString(p.Name, normal, Brushes.Black, new RectangleF(x, y, width, nameHeight), format);
            y += nameHeight + 2;
        }
        if (d.ShowBarcode)
        {
            DrawBarcode(g, d, p, x, y, width, barHeight, codeTextHeight, small, format);
        }
        string footer = (d.ShowPrice ? p.Price.ToString("#,0.##", CultureInfo.GetCultureInfo("vi-VN")) + " đ" : "")
            + (d.ShowUnit ? (d.ShowPrice ? " / " : "") + p.Unit : "");
        if (footHeight > 0) g.DrawString(footer, price, Brushes.Black, new RectangleF(x, bottom - footHeight, width, footHeight), format);
    }

    private static void DrawBarcode(Graphics g, ProductLabelDesign d, LabelProduct p, int x, int y, int width,
        int barHeight, int codeTextHeight, Font small, StringFormat format)
    {
        var bars = EncodeBarcode(d.BarcodeFormat, p.Barcode);
        int module = width / (bars.Width + 20); // Ten quiet modules on each side.
        if (module < 2)
            throw new ValidationAppException($"Mã {p.Barcode} quá dài cho tem {d.WidthMm} mm. Chọn khổ tem rộng hơn để mã vạch quét được.");
        int start = x + (width - bars.Width * module) / 2;
        for (var i = 0; i < bars.Width; i++)
            if (bars[i, 0]) g.FillRectangle(Brushes.Black, start + i * module, y, module, barHeight);
        if (d.ShowBarcodeText)
            g.DrawString(p.Barcode, small, Brushes.Black, new RectangleF(x, y + barHeight, width, codeTextHeight), format);
    }

    public static string DetectBarcodeFormat(string value)
    {
        // EAN-13 is only selected when the complete, unchanged value has a valid check digit.
        if (value is not { Length: 13 } || value.Any(c => c < '0' || c > '9')) return "CODE128";
        int sum = 0;
        for (int i = 0; i < 12; i++) sum += (value[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (sum + value[12] - '0') % 10 == 0 ? "EAN13" : "CODE128";
    }

    public static ZXing.Common.BitMatrix EncodeBarcode(string format, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(c => c < 32 || c > 126))
            throw new ValidationAppException("Sản phẩm chưa có mã vạch đơn vị gốc hợp lệ.");
        if (format == "AUTO") format = DetectBarcodeFormat(value);
        var type = format switch { "CODE128" => BarcodeFormat.CODE_128, "EAN13" => BarcodeFormat.EAN_13,
            "EAN8" => BarcodeFormat.EAN_8, "CODE39" => BarcodeFormat.CODE_39, _ => throw new ValidationAppException("Loại mã vạch không hợp lệ.") };
        if (type == BarcodeFormat.EAN_13 && value.Length != 13 || type == BarcodeFormat.EAN_8 && value.Length != 8)
            throw new ValidationAppException($"{format} yêu cầu đúng số chữ số, gồm số kiểm tra. Không tự thay đổi mã sản phẩm.");
        if (type == BarcodeFormat.CODE_39 && value.Any(c => !"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%".Contains(c)))
            throw new ValidationAppException("Code 39 chỉ hỗ trợ chữ hoa, số và - . dấu cách $ / + %.");
        try { return new MultiFormatWriter().encode(value, type, 0, 1, new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 }); }
        catch (ArgumentException) { throw new ValidationAppException($"Mã {value} không phù hợp {format} hoặc sai số kiểm tra."); }
    }

    public static void ValidatePayload(LabelPrintPayload payload)
    {
        payload.Design.Validate(payload.Printer.PrintableWidthMm);
        if (payload.Printer.Dpi is not (203 or 300)) throw new ValidationAppException("DPI phải là 203 hoặc 300.");
        if (payload.Items.Count == 0 || payload.Items.Count > 500 || payload.Items.Any(x => x.Quantity <= 0)
            || payload.Items.Sum(x => (long)x.Quantity) > 10000)
            throw new ValidationAppException("Mỗi lần in từ 1 đến 10.000 tem, tối đa 500 sản phẩm.");
        foreach (var item in payload.Items)
        {
            using var rendered = RenderRow(payload.Design, [item.Product], payload.Printer.Dpi, 0, 0);
        }
    }

    public static IEnumerable<byte[]> Commands(LabelPrintPayload payload)
    {
        ValidatePayload(payload);
        var d = payload.Design; var printer = payload.Printer;
        yield return Encoding.ASCII.GetBytes(FormattableString.Invariant($"SIZE {d.RollWidthMm:0.##} mm,{d.HeightMm:0.##} mm\r\nGAP {d.RowGapMm:0.##} mm,0 mm\r\nDIRECTION 1\r\nREFERENCE 0,0\r\nSET TEAR ON\r\n"));
        var row = new List<LabelProduct>();
        foreach (var item in payload.Items)
        for (var n = 0; n < item.Quantity; n++)
        {
            row.Add(item.Product);
            if (row.Count < d.Columns) continue;
            yield return RasterCommand(d, printer, row); row.Clear();
        }
        // An odd count leaves unused columns blank; never duplicate the last product.
        if (row.Count > 0) yield return RasterCommand(d, printer, row);
    }

    private static byte[] RasterCommand(ProductLabelDesign d, LabelPrinterSnapshot p, List<LabelProduct> row)
    {
        using var bitmap = RenderRow(d, row, p.Dpi, p.OffsetXmm, p.OffsetYmm);
        int stride = (bitmap.Width + 7) / 8;
        byte[] raster = new byte[stride * bitmap.Height];
        Array.Fill(raster, (byte)255); // TSPL BITMAP: zero is a printed dot.
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var scanline = new byte[Math.Abs(data.Stride)];
            for (int y = 0; y < bitmap.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, scanline, 0, scanline.Length);
                for (int x = 0; x < bitmap.Width; x++)
                    if (scanline[x * 3] + scanline[x * 3 + 1] + scanline[x * 3 + 2] < 384)
                        raster[y * stride + x / 8] &= (byte)~(128 >> (x % 8));
            }
        }
        finally { bitmap.UnlockBits(data); }
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes($"CLS\r\nBITMAP 0,0,{stride},{bitmap.Height},0,"));
        output.Write(raster); output.Write(Encoding.ASCII.GetBytes("\r\nPRINT 1,1\r\n"));
        return output.ToArray();
    }
}
