using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public partial class InvoiceCommandService
{
    public async Task<Result<InvoiceHeadDto>> UpdateRevenueTaxAsync(UpdateInvoiceRevenueTaxRequest request, CancellationToken ct = default)
    {
        if (request.InvoiceHeadId <= 0)
            return Result<InvoiceHeadDto>.Failure(Error.Validation("Invoice.InvalidInvoiceHeadId", "Hóa đơn không hợp lệ."));
        await using var mutation = await _invoiceRepository.BeginDetailMutationAsync(request.InvoiceHeadId, ct);
        var head = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(request.InvoiceHeadId, ct);
        if (head == null) return Result<InvoiceHeadDto>.Failure(Error.NotFound("Không tìm thấy hóa đơn."));
        var error = await ValidateDetailMutationAsync(head, ct);
        if (error != null) return Result<InvoiceHeadDto>.Failure(error);
        var expected = new byte[8];
        if (!Convert.TryFromBase64String(request.ExpectedVersion ?? "", expected, out var length) || length != 8 ||
            head.RowVersion == null || !expected.AsSpan().SequenceEqual(head.RowVersion))
            return Result<InvoiceHeadDto>.Failure(Error.Conflict("Hóa đơn đã thay đổi. Tải lại trang để kiểm tra trước khi lưu giảm thuế."));
        var order = head.OrderId.HasValue ? await _invoiceRepository.GetOrderForInvoiceAsync(head.OrderId.Value, ct) : null;
        if (head.IsAutoInvoiceGroup || (order?.InvoiceIssuanceRoute == InvoiceIssuanceRoute.Automatic && head.CorrectionType != InvoiceCorrectionType.Replacement))
            return Result<InvoiceHeadDto>.Failure(Error.Validation("Invoice.RevenueTaxManualOnly", "Lựa chọn giảm thuế này dành cho hóa đơn phát hành thủ công."));
        var percentage = request.Enabled ? request.Percentage : null;
        if (request.Enabled && !InvoiceRevenueTaxPolicy.IsValidPercentage(percentage))
            return Result<InvoiceHeadDto>.Failure(Error.Validation("Invoice.RevenueTaxPercentageInvalid", "Chọn tỷ lệ theo doanh thu 1%, 2%, 3% hoặc 5%."));
        if (percentage.HasValue)
        {
            var current = LegacyInvoiceIssueConfiguration.UseCurrent(head);
            var setting = await _invoiceProviderSettingRepository.GetForInvoiceAsync(head.StoreId, current ? null : head.InvoiceProviderSettingId, ct);
            if (setting == null || setting.StoreId != head.StoreId || !setting.IsActive || setting.IsDeleted ||
                !string.Equals(setting.ProviderCode, "VIETTEL", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(string.IsNullOrWhiteSpace(setting.CurrencyCode) ? "VND" : setting.CurrencyCode, "VND", StringComparison.OrdinalIgnoreCase))
                return Result<InvoiceHeadDto>.Failure(Error.Validation("Invoice.RevenueTaxProviderRequired", "Cần cấu hình Viettel đang hoạt động, tiền tệ VND để áp dụng giảm thuế."));
            var type = current || string.IsNullOrWhiteSpace(head.InvoiceType) ? setting.InvoiceType : head.InvoiceType;
            error = InvoiceRevenueTaxPolicy.Validate(percentage, type, head.CorrectionType, VietnamTime.Today,
                head.Details.Any(x => !x.IsDeleted && (x.VatRate != 0 || x.VatAmount != 0)));
            if (error != null) return Result<InvoiceHeadDto>.Failure(error);
        }
        head.RevenueTaxPercentage = percentage;
        InvoiceAmountCalculator.RecalculateHead(head);
        head.LastIssuanceRelevantChangeAtUtc = DateTime.UtcNow;
        await _invoiceRepository.SaveChangesAsync(ct);
        await mutation.CommitAsync(ct);
        return Result<InvoiceHeadDto>.Success(InvoiceDtoMapper.ToDetailDto(head));
    }
}
