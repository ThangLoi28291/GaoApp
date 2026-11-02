using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Runtime.Versioning;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Infrastructure.Printing;

namespace GaoApp.LabelPrintClient;

public interface IA4LabelPrintTransport
{
    void Validate(LabelPrinterSnapshot printer);
    int? Send(string documentName, LabelPrintPayload payload);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsA4LabelPrinter : IA4LabelPrintTransport
{
    public void Validate(LabelPrinterSnapshot printer)
    {
        using var document = Document(printer.WindowsPrinterName);
        var area = document.DefaultPageSettings.PrintableArea;
        const float mm = 25.4f / 100;
        float left = (float)(PromotionA4Layout.LeftMm + printer.OffsetXmm);
        float top = (float)(PromotionA4Layout.TopMm + printer.OffsetYmm);
        float right = (float)(200m + printer.OffsetXmm), bottom = (float)(283.5m + printer.OffsetYmm);
        if (area.Left * mm > left || area.Top * mm > top || area.Right * mm < right || area.Bottom * mm < bottom)
            throw new ValidationAppException("Vùng in A4 của driver không chứa đủ tem. Giảm dịch ngang/dọc hoặc kiểm tra driver Brother.");
    }

    public int? Send(string documentName, LabelPrintPayload payload)
    {
        using var document = Document(payload.Printer.WindowsPrinterName);
        document.DocumentName = documentName;
        int page = 1, count = PromotionA4Layout.PageCount(payload.Items);
        document.PrintPage += (_, args) =>
        {
            if (args.Graphics is not { } graphics) throw new IOException("Không tạo được vùng in A4.");
            using var bitmap = PromotionA4Layout.Render(payload, page);
            // PrintPage begins at the hardware printable origin. Translate back to
            // the physical paper origin and draw in millimetres at actual size.
            var saved = graphics.Save();
            try
            {
                graphics.PageUnit = GraphicsUnit.Millimeter;
                graphics.TranslateTransform(-args.PageSettings.HardMarginX * 25.4f / 100,
                    -args.PageSettings.HardMarginY * 25.4f / 100);
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.DrawImage(bitmap, new RectangleF(0, 0, (float)PromotionA4Layout.PaperWidthMm, (float)PromotionA4Layout.PaperHeightMm));
            }
            finally { graphics.Restore(saved); }
            args.HasMorePages = ++page <= count;
        };
        document.Print(); // StandardPrintController: no browser/Windows print dialog.
        // PrintDocument does not expose the spool job ID. Submission still has
        // the durable request claim; never invent a job ID or replay the request.
        return null;
    }

    private static PrintDocument Document(string name)
    {
        var document = new PrintDocument { PrintController = new StandardPrintController(), OriginAtMargins = false };
        try
        {
            document.PrinterSettings.PrinterName = name;
            if (!document.PrinterSettings.IsValid) throw new ValidationAppException("Không đọc được driver máy in đã chọn trên client.");
            var a4 = document.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(x => x.Kind == PaperKind.A4)
                ?? throw new ValidationAppException("Driver máy in đã chọn không có khổ A4. Chọn Brother và cài driver đầy đủ.");
            document.PrinterSettings.PrintToFile = false;
            document.PrinterSettings.Copies = 1;
            document.PrinterSettings.Collate = false;
            document.PrinterSettings.PrintRange = PrintRange.AllPages;
            document.PrinterSettings.Duplex = Duplex.Simplex;
            document.DefaultPageSettings.PaperSize = a4;
            document.DefaultPageSettings.Landscape = false;
            document.DefaultPageSettings.Color = false;
            document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
