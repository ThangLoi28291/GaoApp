using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Infrastructure.Printing;

namespace GaoApp.LabelPrintClient;

// Persist the claim before touching Windows. An uncertain write is never automatically repeated.
[SupportedOSPlatform("windows")]
public sealed class ClientPrintJournal(string directory)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private sealed record Entry(string Hash, string Status, int? SpoolJobId = null);
    public async Task<object> Result(Guid requestId)
    {
        var file = Path.Combine(directory, requestId.ToString("N") + ".json");
        var entry = File.Exists(file) ? LabelJson.Read<Entry>(await File.ReadAllTextAsync(file)) : null;
        return new { ok = true, protocol = LabelClientHost.Protocol, requestId, submitted = entry?.Status == "submitted", spoolJobId = entry?.SpoolJobId };
    }
    public async Task<object> Send(ClientPrintRequest request, Func<IReadOnlyList<string>> installed, ILabelPrintTransport transport,
        IA4LabelPrintTransport? a4Transport = null)
    {
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(1))) throw new ConflictAppException("Đang gửi một lệnh in khác. Đợi rồi thử lại.");
        try
        {
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, request.RequestId.ToString("N") + ".json");
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LabelJson.Write(request.Payload))));
            if (File.Exists(file))
            {
                var existing = LabelJson.Read<Entry>(await File.ReadAllTextAsync(file));
                if (existing.Hash != hash) throw new ConflictAppException("Mã lệnh đã dùng cho nội dung khác.");
                if (existing.Status != "submitted") throw new ConflictAppException("Lệnh này đã gửi nhưng chưa xác định kết quả. Kiểm tra tem thực tế trước khi tạo lệnh mới.");
                return Result(existing.SpoolJobId, true);
            }
            if (!installed().Contains(request.Payload.Printer.WindowsPrinterName, StringComparer.Ordinal))
                throw new ValidationAppException("Máy in đã chọn không có trên client. Kết nối và chọn lại máy in.");
            // Fully validate before recording the irreversible boundary; Commands validates again before sending bytes.
            ProductLabelRenderer.ValidatePayload(request.Payload);
            if (request.Payload.Design.IsA4)
            {
                if (a4Transport is null) throw new ValidationAppException("Bộ kết nối chưa hỗ trợ in A4. Cập nhật client.");
                a4Transport.Validate(request.Payload.Printer);
            }
            await Write(file, new(hash, "sending"));
            int? spoolId = request.Payload.Design.IsA4
                ? a4Transport!.Send("GaoApp · Tem khuyến mãi A4", request.Payload)
                : transport.Send(request.Payload.Printer.WindowsPrinterName, "GaoApp · Tem khuyến mãi", ProductLabelRenderer.Commands(request.Payload));
            await Write(file, new(hash, "submitted", spoolId));
            return Result(spoolId, false);
            object Result(int? id, bool duplicate) => new { ok = true, protocol = LabelClientHost.Protocol, submitted = true, spoolJobId = id,
                requestId = request.RequestId, quantity = request.Payload.Items.Sum(x => x.Quantity), duplicate,
                pages = request.Payload.Design.IsA4 ? PromotionA4Layout.PageCount(request.Payload.Items) : (int?)null };
        }
        finally { gate.Release(); }
    }
    private static async Task Write(string file, Entry entry)
    {
        string temporary = file + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(LabelJson.Write(entry));
            await stream.WriteAsync(bytes); stream.Flush(true);
        }
        File.Move(temporary, file, true);
    }
}
