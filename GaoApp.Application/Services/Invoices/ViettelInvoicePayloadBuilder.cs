using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaoApp.Application.Services.Invoices;

public class ViettelInvoicePayloadBuilder : IViettelInvoicePayloadBuilder
{
    private readonly IInvoiceService _invoiceService;
    private readonly IInvoiceProviderSettingRepository _settingRepository;
    private readonly IInvoiceIntegrationLogRepository _logRepository;

    public ViettelInvoicePayloadBuilder(
       IInvoiceService invoiceService,
       IInvoiceProviderSettingRepository settingRepository,
       IInvoiceIntegrationLogRepository logRepository)
    {
        _invoiceService = invoiceService;
        _settingRepository = settingRepository;
        _logRepository = logRepository;
    }

    public async Task<Result<ViettelInvoicePayloadResultDto>> BuildAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(
                Error.Validation("Invoice.InvalidInvoiceHeadId", "InvoiceHeadId không hợp lệ."));
        }

        var invoiceResult = await _invoiceService.GetInvoiceDetailAsync(invoiceHeadId, ct);

        if (!invoiceResult.IsSuccess)
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(invoiceResult.Error!);
        }

        var invoice = invoiceResult.Value;

        var setting = await _settingRepository.GetActiveViettelAsync(ct);

        if (setting == null)
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(
                Error.Validation(
                    "InvoiceProvider.NotConfigured",
                    "Chưa có cấu hình Viettel đang dùng."));
        }

        var warnings = new List<string>();

        var activeDetails = invoice.Details
            .Where(x => x.Quantity > 0 && !string.IsNullOrWhiteSpace(x.ItemName))
            .OrderBy(x => x.Id)
            .ToList();

        if (!activeDetails.Any())
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(
                Error.Validation(
                    "Invoice.NoDetails",
                    "Hóa đơn chưa có dòng chi tiết để build JSON Viettel."));
        }

        var transactionUuid = invoice.TransactionUuid;

        if (string.IsNullOrWhiteSpace(transactionUuid))
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(
                Error.Validation(
                    "Invoice.TransactionUuidMissing",
                    "InvoiceHead chưa có TransactionUuid. Cần cập nhật dữ liệu cũ hoặc tạo lại hóa đơn mới."));
        }

        if (transactionUuid.Length < 10 || transactionUuid.Length > 36)
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(
                Error.Validation(
                    "Invoice.TransactionUuidInvalid",
                    "TransactionUuid phải có độ dài từ 10 đến 36 ký tự."));
        }

        if (invoice.GrandTotal <= 0)
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(
                Error.Validation(
                    "Invoice.TotalInvalid",
                    "Tổng tiền hóa đơn phải lớn hơn 0."));
        }

        var itemInfo = BuildItemInfo(activeDetails, warnings);

        var totalAmountWithoutTax = RoundVnd(itemInfo.Sum(x => x.ItemTotalAmountWithoutTax));
        var totalTaxAmount = RoundVnd(itemInfo.Sum(x => x.TaxAmount));
        var totalAmountWithTax = RoundVnd(itemInfo.Sum(x => x.ItemTotalAmountWithTax));

        var taxBreakdowns = itemInfo
            .GroupBy(x => x.TaxPercentage)
            .OrderBy(x => x.Key)
            .Select(g => new ViettelTaxBreakdownDto
            {
                TaxPercentage = g.Key,
                TaxableAmount = RoundVnd(g.Sum(x => x.ItemTotalAmountWithoutTax)),
                TaxAmount = RoundVnd(g.Sum(x => x.TaxAmount))
            })
            .ToList();

        var buyerName = string.IsNullOrWhiteSpace(invoice.BuyerName)
            ? "Người mua không lấy hoá đơn"
            : invoice.BuyerName.Trim();

        var invoiceNote = NormalizeInvoiceNote(invoice.Note);

        var metadata = new List<ViettelMetadataDto>();

        if (!string.IsNullOrWhiteSpace(invoiceNote))
        {
            metadata.Add(new ViettelMetadataDto
            {
                KeyTag = "invoiceNote",
                ValueType = "text",
                Value = invoiceNote
            });
        }

        var payload = new ViettelInvoicePayloadDto
        {
            GeneralInvoiceInfo = new ViettelGeneralInvoiceInfoDto
            {
                InvoiceType = string.IsNullOrWhiteSpace(setting.InvoiceType) ? "1" : setting.InvoiceType.Trim(),
                TemplateCode = setting.TemplateCode.Trim(),
                InvoiceSeries = setting.InvoiceSeries.Trim(),
                CurrencyCode = string.IsNullOrWhiteSpace(setting.CurrencyCode) ? "VND" : setting.CurrencyCode.Trim(),
                AdjustmentType = "1",
                PaymentStatus = setting.DefaultPaymentStatus,
                CusGetInvoiceRight = setting.CusGetInvoiceRight,
                TransactionUuid = transactionUuid,
                InvoiceIssuedDate = null,
                InvoiceNote = invoiceNote,
                Validation = null
            },

            BuyerInfo = new ViettelBuyerInfoDto
            {
                BuyerName = buyerName,
                BuyerLegalName = null,
                BuyerTaxCode = string.IsNullOrWhiteSpace(invoice.BuyerTaxCode) ? null : invoice.BuyerTaxCode.Trim(),
                BuyerAddressLine = string.IsNullOrWhiteSpace(invoice.BuyerAddress) ? null : invoice.BuyerAddress.Trim()
            },

            SellerInfo = new { },

            Payments = new List<ViettelPaymentDto>
            {
                new ViettelPaymentDto
                {
                    PaymentMethodName = string.IsNullOrWhiteSpace(setting.PaymentMethodName)
                        ? "TM"
                        : setting.PaymentMethodName.Trim()
                }
            },

            ItemInfo = itemInfo,

            Metadata = metadata,

            SummarizeInfo = new ViettelSummarizeInfoDto
            {
                SumOfTotalLineAmountWithoutTax = totalAmountWithoutTax,
                TotalAmountWithoutTax = totalAmountWithoutTax,
                TotalTaxAmount = totalTaxAmount,
                TotalAmountWithTax = totalAmountWithTax,
                TotalAmountWithTaxInWords = VietnameseMoneyText.ReadMoney(totalAmountWithTax),
                DiscountAmount = 0,
                SettlementDiscountAmount = 0
            },

            TaxBreakdowns = taxBreakdowns
        };

        if (invoice.SubTotal != totalAmountWithoutTax)
        {
            warnings.Add(
                $"SubTotal trên InvoiceHead ({invoice.SubTotal:N0}) khác tổng dòng build JSON ({totalAmountWithoutTax:N0}).");
        }

        if (invoice.VatAmount != totalTaxAmount)
        {
            warnings.Add(
                $"VatAmount trên InvoiceHead ({invoice.VatAmount:N0}) khác tổng VAT build JSON ({totalTaxAmount:N0}).");
        }

        if (invoice.GrandTotal != totalAmountWithTax)
        {
            warnings.Add(
                $"GrandTotal trên InvoiceHead ({invoice.GrandTotal:N0}) khác tổng tiền build JSON ({totalAmountWithTax:N0}).");
        }

        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

        var latestLogs = await _logRepository.GetLatestByInvoiceHeadAsync(
    invoiceHeadId,
    20,
    ct);

        var isIssued = IsIssuedLike(invoice);
        var requiresUuidSyncBeforeIssue = IsProcessingOrUnclear(invoice);

        var issueBlockReason = GetIssueBlockReason(
            invoice,
            isIssued,
            requiresUuidSyncBeforeIssue);

        var canIssue =
            !isIssued &&
            !requiresUuidSyncBeforeIssue &&
            invoice.GrandTotal > 0 &&
            invoice.Details.Any(x => x.Quantity > 0);

        var hasOfficialPdf = !string.IsNullOrWhiteSpace(invoice.PdfFilePath);
        var hasOfficialZip = !string.IsNullOrWhiteSpace(invoice.ZipFilePath);
        var canSendEmail = isIssued;
        var result = new ViettelInvoicePayloadResultDto
        {
            InvoiceHeadId = invoice.Id,
            OrderId = invoice.OrderId,
            OrderNumber = invoice.InvoiceNumber,
            SupplierTaxCode = setting.SupplierTaxCode,
            TemplateCode = setting.TemplateCode,
            InvoiceSeries = setting.InvoiceSeries,
            TransactionUuid = transactionUuid,
            TotalAmountWithoutTax = totalAmountWithoutTax,
            TotalTaxAmount = totalTaxAmount,
            TotalAmountWithTax = totalAmountWithTax,
            Warnings = warnings,
            Payload = payload,
            Json = json,
            ProviderStatus = (int)invoice.ProviderStatus,
            ProviderStatusName = GetProviderStatusName(invoice.ProviderStatus),
            ProviderStatusBadgeClass = GetProviderStatusBadgeClass(invoice.ProviderStatus),
            ProviderInvoiceNo = invoice.ProviderInvoiceNo,
            LastErrorCode = invoice.LastErrorCode,
            LastErrorMessage = invoice.LastErrorMessage,
            IssuedAtUtc = invoice.IssuedAtUtc,
            LastSyncedAtUtc = invoice.LastSyncedAtUtc,
            PdfFilePath = invoice.PdfFilePath,
            ZipFilePath = invoice.ZipFilePath,
           
            CanSendEmail = canSendEmail,
            IsIssued = isIssued,
            HasOfficialPdf = hasOfficialPdf,
            HasOfficialZip = hasOfficialZip,

            CanPreviewDraft = !isIssued && invoice.ProviderStatus != InvoiceProviderStatus.Issuing,
            CanIssue = canIssue,
            CanSyncByUuid = !string.IsNullOrWhiteSpace(invoice.TransactionUuid),
            CanDownloadOfficialFiles = isIssued,
            CanViewSavedPdf = hasOfficialPdf,
            CanDownloadSavedZip = hasOfficialZip,
            RequiresUuidSyncBeforeIssue = requiresUuidSyncBeforeIssue,
            IssueBlockReason = issueBlockReason,
            Logs = latestLogs.Select(x => new InvoiceIntegrationLogItemDto
            {
                Id = x.Id,
                ActionName = GetActionName(x.ActionType),
                IsSuccess = x.IsSuccess,
                ErrorCode = x.ErrorCode,
                ErrorMessage = x.ErrorMessage,
                RequestUrl = x.RequestUrl,
                StartedAtUtc = x.StartedAtUtc,
                FinishedAtUtc = x.FinishedAtUtc,
                DurationMs = x.DurationMs
            }).ToList()
        };

        return Result<ViettelInvoicePayloadResultDto>.Success(result);
    }
    private static string GetActionName(InvoiceIntegrationActionType actionType)
    {
        return actionType switch
        {
            InvoiceIntegrationActionType.PreviewDraft => "Preview PDF nháp",
            InvoiceIntegrationActionType.IssueInvoice => "Phát hành Viettel",
            InvoiceIntegrationActionType.SearchByTransactionUuid => "Tra cứu UUID",
            InvoiceIntegrationActionType.DownloadPdf => "Tải PDF chính thức",
            InvoiceIntegrationActionType.DownloadZip => "Tải ZIP/XML",
            InvoiceIntegrationActionType.SendEmail => "Gửi email",
            _ => actionType.ToString()
        };
    }
    private static bool IsIssuedLike(InvoiceHeadDto invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo))
            return true;

        return invoice.ProviderStatus is
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent;
    }

    private static bool IsProcessingOrUnclear(InvoiceHeadDto invoice)
    {
        if (invoice.ProviderStatus == InvoiceProviderStatus.Issuing)
            return true;

        if (invoice.ProviderStatus == InvoiceProviderStatus.IssuedWaitingNumber)
            return true;

        if (invoice.ProviderStatus != InvoiceProviderStatus.IssueFailed)
            return false;

        var code = invoice.LastErrorCode ?? string.Empty;
        var message = invoice.LastErrorMessage ?? string.Empty;

        if (code.Equals("TIMEOUT", StringComparison.OrdinalIgnoreCase))
            return true;

        if (code.StartsWith("HTTP_5", StringComparison.OrdinalIgnoreCase))
            return true;

        if (code.Equals("HTTP_500", StringComparison.OrdinalIgnoreCase))
            return true;

        if (code.Equals("VIETTEL_SERVER_500", StringComparison.OrdinalIgnoreCase))
            return true;

        if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return true;

        if (message.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string GetIssueBlockReason(
        InvoiceHeadDto invoice,
        bool isIssued,
        bool requiresUuidSyncBeforeIssue)
    {
        if (isIssued)
        {
            return string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo)
                ? "Hóa đơn đã phát hành Viettel."
                : $"Hóa đơn đã phát hành Viettel. Số hóa đơn: {invoice.ProviderInvoiceNo}.";
        }

        if (requiresUuidSyncBeforeIssue)
        {
            return "Lần phát hành trước chưa rõ kết quả. Cần bấm Tra cứu UUID trước khi phát hành lại.";
        }

        if (invoice.GrandTotal <= 0)
        {
            return "Không được phát hành hóa đơn có tổng tiền bằng 0.";
        }

        if (invoice.Details == null || !invoice.Details.Any(x => x.Quantity > 0))
        {
            return "Hóa đơn chưa có dòng chi tiết.";
        }

        return string.Empty;
    }

    private static string GetProviderStatusName(InvoiceProviderStatus status)
    {
        return status switch
        {
            InvoiceProviderStatus.LocalDraft => "Chưa phát hành",
            InvoiceProviderStatus.ReadyToIssue => "Sẵn sàng phát hành",
            InvoiceProviderStatus.Previewed => "Đã preview nháp",
            InvoiceProviderStatus.DraftSent => "Đã gửi nháp",
            InvoiceProviderStatus.Issuing => "Đang phát hành",
            InvoiceProviderStatus.Issued => "Đã phát hành",
            InvoiceProviderStatus.IssuedWaitingNumber => "Chờ tra cứu số HĐ",
            InvoiceProviderStatus.IssueFailed => "Phát hành lỗi",
            InvoiceProviderStatus.Cancelled => "Đã hủy",
            InvoiceProviderStatus.PdfDownloaded => "Đã tải PDF",
            InvoiceProviderStatus.ZipDownloaded => "Đã tải ZIP/XML",
            InvoiceProviderStatus.EmailSent => "Đã gửi email",
            _ => status.ToString()
        };
    }

    private static string GetProviderStatusBadgeClass(InvoiceProviderStatus status)
    {
        return status switch
        {
            InvoiceProviderStatus.LocalDraft => "bg-secondary",
            InvoiceProviderStatus.ReadyToIssue => "bg-info",
            InvoiceProviderStatus.Previewed => "bg-info",
            InvoiceProviderStatus.DraftSent => "bg-info",
            InvoiceProviderStatus.Issuing => "bg-warning text-dark",
            InvoiceProviderStatus.Issued => "bg-success",
            InvoiceProviderStatus.IssuedWaitingNumber => "bg-warning text-dark",
            InvoiceProviderStatus.IssueFailed => "bg-danger",
            InvoiceProviderStatus.Cancelled => "bg-dark",
            InvoiceProviderStatus.PdfDownloaded => "bg-success",
            InvoiceProviderStatus.ZipDownloaded => "bg-success",
            InvoiceProviderStatus.EmailSent => "bg-success",
            _ => "bg-secondary"
        };
    }

    private static List<ViettelItemInfoDto> BuildItemInfo(
        List<InvoiceDetailDto> details,
        List<string> warnings)
    {
        var result = new List<ViettelItemInfoDto>();

        var lineNo = 1;

        foreach (var detail in details)
        {
            var quantity = detail.Quantity;
            var unitPrice = RoundVnd(detail.UnitPrice);
            var amount = RoundVnd(detail.Amount);
            var taxRate = detail.VatRate;
            var taxAmount = RoundVnd(detail.VatAmount);
            var totalAmount = RoundVnd(detail.TotalAmount);

            if (amount != RoundVnd(quantity * unitPrice))
            {
                warnings.Add(
                    $"Dòng #{detail.Id}: Amount ({amount:N0}) khác Quantity x UnitPrice ({RoundVnd(quantity * unitPrice):N0}).");
            }

            if (totalAmount != amount + taxAmount)
            {
                warnings.Add(
                    $"Dòng #{detail.Id}: TotalAmount ({totalAmount:N0}) khác Amount + VAT ({(amount + taxAmount):N0}).");
            }

            result.Add(new ViettelItemInfoDto
            {
                LineNumber = lineNo++,
                Selection = 1,
                ItemCode = detail.ProductVariantId.HasValue ? detail.ProductVariantId.Value.ToString() : null,
                ItemName = TrimMax(detail.ItemName, 500),
                UnitName = string.IsNullOrWhiteSpace(detail.UnitName) ? null : TrimMax(detail.UnitName, 300),
                UnitPrice = unitPrice,
                Quantity = quantity,
                ItemTotalAmountWithoutTax = amount,
                ItemTotalAmountWithTax = totalAmount,
                ItemTotalAmountAfterDiscount = amount,
                TaxPercentage = taxRate,
                TaxAmount = taxAmount,
                Discount = 0,
                ItemDiscount = 0,
                ItemNote = string.IsNullOrWhiteSpace(detail.Note) ? null : TrimMax(detail.Note, 500),
                BatchNo = "",
                ExpDate = ""
            });
        }

        return result;
    }

    private static decimal RoundVnd(decimal value)
    {
        return Math.Round(value, 0, MidpointRounding.AwayFromZero);
    }

    private static string TrimMax(string value, int maxLength)
    {
        value = value.Trim();

        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
    private static string? NormalizeInvoiceNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return null;

        note = note.Trim();

        // Ghi chú nội bộ hệ thống, không gửi lên Viettel.
        if (note.Contains("Tạo khung hóa đơn bán ra từ POS order", StringComparison.OrdinalIgnoreCase))
            return null;

        if (note.Contains("Chưa sinh dòng chi tiết", StringComparison.OrdinalIgnoreCase))
            return null;

        return TrimMax(note, 500);
    }
}

