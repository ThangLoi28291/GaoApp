using System.Globalization;
using System.Text.Json;
using GaoApp.Domain.Entities;
using static GaoApp.Web.Services.Acb.AcbProtocol;

namespace GaoApp.Web.Services.Acb;

public sealed record AcbSandboxStep(string Name, bool Succeeded, string Message);
public sealed class AcbSandboxCheckResult
{
    public Guid RunId { get; init; } = Guid.NewGuid();
    public int StoreId { get; init; }
    public string Environment { get; init; } = "Sandbox";
    public string JournalFolder => Environment == "Production" ? "acb-production-check" : "acb-sandbox";
    public string ProviderOrderId { get; init; } = "";
    public string TraceNumber { get; set; } = "";
    public int Amount { get; init; } = 1000;
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public bool CancellationConfirmed { get; set; }
    public List<AcbSandboxStep> Steps { get; } = [];
}

public interface IAcbSandboxJournal
{
    Task SaveAsync(AcbSandboxCheckResult result, CancellationToken ct);
}

public sealed class AcbSandboxFileJournal(IWebHostEnvironment environment) : IAcbSandboxJournal
{
    public async Task SaveAsync(AcbSandboxCheckResult result, CancellationToken ct)
    {
        var directory = System.IO.Path.Combine(environment.ContentRootPath, "App_Data", "Logs", result.JournalFolder);
        Directory.CreateDirectory(directory);
        var file = System.IO.Path.Combine(directory, result.RunId.ToString("N") + ".json");
        var temporary = file + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
        File.Move(temporary, file, overwrite: true);
    }
}

public sealed class AcbSandboxCheck(AcbProtocol protocol, IAcbSandboxJournal journal)
{
    public Task<AcbSandboxCheckResult> RunAsync(StoreAcbSettings settings, int terminalId, CancellationToken ct)
        => RunCoreAsync(settings, terminalId, "Sandbox", ct);

    public Task<AcbSandboxCheckResult> RunProductionAsync(StoreAcbSettings settings, int terminalId, CancellationToken ct)
        => RunCoreAsync(settings, terminalId, "Production", ct);

