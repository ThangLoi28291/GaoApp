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
                Error.NotFound("Không tìm thấy hóa đơn."));
        }

        var validate = ValidateOriginalInvoice(
            original,
            type);

        if (!validate.IsSuccess)
            return Result<InvoiceCorrectionCreateInfoDto>.Failure(validate.Error!);

        var ruleValidation = await ValidateCorrectionCreationRuleAsync(
            original,
            type,
            ct);

        if (!ruleValidation.IsSuccess)
            return Result<InvoiceCorrectionCreateInfoDto>.Failure(ruleValidation.Error!);

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
                Error.Validation(
                    "InvoiceCorrection.OriginalInvalid",
                    "Hóa đơn không hợp lệ."));
        }

        if (!IsValidCorrectionType(request.Type))
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation(
                    "InvoiceCorrection.InvalidCorrectionType",
                    "Loại xử lý sai sót không hợp lệ."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation(
                    "InvoiceCorrection.ReasonRequired",
                    "Vui lòng nhập lý do sai sót."));
        }

        if (string.IsNullOrWhiteSpace(agreementNo))
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation(
                    "InvoiceCorrection.AgreementRequired",
                    "Vui lòng nhập số văn bản / biên bản thỏa thuận."));
        }

        if (request.AgreementDate == default)
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation(
                    "InvoiceCorrection.AgreementDateRequired",
                    "Vui lòng nhập ngày văn bản thỏa thuận."));
        }

        var original = await _repository.GetOriginalInvoiceWithDetailsAsync(
            request.OriginalInvoiceHeadId,
            ct);

        if (original == null)
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn."));
        }

        var validate = ValidateOriginalInvoice(
            original,
            request.Type);

        if (!validate.IsSuccess)
            return Result<CreateInvoiceCorrectionResultDto>.Failure(validate.Error!);

        var ruleValidation = await ValidateCorrectionCreationRuleAsync(
            original,
            request.Type,
            ct);

        if (!ruleValidation.IsSuccess)
            return Result<CreateInvoiceCorrectionResultDto>.Failure(ruleValidation.Error!);

        var originalInvoiceNo = GetOriginalInvoiceNo(original);

        if (string.IsNullOrWhiteSpace(originalInvoiceNo))
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation(
                    "InvoiceCorrection.OriginalInvoiceNoMissing",
                    "Hóa đơn chưa có số hóa đơn Viettel, không thể lập thay thế/điều chỉnh."));
        }

        if (!original.IssuedAtUtc.HasValue)
        {
            return Result<CreateInvoiceCorrectionResultDto>.Failure(
                Error.Validation(
                    "InvoiceCorrection.OriginalIssuedDateMissing",
                    "Hóa đơn chưa có ngày phát hành Viettel, không thể lập thay thế/điều chỉnh."));
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

    private async Task<Result<bool>> ValidateCorrectionCreationRuleAsync(
        InvoiceHead original,
        InvoiceCorrectionType requestedType,
        CancellationToken ct)
    {
        // 1. Không cho tạo hồ sơ mới nếu còn hồ sơ đang treo.
        // Các trạng thái này phải xử lý tiếp hoặc hủy trước.
        var unfinishedCase = await _repository.GetUnfinishedCaseByOriginalAsync(
            original.Id,
            ct);

        if (unfinishedCase != null)
        {
            var unfinishedInvoice = unfinishedCase.NewInvoiceHead;

            var unfinishedText = unfinishedInvoice == null
                ? $"Case #{unfinishedCase.Id}"
                : !string.IsNullOrWhiteSpace(unfinishedInvoice.ProviderInvoiceNo)
                    ? unfinishedInvoice.ProviderInvoiceNo
                    : $"Invoice #{unfinishedInvoice.Id}";

            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceCorrection.UnfinishedCaseExists",
                    $"Hóa đơn này đang có hồ sơ xử lý sai sót chưa hoàn tất ({unfinishedText}). Vui lòng phát hành tiếp, sửa lỗi hoặc hủy hồ sơ đó trước."));
        }

        // 2. Nếu hóa đơn này đã có hóa đơn thay thế đang hoạt động/đã phát hành,
        // thì hóa đơn này không còn là hóa đơn hiện hành.
        // Muốn xử lý tiếp phải mở hóa đơn thay thế.
        var activeReplacement = await _repository.GetActiveReplacementCaseByOriginalAsync(
            original.Id,
            ct);

        if (activeReplacement != null)
        {
            var replacementInvoice = activeReplacement.NewInvoiceHead;

            var replacementText = replacementInvoice == null
                ? $"Case #{activeReplacement.Id}"
                : !string.IsNullOrWhiteSpace(replacementInvoice.ProviderInvoiceNo)
                    ? replacementInvoice.ProviderInvoiceNo
                    : $"Invoice #{replacementInvoice.Id}";

            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceCorrection.ReplacementAlreadyExists",
                    $"Hóa đơn này đã được thay thế bởi {replacementText}. Vui lòng mở hóa đơn thay thế đó để xử lý sai sót tiếp."));
        }

        // 3. Kiểm tra hình thức xử lý đầu tiên.
        // - Nếu lần đầu đã điều chỉnh thì các lần sau tiếp tục điều chỉnh.
        // - Không chuyển sang thay thế trên chính hóa đơn đó.
        // - Nếu chưa có hồ sơ nào thì được chọn điều chỉnh hoặc thay thế.
        var activeCases = await _repository.GetActiveCasesByOriginalAsync(
            original.Id,
            ct);

        var firstActiveCase = activeCases
            .OrderBy(x => x.Id)
            .FirstOrDefault();

        if (firstActiveCase == null)
        {
            return Result<bool>.Success(true);
        }

        var firstType = firstActiveCase.Type;

        if (firstType == InvoiceCorrectionType.AdjustmentAmount ||
            firstType == InvoiceCorrectionType.AdjustmentInfo)
        {
            if (requestedType == InvoiceCorrectionType.Replacement)
            {
                return Result<bool>.Failure(
                    Error.Validation(
                        "InvoiceCorrection.FirstMethodWasAdjustment",
                        "Hóa đơn này đã xử lý sai sót lần đầu bằng hình thức điều chỉnh. Các lần tiếp theo nên tiếp tục lập hóa đơn điều chỉnh, không chuyển sang thay thế trên cùng hóa đơn."));
            }

            if (requestedType == InvoiceCorrectionType.AdjustmentAmount ||
                requestedType == InvoiceCorrectionType.AdjustmentInfo)
            {
                return Result<bool>.Success(true);
            }
        }

        if (firstType == InvoiceCorrectionType.Replacement)
        {
            // Trường hợp này thường đã bị chặn ở activeReplacement phía trên.
            // Giữ thêm để an toàn nếu dữ liệu thiếu NewInvoiceHead.
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceCorrection.FirstMethodWasReplacement",
                    "Hóa đơn này đã có hóa đơn thay thế. Vui lòng mở hóa đơn thay thế để xử lý tiếp."));
        }

        return Result<bool>.Failure(
            Error.Validation(
                "InvoiceCorrection.InvalidRule",
                "Không xác định được quy tắc xử lý sai sót cho hóa đơn này."));
    }

    private static Result<bool> ValidateOriginalInvoice(
        InvoiceHead original,
        InvoiceCorrectionType requestedType)
    {
        if (!IsValidCorrectionType(requestedType))
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceCorrection.InvalidCorrectionType",
                    "Loại xử lý sai sót không hợp lệ."));
        }

        // Không cho lấy hóa đơn điều chỉnh làm gốc để xử lý tiếp.
        // Khi cần điều chỉnh tiếp, lập thêm điều chỉnh trên hóa đơn gốc/hoá đơn hiện hành.
        if (original.CorrectionType == InvoiceCorrectionType.AdjustmentAmount ||
            original.CorrectionType == InvoiceCorrectionType.AdjustmentInfo)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceCorrection.CannotCorrectAdjustmentInvoice",
                    "Không lập xử lý sai sót trực tiếp từ hóa đơn điều chỉnh. Vui lòng xử lý tiếp trên hóa đơn gốc hoặc hóa đơn hiện hành."));
        }

        // Hóa đơn thay thế được phép xử lý tiếp.
        // Ví dụ: F0 -> TT1, nếu TT1 sai thì TT1 -> TT2.
        // Vì TT1 là hóa đơn đang có hiệu lực sau thay thế.
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
                    "Hóa đơn chưa có số hóa đơn Viettel."));
        }

        if (!original.IssuedAtUtc.HasValue)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceCorrection.OriginalIssuedDateMissing",
                    "Hóa đơn chưa có ngày phát hành Viettel."));
        }

        if (string.IsNullOrWhiteSpace(original.CodeOfTax))
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "InvoiceCorrection.OriginalNotAcceptedByTaxAuthority",
                    "Hóa đơn chưa được Cơ quan Thuế chấp nhận, chưa thể lập thay thế/điều chỉnh. Vui lòng đồng bộ trạng thái Viettel trước."));
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
            LegalEntityId = original.LegalEntityId,
            InvoiceProviderSettingId = original.InvoiceProviderSettingId,

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
                OrderLegalEntityAllocationId = source.OrderLegalEntityAllocationId,
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

    private static bool IsValidCorrectionType(InvoiceCorrectionType type)
    {
        return type is
            InvoiceCorrectionType.Replacement or
            InvoiceCorrectionType.AdjustmentAmount or
            InvoiceCorrectionType.AdjustmentInfo;
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
}