public static class VietnameseMoneyText
{
    private static readonly string[] Digits =
    {
        "không", "một", "hai", "ba", "bốn",
        "năm", "sáu", "bảy", "tám", "chín"
    };

    private static readonly string[] Units =
    {
        "", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ"
    };

    public static string ReadMoney(decimal amount)
    {
        var rounded = (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);

        if (rounded == 0)
            return "Không đồng";

        var words = ReadNumber(rounded);

        return Capitalize(words + " đồng chẵn");
    }

    private static string ReadNumber(long number)
    {
        var groups = new List<int>();

        while (number > 0)
        {
            groups.Add((int)(number % 1000));
            number /= 1000;
        }

        var parts = new List<string>();

        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];

            if (group == 0)
                continue;

            var full = i < groups.Count - 1;
            var text = ReadThreeDigits(group, full);

            if (!string.IsNullOrWhiteSpace(Units[i]))
                text += " " + Units[i];

            parts.Add(text);
        }

        return string.Join(" ", parts).Replace("  ", " ").Trim();
    }

    private static string ReadThreeDigits(int number, bool full)
    {
        var hundred = number / 100;
        var ten = (number % 100) / 10;
        var unit = number % 10;

        var parts = new List<string>();

        if (hundred > 0 || full)
        {
            parts.Add(Digits[hundred]);
            parts.Add("trăm");
        }

        if (ten > 1)
        {
            parts.Add(Digits[ten]);
            parts.Add("mươi");

            if (unit == 1)
                parts.Add("mốt");
            else if (unit == 5)
                parts.Add("lăm");
            else if (unit > 0)
                parts.Add(Digits[unit]);
        }
        else if (ten == 1)
        {
            parts.Add("mười");

            if (unit == 5)
                parts.Add("lăm");
            else if (unit > 0)
                parts.Add(Digits[unit]);
        }
        else if (unit > 0)
        {
            if (hundred > 0 || full)
                parts.Add("lẻ");

            parts.Add(Digits[unit]);
        }

        return string.Join(" ", parts);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return char.ToUpper(value[0]) + value[1..];
    }
   
}