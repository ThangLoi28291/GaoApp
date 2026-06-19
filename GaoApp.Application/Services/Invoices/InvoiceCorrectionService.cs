using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public class InvoiceCorrectionService : IInvoiceCorrectionService
{
    private readonly IInvoiceCorrectionRepository _repository;

    public InvoiceCorrectionService(
        IInvoiceCorrectionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<InvoiceCorrectionCreateInfoDto>> GetCreateInfoAsync(
        int originalInvoiceHeadId,
        InvoiceCorrectionType type,
        CancellationToken ct = default)
    {
        var original = await _repository.GetOriginalInvoiceWithDetailsAsync(
            originalInvoiceHeadId,
            ct);

        if (original == null)
        {
            return Result<InvoiceCorrectionCreateInfoDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn gốc."));
        }

        var validate = ValidateOriginalInvoice(original);

        if (!validate.IsSuccess)
            return Result<InvoiceCorrectionCreateInfoDto>.Failure(validate.Error!);

        var openCase = await _repository.GetOpenCaseByOriginalAsync(
            originalInvoiceHeadId,
            ct);

        if (openCase != null)
        {
            return Result<InvoiceCorrectionCreateInfoDto>.Failure(
                Error.Validation(
                    "InvoiceCorrection.OpenCaseExists",
                    $"Hóa đơn gốc đang có hồ sơ xử lý sai sót chưa hoàn tất. Mã hồ sơ: #{openCase.Id}."));
        }

        var originalNo = GetOriginalInvoiceNo(original);

        return Result<InvoiceCorrectionCreateInfoDto>.Success(
            new InvoiceCorrectionCreateInfoDto
            {
                OriginalInvoiceHeadId = original.Id,
                OriginalInvoiceNo = originalNo,
                ProviderInvoiceNo = original.ProviderInvoiceNo,
                InvoiceDate = original.InvoiceDate,
                IssuedAtUtc = original.IssuedAtUtc,
                BuyerName = original.BuyerName,
                BuyerTaxCode = original.BuyerTaxCode,
                GrandTotal = original.GrandTotal,
                Type = type,
                TypeName = GetTypeName(type),
                DefaultAgreementDocumentNo = $"BBTT-{DateTime.Now:yyyyMMdd}-{original.Id}",
                DefaultAgreementDate = DateTime.Today
            });
    }

    public async Task<Result<CreateInvoiceCorrectionResultDto>> CreateAsync(
        CreateInvoiceCorrectionRequestDto request,
        CancellationToken ct = default)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        var agreementNo = (request.AgreementDocumentNo ?? string.Empty).Trim();

        if (request.OriginalInvoiceHeadId <= 0)
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation("InvoiceCorrection.OriginalInvalid", "Hóa đơn gốc không hợp lệ."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation("InvoiceCorrection.ReasonRequired", "Vui lòng nhập lý do sai sót."));
        }

        if (string.IsNullOrWhiteSpace(agreementNo))
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation("InvoiceCorrection.AgreementRequired", "Vui lòng nhập số văn bản / biên bản thỏa thuận."));
        }

        if (request.AgreementDate == default)
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation("InvoiceCorrection.AgreementDateRequired", "Vui lòng nhập ngày văn bản thỏa thuận."));
        }

        var original = await _repository.GetOriginalInvoiceWithDetailsAsync(
            request.OriginalInvoiceHeadId,
            ct);

        if (original == null)
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn gốc."));
        }

        var validate = ValidateOriginalInvoice(original);

        if (!validate.IsSuccess)
            return Result<CreateInvoiceCorrectionResultDto>.Failure(validate.Error!);

        var openCase = await _repository.GetOpenCaseByOriginalAsync(
            original.Id,
            ct);

        if (openCase != null)
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation(
                    "InvoiceCorrection.OpenCaseExists",
                    $"Hóa đơn gốc đang có hồ sơ xử lý sai sót chưa hoàn tất. Mã hồ sơ: #{openCase.Id}."));
        }

        var originalInvoiceNo = GetOriginalInvoiceNo(original);

        if (string.IsNullOrWhiteSpace(originalInvoiceNo))
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation(
                    "InvoiceCorrection.OriginalInvoiceNoMissing",
                    "Hóa đơn gốc chưa có số hóa đơn Viettel, không thể lập thay thế/điều chỉnh."));
        }

        if (!original.IssuedAtUtc.HasValue)
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation(
                    "InvoiceCorrection.OriginalIssuedDateMissing",
                    "Hóa đơn gốc chưa có ngày phát hành Viettel, không thể lập thay thế/điều chỉnh."));
        }

        var newInvoice = BuildNewCorrectionInvoice(
            original,
            request.Type,
            reason,
            agreementNo,
            request.AgreementDate,
            request.Note);

        var correctionCase = new InvoiceCorrectionCase
        {
            StoreId = original.StoreId,
            OriginalInvoiceHeadId = original.Id,
            NewInvoiceHead = newInvoice,
            Type = request.Type,
            Status = InvoiceCorrectionStatus.Draft,
            Reason = reason,
            AgreementDocumentNo = agreementNo,
            AgreementDateUtc = ToUtcDate(request.AgreementDate),
            Note = request.Note,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _repository.AddInvoiceHeadAsync(newInvoice, ct);
        await _repository.AddCorrectionCaseAsync(correctionCase, ct);
        await _repository.SaveChangesAsync(ct);

        return Result<CreateInvoiceCorrectionResultDto>.Success(
            new CreateInvoiceCorrectionResultDto
            {
                CorrectionCaseId = correctionCase.Id,
                OriginalInvoiceHeadId = original.Id,
                NewInvoiceHeadId = newInvoice.Id,
                Type = request.Type,
                TypeName = GetTypeName(request.Type)
            });
    }

    private static Result<bool> ValidateOriginalInvoice(InvoiceHead original)
    {
        if (original.OriginalInvoiceHeadId.HasValue || original.CorrectionType.HasValue)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceCorrection.OriginalMustBeRoot",
                    "Tạm thời chỉ cho lập thay thế/điều chỉnh từ hóa đơn gốc. Không lập tiếp từ hóa đơn đã là thay thế/điều chỉnh."));
        }

        if (!IsIssuedLike(original))
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceCorrection.OriginalNotIssued",
                    "Chỉ được lập thay thế/điều chỉnh cho hóa đơn đã phát hành Viettel."));
        }

        if (string.IsNullOrWhiteSpace(GetOriginalInvoiceNo(original)))
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceCorrection.OriginalInvoiceNoMissing",
                    "Hóa đơn gốc chưa có số hóa đơn Viettel."));
        }

        if (!original.IssuedAtUtc.HasValue)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceCorrection.OriginalIssuedDateMissing",
                    "Hóa đơn gốc chưa có ngày phát hành Viettel."));
        }

        return Result<bool>.Success(true);
    }

    private static InvoiceHead BuildNewCorrectionInvoice(
        InvoiceHead original,
        InvoiceCorrectionType type,
        string reason,
        string agreementNo,
        DateTime agreementDate,
        string? note)
    {
        var newInvoice = new InvoiceHead
        {
            StoreId = original.StoreId,
            OrderId = original.OrderId,

            InvoiceNumber = null,
            InvoiceDate = DateTime.Today,

            BuyerName = original.BuyerName,
            BuyerTaxCode = original.BuyerTaxCode,
            BuyerAddress = original.BuyerAddress,

            TotalQuantity = 0,
            SubTotal = 0,
            VatAmount = 0,
            GrandTotal = 0,

            Note = BuildInvoiceNote(type, reason, note),

            IsLocked = false,
            LockReason = null,

            ProviderStatus = type == InvoiceCorrectionType.AdjustmentAmount
                ? InvoiceProviderStatus.LocalDraft
                : InvoiceProviderStatus.ReadyToIssue,

            TransactionUuid = Guid.NewGuid().ToString("D"),

            ProviderCode = original.ProviderCode,
            SupplierTaxCode = original.SupplierTaxCode,
            InvoiceType = original.InvoiceType,
            TemplateCode = original.TemplateCode,
            InvoiceSeries = original.InvoiceSeries,

            OriginalInvoiceHeadId = original.Id,
            CorrectionType = type,
            OriginalInvoiceNo = GetOriginalInvoiceNo(original),
            OriginalInvoiceIssuedAtUtc = original.IssuedAtUtc,
            AdjustedNote = TrimMax(reason, 255),
            AdditionalReferenceDesc = TrimMax(agreementNo, 255),
            AdditionalReferenceDateUtc = ToUtcDate(agreementDate)
        };

        if (type == InvoiceCorrectionType.Replacement)
        {
            CopyDetails(original, newInvoice);
            RecalculateTotals(newInvoice);
        }
        else if (type == InvoiceCorrectionType.AdjustmentInfo)
        {
            AddAdjustmentInfoDetail(newInvoice, original, reason);
            RecalculateTotals(newInvoice);
        }

        return newInvoice;
    }
    private static void AddAdjustmentInfoDetail(
    InvoiceHead newInvoice,
    InvoiceHead original,
    string reason)
    {
        var originalInvoiceNo = GetOriginalInvoiceNo(original) ?? $"#{original.Id}";

        var detail = new InvoiceDetail
        {
            StoreId = newInvoice.StoreId,
            InvoiceHeadId = newInvoice.Id,

            OrderLineId = null,
            ProductVariantId = null,
            SourceType = InvoiceDetailSourceType.Manual,

            ItemName = $"Điều chỉnh thông tin hóa đơn {originalInvoiceNo}",
            UnitName = null,

            Quantity = 0,
            UnitPrice = 0,
            Amount = 0,
            VatRate = 0,
            VatAmount = 0,
            TotalAmount = 0,

            Note = string.IsNullOrWhiteSpace(reason)
                ? $"Điều chỉnh thông tin hóa đơn gốc {originalInvoiceNo}."
                : $"Hóa đơn điều chỉnh thông tin. Lý do: {reason.Trim()}"
        };

        newInvoice.Details.Add(detail);
    }

    private static void CopyDetails(
        InvoiceHead original,
        InvoiceHead newInvoice)
    {
        var activeDetails = original.Details
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Id)
            .ToList();

        foreach (var source in activeDetails)
        {
            var target = new InvoiceDetail
            {
                StoreId = newInvoice.StoreId,

                OrderLineId = source.OrderLineId,
                ProductVariantId = source.ProductVariantId,
                SourceType = source.SourceType,

                ItemName = source.ItemName,
                UnitName = source.UnitName,

                Quantity = source.Quantity,
                UnitPrice = source.UnitPrice,
                Amount = source.Amount,
                VatRate = source.VatRate,
                VatAmount = source.VatAmount,
                TotalAmount = source.TotalAmount,

                Note = source.Note
            };

            newInvoice.Details.Add(target);
        }
    }

    private static void RecalculateTotals(InvoiceHead invoice)
    {
        var details = invoice.Details
            .Where(x => !x.IsDeleted)
            .ToList();

        invoice.TotalQuantity = details.Sum(x => x.Quantity);
        invoice.SubTotal = details.Sum(x => x.Amount);
        invoice.VatAmount = details.Sum(x => x.VatAmount);
        invoice.GrandTotal = details.Sum(x => x.TotalAmount);
    }

    private static bool IsIssuedLike(InvoiceHead invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo))
            return true;

        return invoice.ProviderStatus is
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent;
    }

    private static string? GetOriginalInvoiceNo(InvoiceHead original)
    {
        if (!string.IsNullOrWhiteSpace(original.ProviderInvoiceNo))
            return original.ProviderInvoiceNo.Trim();

        if (!string.IsNullOrWhiteSpace(original.InvoiceNumber))
            return original.InvoiceNumber.Trim();

        return null;
    }

    private static string BuildInvoiceNote(
        InvoiceCorrectionType type,
        string reason,
        string? note)
    {
        var title = type switch
        {
            InvoiceCorrectionType.Replacement => "Hóa đơn thay thế",
            InvoiceCorrectionType.AdjustmentAmount => "Hóa đơn điều chỉnh tiền",
            InvoiceCorrectionType.AdjustmentInfo => "Hóa đơn điều chỉnh thông tin",
            _ => "Hóa đơn xử lý sai sót"
        };

        var result = $"{title}. Lý do: {reason}";

        if (!string.IsNullOrWhiteSpace(note))
            result += $". Ghi chú: {note.Trim()}";

        return TrimMax(result, 500);
    }

    private static string GetTypeName(InvoiceCorrectionType type)
    {
        return type switch
        {
            InvoiceCorrectionType.Replacement => "Hóa đơn thay thế",
            InvoiceCorrectionType.AdjustmentAmount => "Hóa đơn điều chỉnh tiền",
            InvoiceCorrectionType.AdjustmentInfo => "Hóa đơn điều chỉnh thông tin",
            _ => type.ToString()
        };
    }

    private static DateTime ToUtcDate(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    }

    private static string TrimMax(string value, int maxLength)
    {
        value = (value ?? string.Empty).Trim();

        if (value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
    public async Task<Result<InvoiceCorrectionHistoryDto>> GetHistoryAsync(
    int invoiceHeadId,
    CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<InvoiceCorrectionHistoryDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var currentInvoice = await _repository.GetInvoiceHeadForHistoryAsync(
            invoiceHeadId,
            ct);

        if (currentInvoice == null)
        {
            return Result<InvoiceCorrectionHistoryDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn."));
        }

        var originalInvoiceHeadId = currentInvoice.OriginalInvoiceHeadId ?? currentInvoice.Id;

        var originalInvoiceNo = currentInvoice.OriginalInvoiceHeadId.HasValue
            ? currentInvoice.OriginalInvoiceNo
            : GetOriginalInvoiceNo(currentInvoice);

        var cases = await _repository.GetCasesByOriginalInvoiceHeadIdAsync(
            originalInvoiceHeadId,
            ct);

        var result = new InvoiceCorrectionHistoryDto
        {
            CurrentInvoiceHeadId = currentInvoice.Id,
            OriginalInvoiceHeadId = originalInvoiceHeadId,
            OriginalInvoiceNo = originalInvoiceNo,
            IsCurrentOriginal = currentInvoice.Id == originalInvoiceHeadId,
            Items = cases
                .Select(MapHistoryItem)
                .ToList()
        };

        return Result<InvoiceCorrectionHistoryDto>.Success(result);
    }

    private static InvoiceCorrectionHistoryItemDto MapHistoryItem(
        InvoiceCorrectionCase correctionCase)
    {
        var newInvoice = correctionCase.NewInvoiceHead;

        return new InvoiceCorrectionHistoryItemDto
        {
            CorrectionCaseId = correctionCase.Id,
            OriginalInvoiceHeadId = correctionCase.OriginalInvoiceHeadId,
            NewInvoiceHeadId = correctionCase.NewInvoiceHeadId,

            NewInvoiceNo = newInvoice?.InvoiceNumber,
            NewProviderInvoiceNo = newInvoice?.ProviderInvoiceNo,

            Type = correctionCase.Type,
            TypeName = GetTypeName(correctionCase.Type),
            TypeBadgeClass = GetTypeBadgeClass(correctionCase.Type),

            Status = correctionCase.Status,
            StatusName = GetStatusName(correctionCase.Status),
            StatusBadgeClass = GetStatusBadgeClass(correctionCase.Status),

            ProviderStatus = newInvoice?.ProviderStatus,
            ProviderStatusName = newInvoice == null
                ? null
                : GetProviderStatusName(newInvoice.ProviderStatus),

            Reason = correctionCase.Reason,
            AgreementDocumentNo = correctionCase.AgreementDocumentNo,
            AgreementDateUtc = correctionCase.AgreementDateUtc,
            CreatedAtUtc = correctionCase.CreatedAtUtc,
            IssuedAtUtc = correctionCase.IssuedAtUtc,

            LastErrorCode = correctionCase.LastErrorCode,
            LastErrorMessage = correctionCase.LastErrorMessage
        };
    }

    private static string GetStatusName(InvoiceCorrectionStatus status)
    {
        return status switch
        {
            InvoiceCorrectionStatus.Draft => "Nháp",
            InvoiceCorrectionStatus.ReadyToIssue => "Sẵn sàng phát hành",
            InvoiceCorrectionStatus.Issuing => "Đang phát hành",
            InvoiceCorrectionStatus.Issued => "Đã phát hành",
            InvoiceCorrectionStatus.Failed => "Lỗi",
            InvoiceCorrectionStatus.Cancelled => "Đã hủy",
            _ => status.ToString()
        };
    }

    private static string GetStatusBadgeClass(InvoiceCorrectionStatus status)
    {
        return status switch
        {
            InvoiceCorrectionStatus.Draft => "bg-secondary",
            InvoiceCorrectionStatus.ReadyToIssue => "bg-info",
            InvoiceCorrectionStatus.Issuing => "bg-warning text-dark",
            InvoiceCorrectionStatus.Issued => "bg-success",
            InvoiceCorrectionStatus.Failed => "bg-danger",
            InvoiceCorrectionStatus.Cancelled => "bg-dark",
            _ => "bg-secondary"
        };
    }

    private static string GetTypeBadgeClass(InvoiceCorrectionType type)
    {
        return type switch
        {
            InvoiceCorrectionType.Replacement => "bg-danger",
            InvoiceCorrectionType.AdjustmentAmount => "bg-warning text-dark",
            InvoiceCorrectionType.AdjustmentInfo => "bg-info",
            _ => "bg-secondary"
        };
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
}