using GaoApp.Application.DTOs.POS;
using GaoApp.Application.DTOs.POSPaymentQrs;
using GaoApp.Application.Interfaces.Repositories.POSPaymentQrs;
using GaoApp.Application.Interfaces.Repositories.StoreBankAccounts;
using GaoApp.Application.Interfaces.Services.POSPaymentQrs;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.POSPaymentQrs;

public class POSPaymentQrService : IPOSPaymentQrService
{
    private readonly IStoreBankAccountRepository _bankRepository;
    private readonly IPOSPaymentQrRequestRepository _qrRepository;
    private readonly ILocalVietQrGenerator _localVietQrGenerator;

    public POSPaymentQrService(
        IStoreBankAccountRepository bankRepository,
        IPOSPaymentQrRequestRepository qrRepository,
        ILocalVietQrGenerator localVietQrGenerator)
    {
        _bankRepository = bankRepository;
        _qrRepository = qrRepository;
        _localVietQrGenerator = localVietQrGenerator;
    }

    public async Task<POSPaymentQrDto> CreateLocalManualQrAsync(
     OrderDraftDto currentDraft,
     CreatePOSPaymentQrRequest request,
     CancellationToken ct = default)
    {
        if (currentDraft == null)
            throw new InvalidOperationException("Chưa có giỏ hiện tại.");

        if (currentDraft.OrderId <= 0)
            throw new InvalidOperationException("Giỏ hiện tại không hợp lệ.");

        var amount = request.Amount.GetValueOrDefault();

        if (amount <= 0)
        {
            amount = currentDraft.BalanceDue;
        }

        if (amount <= 0)
            throw new InvalidOperationException("Đơn hàng đã đủ tiền, không cần tạo QR.");
        if (amount != decimal.Truncate(amount) || amount >= 10000000000000000m)
            throw new InvalidOperationException("Số tiền QR phải là số đồng nguyên dương, nhỏ hơn 10.000.000.000.000.000 đồng.");

        var bank = await _bankRepository.GetDefaultActiveAsync(ct);

        if (bank == null)
            throw new InvalidOperationException("Chưa cấu hình ngân hàng mặc định cho POS.");

        if (!bank.IsActive)
            throw new InvalidOperationException("Tài khoản ngân hàng đang tắt.");

        if (bank.QrRenderMode != BankQrRenderMode.LocalEmvQr)
            throw new InvalidOperationException("Ngân hàng này chưa cấu hình LocalEmvQr.");

        if (bank.ConfirmMode != BankQrConfirmMode.Manual)
            throw new InvalidOperationException("Ngân hàng này không phải chế độ xác nhận thủ công.");

        var requestCode = $"QR-{currentDraft.OrderId}-{Guid.NewGuid():N}";
        var content = BuildContent(bank.NoteTemplate, currentDraft.OrderId, requestCode);

        var raw = _localVietQrGenerator.BuildPayload(
            bank.VietQrBankBin ?? string.Empty,
            bank.AccountNumber,
            amount,
            content);

        var dataUrl = _localVietQrGenerator.GeneratePngDataUrl(raw);

        var entity = new PosPaymentQrRequest
        {
            OrderId = currentDraft.OrderId,
            BankAccountId = bank.Id,
            Amount = amount,
            ClientRequestId = request.ClientRequestId,
            Content = content,
            RequestCode = requestCode,
            QrRenderMode = BankQrRenderMode.LocalEmvQr,
            ConfirmMode = BankQrConfirmMode.Manual,
            Status = PosPaymentQrStatus.Pending,
            QrRawText = raw,
            QrDataUrl = dataUrl,
            ExpireAtUtc = DateTime.UtcNow.AddMinutes(10)
        };

        await _qrRepository.AddAsync(entity, ct);
        await _qrRepository.SaveChangesAsync(ct);

        return new POSPaymentQrDto
        {
            Id = entity.Id,
            OrderId = entity.OrderId,
            BankAccountId = bank.Id,
            BankCode = bank.BankCode,
            BankName = bank.BankName,
            AccountNumber = bank.AccountNumber,
            AccountName = bank.AccountName,
            Amount = amount,
            Content = content,
            RequestCode = requestCode,
            Status = entity.Status,
            QrDataUrl = dataUrl,
            QrRawText = raw,
            ExpireAtUtc = entity.ExpireAtUtc
        };
    }

    private static string BuildContent(
        string? template,
        int orderId,
        string requestCode)
    {
        var value = string.IsNullOrWhiteSpace(template)
            ? "POS-{OrderId}-{QrRequestId}"
            : template.Trim();

        return value
            .Replace("{OrderId}", orderId.ToString())
            .Replace("{QrRequestId}", requestCode)
            .Replace("{RequestCode}", requestCode);
    }

    public async Task MarkManualConfirmedAsync(
    int qrRequestId,
    int? userId,
    CancellationToken ct = default)
    {
        var entity = await _qrRepository.GetByIdAsync(qrRequestId, ct);

        if (entity == null)
            throw new InvalidOperationException("Không tìm thấy QR thanh toán.");

        if (entity.ConfirmMode != BankQrConfirmMode.Manual)
            throw new InvalidOperationException("QR tự động phải được ngân hàng xác nhận.");

        if (entity.Status == PosPaymentQrStatus.ManualConfirmed ||
            entity.Status == PosPaymentQrStatus.Paid)
        {
            return;
        }

        if (entity.Status == PosPaymentQrStatus.Cancelled)
            throw new InvalidOperationException("QR này đã bị hủy.");

        entity.Status = PosPaymentQrStatus.ManualConfirmed;
        entity.PaidAtUtc = DateTime.UtcNow;
        entity.ManualConfirmedAtUtc = DateTime.UtcNow;
        entity.ManualConfirmedByUserId = userId;

        await _qrRepository.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(
        int qrRequestId,
        CancellationToken ct = default)
    {
        var entity = await _qrRepository.GetByIdAsync(qrRequestId, ct);

        if (entity == null)
            throw new InvalidOperationException("Không tìm thấy QR thanh toán.");

        if (entity.ConfirmMode != BankQrConfirmMode.Manual)
            throw new InvalidOperationException("QR tự động phải được hủy qua ngân hàng.");

        if (entity.Status == PosPaymentQrStatus.ManualConfirmed ||
            entity.Status == PosPaymentQrStatus.Paid)
        {
            throw new InvalidOperationException("QR đã xác nhận nhận tiền, không thể hủy.");
        }

        entity.Status = PosPaymentQrStatus.Cancelled;

        await _qrRepository.SaveChangesAsync(ct);
    }
    public async Task CancelByContentAsync(
    string content,
    CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var entity = await _qrRepository.GetLatestByContentAsync(content.Trim(), ct);

        if (entity == null)
        {
            return;
        }

        // Nếu QR đã bị hủy rồi thì bỏ qua
        if (entity.ConfirmMode != BankQrConfirmMode.Manual)
            throw new InvalidOperationException("QR tự động phải được xử lý qua luồng thanh toán ACB.");

        if (entity.Status == PosPaymentQrStatus.Cancelled)
        {
            return;
        }

        // Khi payment bị xóa khỏi đơn, QR liên quan phải không còn là đã nhận tiền nữa
        entity.Status = PosPaymentQrStatus.Cancelled;

        await _qrRepository.SaveChangesAsync(ct);
    }
}