    private async Task<AcbSandboxCheckResult> RunCoreAsync(StoreAcbSettings settings, int terminalId, string expectedEnvironment, CancellationToken ct)
    {
        var actualEnvironment = ValidateEnvironment(settings.TokenEndpoint, settings.ApiBaseUrl, settings.QrEndpoint);
        if (actualEnvironment != expectedEnvironment)
            throw new InvalidOperationException($"Phép thử này chỉ dùng cho ACB {expectedEnvironment}. Hãy chọn phép thử đúng môi trường đã lưu.");
        if (terminalId <= 0 || string.IsNullOrWhiteSpace(settings.MerchantId) || settings.MerchantId.Length > 30 ||
            string.IsNullOrWhiteSpace(settings.BeneficiaryName) || settings.BeneficiaryName.Length > 30 ||
            string.IsNullOrWhiteSpace(settings.VirtualAccountPrefix))
            throw new InvalidOperationException("Cần chọn máy và lưu đủ thông tin merchant, đầu VA, tên thụ hưởng trước khi thử.");
        var result = new AcbSandboxCheckResult { StoreId = settings.StoreId, Environment = actualEnvironment,
            ProviderOrderId = (actualEnvironment == "Production" ? "TP" : "TS") + Guid.NewGuid().ToString("N")[..11].ToUpperInvariant(), TraceNumber = Guid.NewGuid().ToString() };
        // Keep the recovery reference before making a request that can create a bank QR.
        // This journal contains no credential, token, account number, QR image or bank response body.
        await journal.SaveAsync(result, ct);
        var attemptedCreate = false;
        var step = "Lấy token";
        try
        {
            await protocol.CheckConnectionAsync(settings, ct);
            result.Steps.Add(new(step, true, $"ACB {actualEnvironment} cấp token thành công."));
            step = "Tạo QR thử";
            attemptedCreate = true;
            var created = await protocol.CallAsync(settings, HttpMethod.Post, settings.QrEndpoint, new
            {
                traceNumber = result.TraceNumber, merchantId = settings.MerchantId,
                terminalId = terminalId.ToString(CultureInfo.InvariantCulture), userId = "LOCALTEST",
                orderId = result.ProviderOrderId, virtualAccountPrefix = settings.VirtualAccountPrefix,
                beneficiaryName = settings.BeneficiaryName, amount = result.Amount, description = $"Kiem thu {actualEnvironment} GaoApp"
            }, ct);
            var body = Path(created, "responseBody");
            if (!Text(body, "qrDataUrl").StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(Text(body, "virtualAccount")))
                throw new InvalidOperationException("ACB chưa trả đủ dữ liệu QR.");
            if (!string.IsNullOrWhiteSpace(Text(body, "traceNumber"))) result.TraceNumber = Text(body, "traceNumber");
            result.Steps.Add(new(step, true, $"Đã tạo QR {actualEnvironment} 1.000đ; không hiển thị để thanh toán."));
            step = "Tra cứu QR thử";
            var today = DateTime.UtcNow.AddHours(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var url = settings.ApiBaseUrl.TrimEnd('/') + "/acb/open/payments/qr-payment/v1/retrieve" +
                $"?fromDate={today}&toDate={today}&orderId={Uri.EscapeDataString(result.ProviderOrderId)}&traceNumber={Uri.EscapeDataString(result.TraceNumber)}&page=0&size=20";
            var retrieved = await protocol.CallAsync(settings, HttpMethod.Get, url, null, ct);
            var orders = Path(retrieved, "responseBody", "orders");
            if (orders.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("ACB trả dữ liệu tra cứu không đúng định dạng.");
            result.Steps.Add(new(step, true, orders.GetArrayLength() == 0
                ? "ACB nhận truy vấn thành công; chưa có giao dịch thanh toán, không xác nhận đã nhận tiền."
                : "ACB trả kết quả tra cứu; phép thử không ghi nhận thanh toán hoặc chốt đơn."));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or OperationCanceledException or JsonException)
        {
            result.Steps.Add(new(step, false, SafeError(ex)));
        }
        finally
        {
            if (attemptedCreate)
            {
                // Cleanup still runs if the caller disconnects or an initiate response is lost.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(70));
                try
                {
                    var cancelled = await protocol.CallAsync(settings, HttpMethod.Delete,
                        settings.ApiBaseUrl.TrimEnd('/') + "/acb/open/payments/qr-payment/v1/cancellation",
                        new { traceNumber = result.TraceNumber, orderId = result.ProviderOrderId, amount = result.Amount }, cleanup.Token);
                    if (Text(Path(cancelled, "responseBody"), "status") != "SUCCESS")
                        throw new InvalidOperationException("ACB chưa xác nhận hủy QR thử.");
                    result.CancellationConfirmed = true;
                    result.Steps.Add(new("Hủy QR thử", true, "ACB đã xác nhận hủy QR thử."));
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or OperationCanceledException or JsonException)
                { result.Steps.Add(new("Hủy QR thử", false, SafeError(ex) + " Giữ mã tham chiếu để tra cứu/hủy lại QR thử.")); }
            }
            await journal.SaveAsync(result, CancellationToken.None);
        }
        return result;
    }

    private static string SafeError(Exception error) => error switch
    {
        AcbApiException apiError => apiError.Message,
        OperationCanceledException => "Yêu cầu hết thời gian hoặc đã bị ngắt.",
        HttpRequestException => "Không kết nối được ACB. Kiểm tra mạng và quyền kết nối ACB.",
        JsonException => "Phản hồi ACB không đúng định dạng JSON.",
        _ => "ACB chưa xác nhận bước kiểm thử thành công. Kiểm tra cấu hình và dữ liệu ACB được cấp."
    };
}
