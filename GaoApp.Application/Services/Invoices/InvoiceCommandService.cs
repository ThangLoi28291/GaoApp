using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public class InvoiceCommandService : IInvoiceCommandService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IProductVariantRepository _productVariantRepository;
    private readonly IInvoiceProviderSettingRepository _invoiceProviderSettingRepository;
    private readonly IOrderLegalEntityAllocationRepository _allocationRepository;
    private readonly ILegalEntityRepository _legalEntityRepository;

    public InvoiceCommandService(
        IInvoiceRepository invoiceRepository,
        IProductVariantRepository productVariantRepository,
        IInvoiceProviderSettingRepository invoiceProviderSettingRepository,
        IOrderLegalEntityAllocationRepository allocationRepository,
        ILegalEntityRepository legalEntityRepository)
    {
        _invoiceRepository = invoiceRepository;
        _productVariantRepository = productVariantRepository;
        _invoiceProviderSettingRepository = invoiceProviderSettingRepository;
        _allocationRepository = allocationRepository;
        _legalEntityRepository = legalEntityRepository;
    }

    public async Task<Result<InvoiceHeadDto>> CreateInvoiceHeadFromOrderAsync(
        int orderId,
        CancellationToken ct = default)
    {
        if (await _allocationRepository.AnyForOrderAsync(orderId, ct))
        {
            var multiResult = await GenerateInvoicesFromOrderAsync(orderId, ct);
            return multiResult.IsSuccess
                ? Result<InvoiceHeadDto>.Success(multiResult.Value[0])
                : Result<InvoiceHeadDto>.Failure(multiResult.Error);
        }

        var legacyResult = await CreateLegacyInvoiceHeadAsync(orderId, ct);
        return legacyResult.IsSuccess
            ? Result<InvoiceHeadDto>.Success(InvoiceDtoMapper.ToDetailDto(legacyResult.Value))
            : Result<InvoiceHeadDto>.Failure(legacyResult.Error);
    }

    public async Task<Result<InvoiceHeadDto>> GenerateDetailsFromOrderLinesAsync(
        int orderId,
        CancellationToken ct = default)
    {
        if (await _allocationRepository.AnyForOrderAsync(orderId, ct))
        {
            var multiResult = await GenerateInvoicesFromOrderAsync(orderId, ct);
            return multiResult.IsSuccess
                ? Result<InvoiceHeadDto>.Success(multiResult.Value[0])
                : Result<InvoiceHeadDto>.Failure(multiResult.Error);
        }

        var legacyResult = await GenerateLegacyDetailsAsync(orderId, ct);
        return legacyResult.IsSuccess
            ? Result<InvoiceHeadDto>.Success(InvoiceDtoMapper.ToDetailDto(legacyResult.Value))
            : Result<InvoiceHeadDto>.Failure(legacyResult.Error);
    }

    /// <summary>
    /// Sinh trọn bộ hóa đơn của Order. Với Multi LegalEntity, persisted allocation
    /// là source of truth và tuyệt đối không chạy lại allocation ở bước hóa đơn.
    /// </summary>
    public async Task<Result<List<InvoiceHeadDto>>> GenerateInvoicesFromOrderAsync(
        int orderId,
        CancellationToken ct = default)
    {
        if (orderId <= 0)
        {
            return Result<List<InvoiceHeadDto>>.Failure(Error.Validation(
                "Invoice.InvalidOrderId",
                "OrderId không hợp lệ."));
        }

        var order = await _invoiceRepository.GetOrderWithLinesForInvoiceAsync(orderId, ct);
        if (order == null)
        {
            return Result<List<InvoiceHeadDto>>.Failure(
                Error.NotFound("Không tìm thấy đơn hàng để sinh hóa đơn bán ra."));
        }

        var allocations = await _allocationRepository.GetForOrderAsync(orderId, ct);
        if (allocations.Count == 0)
        {
            var legacyResult = await GenerateLegacyDetailsAsync(orderId, ct);
            return legacyResult.IsSuccess
                ? Result<List<InvoiceHeadDto>>.Success(
                    new List<InvoiceHeadDto> { InvoiceDtoMapper.ToDetailDto(legacyResult.Value) })
                : Result<List<InvoiceHeadDto>>.Failure(legacyResult.Error);
        }

        if (allocations.Any(x => x.StoreId != order.StoreId))
        {
            return Result<List<InvoiceHeadDto>>.Failure(Error.Validation(
                "Invoice.AllocationStoreMismatch",
                "Allocation hóa đơn không thuộc cùng cửa hàng với đơn hàng."));
        }

        var existingHeads = await _invoiceRepository
            .GetOriginalInvoiceHeadsWithDetailsByOrderIdAsync(orderId, ct);

        if (existingHeads.Any(x => !x.LegalEntityId.HasValue))
        {
            return Result<List<InvoiceHeadDto>>.Failure(Error.Conflict(
                "Đơn Multi LegalEntity đã có InvoiceHead legacy không gắn HKD. " +
                "Cần xử lý đầu hóa đơn legacy trước khi sinh lại."));
        }

        var legalEntityIds = allocations
            .OrderBy(x => x.SalePriority)
            .ThenBy(x => x.LegalEntityId)
            .Select(x => x.LegalEntityId)
            .Distinct()
            .ToList();
        var legalEntities = new Dictionary<int, LegalEntity>();
        var settings = new Dictionary<int, InvoiceProviderSetting>();
        var headsByLegalEntity = existingHeads
            .Where(x => x.LegalEntityId.HasValue)
            .ToDictionary(x => x.LegalEntityId!.Value);

        // Prevalidate toàn bộ HKD trước khi ghi bất kỳ InvoiceHead nào, tránh tạo
        // nửa bộ hóa đơn khi một HKD thiếu cấu hình.
        foreach (var legalEntityId in legalEntityIds)
        {
            var legalEntity = await _legalEntityRepository.GetByIdAsync(legalEntityId, ct);
            if (legalEntity == null || legalEntity.StoreId != order.StoreId)
            {
                return Result<List<InvoiceHeadDto>>.Failure(Error.Validation(
                    "Invoice.LegalEntityMissing",
                    $"Không tìm thấy HKD #{legalEntityId} của allocation hóa đơn."));
            }

            var settingId = headsByLegalEntity.TryGetValue(legalEntityId, out var existingHead)
                ? existingHead.InvoiceProviderSettingId
                : legalEntity.InvoiceProviderSettingId;
            if (!settingId.HasValue)
            {
                return Result<List<InvoiceHeadDto>>.Failure(Error.Validation(
                    "Invoice.LegalEntityProviderSettingMissing",
                    $"Hóa đơn/HKD {legalEntity.Code} chưa có cấu hình Viettel riêng."));
            }

            var setting = await _invoiceProviderSettingRepository.GetForInvoiceAsync(
                order.StoreId,
                settingId,
                ct);
            var settingError = ValidateProviderSetting(setting, legalEntity);
            if (settingError != null)
                return Result<List<InvoiceHeadDto>>.Failure(settingError);

            legalEntities.Add(legalEntityId, legalEntity);
            settings.Add(legalEntityId, setting!);
        }

        var newHeads = new List<InvoiceHead>();

        foreach (var legalEntityId in legalEntityIds)
        {
            if (headsByLegalEntity.ContainsKey(legalEntityId))
                continue;

            var head = BuildInvoiceHead(
                order,
                settings[legalEntityId],
                legalEntities[legalEntityId]);
            headsByLegalEntity.Add(legalEntityId, head);
            newHeads.Add(head);
        }

        if (newHeads.Count > 0)
        {
            await _invoiceRepository.AddInvoiceHeadsAsync(newHeads, ct);
            await _invoiceRepository.SaveChangesAsync(ct);
        }

        var linesById = order.Lines
            .Where(x => !x.IsDeleted)
            .ToDictionary(x => x.Id);

        foreach (var legalEntityId in legalEntityIds)
        {
            var head = headsByLegalEntity[legalEntityId];
            var eligibleAllocations = allocations
                .Where(x => x.LegalEntityId == legalEntityId)
                .Where(x =>
                    linesById.TryGetValue(x.OrderLineId, out var line) &&
                    line.VariantId > 0 &&
                    line.Variant != null &&
                    line.Variant.HasInputInvoice)
                .OrderBy(x => x.OrderLineId)
                .ThenBy(x => x.Id)
                .ToList();
            var existingAllocationIds = head.Details
                .Where(x => !x.IsDeleted && x.OrderLegalEntityAllocationId.HasValue)
                .Select(x => x.OrderLegalEntityAllocationId!.Value)
                .ToHashSet();
            var missing = eligibleAllocations
                .Where(x => !existingAllocationIds.Contains(x.Id))
                .ToList();

            if (missing.Count > 0 && head.IsLocked)
            {
                return Result<List<InvoiceHeadDto>>.Failure(Error.Validation(
                    "Invoice.Locked",
                    $"Hóa đơn của HKD {legalEntities[legalEntityId].Code} đã khóa nhưng còn thiếu dòng allocation."));
            }

            foreach (var allocation in missing)
            {
                head.Details.Add(InvoiceDetailFactory.FromAllocation(
                    head,
                    allocation,
                    linesById[allocation.OrderLineId]));
            }

            InvoiceAmountCalculator.RecalculateHead(head);
        }

        await _invoiceRepository.SaveChangesAsync(ct);

        var updatedHeads = await _invoiceRepository
            .GetOriginalInvoiceHeadsWithDetailsByOrderIdAsync(orderId, ct);
        var priorityByLegalEntity = allocations
            .GroupBy(x => x.LegalEntityId)
            .ToDictionary(x => x.Key, x => x.Min(a => a.SalePriority));
        var result = updatedHeads
            .Where(x =>
                x.LegalEntityId.HasValue &&
                priorityByLegalEntity.ContainsKey(x.LegalEntityId.Value))
            .OrderBy(x => priorityByLegalEntity[x.LegalEntityId!.Value])
            .ThenBy(x => x.LegalEntityId)
            .Select(InvoiceDtoMapper.ToDetailDto)
            .ToList();

        return Result<List<InvoiceHeadDto>>.Success(result);
    }

    private async Task<Result<InvoiceHead>> CreateLegacyInvoiceHeadAsync(
        int orderId,
        CancellationToken ct)
    {
        if (orderId <= 0)
        {
            return Result<InvoiceHead>.Failure(Error.Validation(
                "Invoice.InvalidOrderId",
                "OrderId không hợp lệ."));
        }

        var order = await _invoiceRepository.GetOrderForInvoiceAsync(orderId, ct);
        if (order == null)
            return Result<InvoiceHead>.Failure(
                Error.NotFound("Không tìm thấy đơn hàng để tạo hóa đơn bán ra."));

        var existedInvoice = await _invoiceRepository
            .GetInvoiceHeadWithDetailsByOrderIdAsync(orderId, ct);
        if (existedInvoice != null)
            return Result<InvoiceHead>.Success(existedInvoice);

        var providerSetting = await _invoiceProviderSettingRepository
            .GetActiveViettelAsync(order.StoreId, ct);
        var settingError = ValidateProviderSetting(providerSetting, legalEntity: null);
        if (settingError != null)
            return Result<InvoiceHead>.Failure(settingError);

        var invoiceHead = BuildInvoiceHead(order, providerSetting!, legalEntity: null);
        await _invoiceRepository.AddInvoiceHeadAsync(invoiceHead, ct);
        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHead>.Success(invoiceHead);
    }

    private async Task<Result<InvoiceHead>> GenerateLegacyDetailsAsync(
        int orderId,
        CancellationToken ct)
    {
        if (orderId <= 0)
        {
            return Result<InvoiceHead>.Failure(Error.Validation(
                "Invoice.InvalidOrderId",
                "OrderId không hợp lệ."));
        }

        var order = await _invoiceRepository.GetOrderWithLinesForInvoiceAsync(orderId, ct);
        if (order == null)
            return Result<InvoiceHead>.Failure(
                Error.NotFound("Không tìm thấy đơn hàng để sinh chi tiết hóa đơn."));

        var invoiceHead = await _invoiceRepository
            .GetInvoiceHeadWithDetailsByOrderIdAsync(orderId, ct);
        if (invoiceHead == null)
        {
            var createResult = await CreateLegacyInvoiceHeadAsync(orderId, ct);
            if (!createResult.IsSuccess)
                return createResult;

            invoiceHead = await _invoiceRepository
                .GetInvoiceHeadWithDetailsByOrderIdAsync(orderId, ct);
            if (invoiceHead == null)
                return Result<InvoiceHead>.Failure(Error.Failure("Không tạo được InvoiceHead."));
        }

        if (invoiceHead.IsLocked)
        {
            return Result<InvoiceHead>.Failure(Error.Validation(
                "Invoice.Locked",
                "Hóa đơn đã khóa, không thể sinh thêm chi tiết từ POS."));
        }

        var existedOrderLineIds = invoiceHead.Details
            .Where(x => !x.IsDeleted && x.OrderLineId.HasValue)
            .Select(x => x.OrderLineId!.Value)
            .ToHashSet();
        var eligibleLines = order.Lines
            .Where(x =>
                !x.IsDeleted &&
                !existedOrderLineIds.Contains(x.Id) &&
                x.VariantId > 0 &&
                x.Variant != null &&
                x.Variant.HasInputInvoice)
            .ToList();

        foreach (var line in eligibleLines)
            invoiceHead.Details.Add(InvoiceDetailFactory.FromOrderLine(invoiceHead, line));

        InvoiceAmountCalculator.RecalculateHead(invoiceHead);
        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHead>.Success(invoiceHead);
    }

    private static InvoiceHead BuildInvoiceHead(
        Order order,
        InvoiceProviderSetting providerSetting,
        LegalEntity? legalEntity)
    {
        return new InvoiceHead
        {
            StoreId = order.StoreId,
            OrderId = order.Id,
            LegalEntityId = legalEntity?.Id,
            LegalEntity = legalEntity,
            InvoiceProviderSettingId = providerSetting.Id,
            InvoiceDate = DateTime.Now,
            BuyerType = InvoiceBuyerTypes.NoInvoice,
            ProviderCode = string.IsNullOrWhiteSpace(providerSetting.ProviderCode)
                ? "VIETTEL"
                : providerSetting.ProviderCode.Trim().ToUpperInvariant(),
            ProviderStatus = InvoiceProviderStatus.LocalDraft,
            TransactionUuid = Guid.NewGuid().ToString("D"),
            SupplierTaxCode = providerSetting.SupplierTaxCode.Trim(),
            InvoiceType = string.IsNullOrWhiteSpace(providerSetting.InvoiceType)
                ? "1"
                : providerSetting.InvoiceType.Trim(),
            TemplateCode = providerSetting.TemplateCode.Trim(),
            InvoiceSeries = providerSetting.InvoiceSeries.Trim(),
            Note = legalEntity == null
                ? null
                : $"Tự sinh theo allocation của {legalEntity.Code}."
        };
    }

    private static Error? ValidateProviderSetting(
        InvoiceProviderSetting? providerSetting,
        LegalEntity? legalEntity)
    {
        if (providerSetting == null)
        {
            return Error.Validation(
                "Invoice.ProviderSettingMissing",
                legalEntity == null
                    ? "Chưa cấu hình Viettel active cho cửa hàng này."
                    : $"Cấu hình Viettel của HKD {legalEntity.Code} không còn active hoặc không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(providerSetting.SupplierTaxCode) ||
            string.IsNullOrWhiteSpace(providerSetting.TemplateCode) ||
            string.IsNullOrWhiteSpace(providerSetting.InvoiceSeries))
        {
            return Error.Validation(
                "Invoice.ProviderSettingInvalid",
                "Cấu hình Viettel thiếu MST phát hành, mẫu số hoặc ký hiệu hóa đơn.");
        }

        if (legalEntity != null &&
            !TaxCodesEqual(legalEntity.TaxCode, providerSetting.SupplierTaxCode))
        {
            return Error.Validation(
                "Invoice.LegalEntityTaxCodeMismatch",
                $"MST của HKD {legalEntity.Code} không khớp MST cấu hình Viettel.");
        }

        return null;
    }

    private static bool TaxCodesEqual(string? left, string? right)
        => string.Equals(
            NormalizeTaxCode(left),
            NormalizeTaxCode(right),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeTaxCode(string? value)
        => new((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .ToArray());

    public async Task<Result<InvoiceHeadDto>> AddManualDetailAsync(
        CreateManualInvoiceDetailRequest request,
        CancellationToken ct = default)
    {
        if (request.InvoiceHeadId <= 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var invoiceHead = await _invoiceRepository
            .GetInvoiceHeadWithDetailsByIdAsync(request.InvoiceHeadId, ct);

        if (invoiceHead == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        if (invoiceHead.IsLocked)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.Locked",
                    "Hóa đơn đã khóa, không thể thêm dòng manual."));
        }

        if (request.ProductVariantId.HasValue &&
            request.ProductVariantId.Value > 0)
        {
            var variant = await _productVariantRepository.GetActiveWithProductAsync(
                request.ProductVariantId.Value,
                ct);

            if (variant == null)
            {
                return Result<InvoiceHeadDto>.Failure(
                    Error.NotFound("Không tìm thấy sản phẩm được chọn."));
            }

            if (string.IsNullOrWhiteSpace(request.ItemName))
            {
                request.ItemName = !string.IsNullOrWhiteSpace(variant.ProductVariantName)
                    ? variant.ProductVariantName
                    : variant.Product?.Name ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(request.UnitName))
            {
                request.UnitName = variant.Product?.BaseUnit?.Name;
            }

            if (request.UnitPrice <= 0)
            {
                request.UnitPrice = variant.Price ?? variant.Product?.BasePrice ?? 0m;
            }
        }

        if (string.IsNullOrWhiteSpace(request.ItemName))
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.ItemNameRequired",
                    "Tên hàng hóa/dịch vụ không được để trống."));
        }

        var validation = InvoiceManualDetailValidator.ValidateAmountInput(
            invoiceHead,
            request.Quantity,
            request.UnitPrice,
            request.VatRate);

        if (!validation.IsSuccess)
        {
            return Result<InvoiceHeadDto>.Failure(
                validation.Error!);
        }

        var detail = InvoiceDetailFactory.Manual(
            invoiceHead,
            request);

        invoiceHead.Details.Add(detail);

        InvoiceAmountCalculator.RecalculateHead(invoiceHead);

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHeadDto>.Success(
            InvoiceDtoMapper.ToDetailDto(invoiceHead));
    }

    public async Task<Result<InvoiceHeadDto>> DeleteManualDetailAsync(
        int invoiceDetailId,
        CancellationToken ct = default)
    {
        if (invoiceDetailId <= 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidDetailId",
                    "InvoiceDetailId không hợp lệ."));
        }

        var detail = await _invoiceRepository.GetInvoiceDetailByIdAsync(
            invoiceDetailId,
            ct);

        if (detail == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy dòng hóa đơn."));
        }

        if (detail.SourceType != InvoiceDetailSourceType.Manual)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.CannotDeleteAutoLine",
                    "Chỉ được xóa dòng manual. Dòng tự sinh từ OrderLine không được xóa."));
        }

        var invoiceHead = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(
            detail.InvoiceHeadId,
            ct);

        if (invoiceHead == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        if (invoiceHead.IsLocked)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.Locked",
                    "Hóa đơn đã khóa, không thể xóa dòng manual."));
        }

        detail.IsDeleted = true;
        detail.DeletedAtUtc = DateTime.UtcNow;

        InvoiceAmountCalculator.RecalculateHead(invoiceHead);

        await _invoiceRepository.SaveChangesAsync(ct);

        var updatedInvoice = await _invoiceRepository.GetInvoiceHeadDetailAsync(
            invoiceHead.Id,
            ct);

        if (updatedInvoice == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn sau khi xóa dòng manual."));
        }

        return Result<InvoiceHeadDto>.Success(
            InvoiceDtoMapper.ToDetailDto(updatedInvoice));
    }

    public async Task<Result<InvoiceHeadDto>> UpdateManualDetailAsync(
        UpdateManualInvoiceDetailRequest request,
        CancellationToken ct = default)
    {
        if (request.InvoiceDetailId <= 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidDetailId",
                    "InvoiceDetailId không hợp lệ."));
        }

        var detail = await _invoiceRepository.GetInvoiceDetailByIdAsync(
            request.InvoiceDetailId,
            ct);

        if (detail == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy dòng hóa đơn."));
        }

        if (detail.SourceType != InvoiceDetailSourceType.Manual)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.CannotEditAutoLine",
                    "Chỉ được sửa dòng manual. Dòng tự sinh từ OrderLine không được sửa."));
        }

        var invoiceHead = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(
            detail.InvoiceHeadId,
            ct);

        if (invoiceHead == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        if (invoiceHead.IsLocked)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.Locked",
                    "Hóa đơn đã khóa, không thể sửa dòng manual."));
        }

        var activeDetail = invoiceHead.Details
            .FirstOrDefault(x => x.Id == detail.Id && !x.IsDeleted);

        if (activeDetail == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Dòng hóa đơn đã bị xóa."));
        }

        var validation = InvoiceManualDetailValidator.ValidateAmountInput(
            invoiceHead,
            request.Quantity,
            request.UnitPrice,
            request.VatRate);

        if (!validation.IsSuccess)
        {
            return Result<InvoiceHeadDto>.Failure(
                validation.Error!);
        }

        activeDetail.Quantity = request.Quantity;
        activeDetail.UnitPrice = request.UnitPrice;
        activeDetail.VatRate = request.VatRate;

        activeDetail.Note = string.IsNullOrWhiteSpace(request.Note)
            ? null
            : request.Note.Trim();

        InvoiceAmountCalculator.RecalculateDetail(activeDetail);
        InvoiceAmountCalculator.RecalculateHead(invoiceHead);

        await _invoiceRepository.SaveChangesAsync(ct);

        var updatedInvoice = await _invoiceRepository.GetInvoiceHeadDetailAsync(
            invoiceHead.Id,
            ct);

        if (updatedInvoice == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn sau khi cập nhật."));
        }

        return Result<InvoiceHeadDto>.Success(
            InvoiceDtoMapper.ToDetailDto(updatedInvoice));
    }

    public async Task<Result<InvoiceHeadDto>> LockInvoiceAsync(
        LockInvoiceRequest request,
        CancellationToken ct = default)
    {
        if (request.InvoiceHeadId <= 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var invoiceHead = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(
            request.InvoiceHeadId,
            ct);

        if (invoiceHead == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        if (invoiceHead.IsLocked)
        {
            return Result<InvoiceHeadDto>.Success(
                InvoiceDtoMapper.ToDetailDto(invoiceHead));
        }

        invoiceHead.IsLocked = true;
        invoiceHead.LockedAtUtc = DateTime.UtcNow;
        invoiceHead.LockedByUserId = request.UserId;
        invoiceHead.LockReason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Khóa hóa đơn."
            : request.Reason.Trim();

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHeadDto>.Success(
            InvoiceDtoMapper.ToDetailDto(invoiceHead));
    }

    public async Task<Result<InvoiceHeadDto>> UnlockInvoiceAsync(
        UnlockInvoiceRequest request,
        CancellationToken ct = default)
    {
        if (request.InvoiceHeadId <= 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var invoiceHead = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(
            request.InvoiceHeadId,
            ct);

        if (invoiceHead == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        if (invoiceHead.LegacyReadOnly)
            return Result<InvoiceHeadDto>.Failure(Error.Validation("Invoice.LegacyReadOnly", "Hóa đơn GaoStore này chỉ lưu để tra cứu, không được mở khóa."));

        if (!invoiceHead.IsLocked)
        {
            return Result<InvoiceHeadDto>.Success(
                InvoiceDtoMapper.ToDetailDto(invoiceHead));
        }

        invoiceHead.IsLocked = false;
        invoiceHead.LockedAtUtc = null;
        invoiceHead.LockedByUserId = null;
        invoiceHead.LockReason = string.IsNullOrWhiteSpace(request.Reason)
            ? null
            : $"Đã mở khóa: {request.Reason.Trim()}";

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHeadDto>.Success(
            InvoiceDtoMapper.ToDetailDto(invoiceHead));
    }
}
