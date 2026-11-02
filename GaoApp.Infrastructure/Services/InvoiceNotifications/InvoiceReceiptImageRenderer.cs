using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace GaoApp.Infrastructure.Services.InvoiceNotifications;

[SupportedOSPlatform("windows")]
internal static class InvoiceReceiptImageRenderer
{
    public static IReadOnlyList<byte[]> Render(IReadOnlyList<string> lines, int availableParts)
    {
        if (availableParts <= 0) throw new InvalidDataException("InvoiceBundleLimitExceeded");
        const int width = 1000, height = 1500, margin = 40;
        using var surface = new Bitmap(width, height);
        using var measure = Graphics.FromImage(surface);
        using var font = new Font("Segoe UI", 17, FontStyle.Regular, GraphicsUnit.Point);
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.LineLimit };
        var wrapped = new List<string>();
        foreach (var text in lines)
        {
            var row = "";
            foreach (var rune in text.EnumerateRunes())
            {
                var next = row + rune.ToString();
                if (row.Length > 0 && measure.MeasureString(next, font, 100000, format).Width > width - margin * 2 - 2)
                { wrapped.Add(row); row = rune.ToString(); }
                else row = next;
            }
            wrapped.Add(row);
        }
        var lineHeight = (int)Math.Ceiling(font.GetHeight(measure) * 1.18);
        var pageLines = (height - margin * 2 - lineHeight) / lineHeight;
        var pages = (wrapped.Count + pageLines - 1) / pageLines;
        if (pages > availableParts) throw new InvalidDataException("InvoiceBundleLimitExceeded");
        var images = new List<byte[]>();
        for (var page = 0; page < pages; page++)
        {
            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var y = margin;
            foreach (var row in wrapped.Skip(page * pageLines).Take(pageLines))
            { graphics.DrawString(row, font, Brushes.Black, new PointF(margin, y), format); y += lineHeight; }
            graphics.DrawString($"Trang {page + 1}/{pages}", font, Brushes.Gray, new PointF(margin, height - margin - lineHeight), format);
            using var output = new MemoryStream(); bitmap.Save(output, ImageFormat.Png); images.Add(output.ToArray());
        }
        return images;
    }
}
