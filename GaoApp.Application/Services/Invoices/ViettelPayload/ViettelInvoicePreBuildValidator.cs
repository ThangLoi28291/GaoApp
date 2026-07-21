using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelInvoicePreBuildValidator
{
    public static Result<ViettelInvoicePreBuildValidation> Validate(InvoiceHeadDto invoice)
    {
        if (invoice == null)
        {
            return Result<ViettelInvoicePreBuildValidation>.Failure(
                Error.Validation(
                    "Invoice.NotFound",
                    "Không tìm thấy hóa đơn."));
        }

        var isCorrectionInvoice =
            invoice.OriginalInvoiceHeadId.HasValue ||
            invoice.CorrectionType.HasValue;

        var isAdjustmentInfoInvoice =
            invoice.CorrectionType == InvoiceCorrectionType.AdjustmentInfo;

        var activeDetails = (invoice.Details ?? new List<InvoiceDetailDto>())
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.ItemName) &&
                (
                    x.Quantity != 0 ||
                    isAdjustmentInfoInvoice
                ))
            .OrderBy(x => x.Id)
            .ToList();

        if (!activeDetails.Any())
        {
            return Result<ViettelInvoicePreBuildValidation>.Failure(
                Error.Validation(
                    "Invoice.NoDetails",
                    "Hóa đơn chưa có dòng chi tiết để build JSON Viettel."));
        }

        var transactionUuid = (invoice.TransactionUuid ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(transactionUuid))
        {
            return Result<ViettelInvoicePreBuildValidation>.Failure(
                Error.Validation(
                    "Invoice.TransactionUuidMissing",
                    "InvoiceHead chưa có TransactionUuid. Cần cập nhật dữ liệu cũ hoặc tạo lại hóa đơn mới."));
        }

        if (transactionUuid.Length < 10 || transactionUuid.Length > 36)
        {
            return Result<ViettelInvoicePreBuildValidation>.Failure(
                Error.Validation(
                    "Invoice.TransactionUuidInvalid",
                    "TransactionUuid phải có độ dài từ 10 đến 36 ký tự."));
        }

        if (!isCorrectionInvoice && invoice.GrandTotal <= 0)
        {
            return Result<ViettelInvoicePreBuildValidation>.Failure(
                Error.Validation(
                    "Invoice.TotalInvalid",
                    "Tổng tiền hóa đơn gốc phải lớn hơn 0."));
        }

        if (invoice.CorrectionType == InvoiceCorrectionType.Replacement &&
            invoice.GrandTotal <= 0)
        {
            return Result<ViettelInvoicePreBuildValidation>.Failure(
                Error.Validation(
                    "Invoice.ReplacementTotalInvalid",
                    "Tổng tiền hóa đơn thay thế phải lớn hơn 0."));
        }

        if (invoice.CorrectionType == InvoiceCorrectionType.AdjustmentAmount &&
            invoice.GrandTotal == 0)
        {
            return Result<ViettelInvoicePreBuildValidation>.Failure(
                Error.Validation(
                    "Invoice.AdjustmentAmountTotalInvalid",
                    "Tổng tiền hóa đơn điều chỉnh tiền không được bằng 0."));
        }

        return Result<ViettelInvoicePreBuildValidation>.Success(
            new ViettelInvoicePreBuildValidation
            {
                IsCorrectionInvoice = isCorrectionInvoice,
                IsAdjustmentInfoInvoice = isAdjustmentInfoInvoice,
                ActiveDetails = activeDetails,
                TransactionUuid = transactionUuid
            });
    }
}

internal class ViettelInvoicePreBuildValidation
{
    public bool IsCorrectionInvoice { get; set; }

    public bool IsAdjustmentInfoInvoice { get; set; }

    public List<InvoiceDetailDto> ActiveDetails { get; set; } = new();

    public string TransactionUuid { get; set; } = string.Empty;
}