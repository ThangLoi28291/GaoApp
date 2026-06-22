using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
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

    public InvoiceCommandService(
        IInvoiceRepository invoiceRepository,
        IProductVariantRepository productVariantRepository,
        IInvoiceProviderSettingRepository invoiceProviderSettingRepository)
    {
        _invoiceRepository = invoiceRepository;
        _productVariantRepository = productVariantRepository;
        _invoiceProviderSettingRepository = invoiceProviderSettingRepository;
    }

    public async Task<Result<InvoiceHeadDto>> CreateInvoiceHeadFromOrderAsync(
        int orderId,
        CancellationToken ct = default)
    {
        if (orderId <= 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidOrderId",
                    "OrderId không hợp lệ."));
        }

        var order = await _invoiceRepository.GetOrderForInvoiceAsync(
            orderId,
            ct);

        if (order == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy đơn hàng để tạo hóa đơn bán ra."));
        }

        var existedInvoice = await _invoiceRepository
            .GetInvoiceHeadWithDetailsByOrderIdAsync(orderId, ct);

        if (existedInvoice != null)
        {
            return Result<InvoiceHeadDto>.Success(
                InvoiceDtoMapper.ToDetailDto(existedInvoice));
        }

        var providerSetting = await _invoiceProviderSettingRepository
            .GetActiveViettelAsync(order.StoreId, ct);

        if (providerSetting == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.ProviderSettingMissing",
                    "Chưa cấu hình Viettel active cho cửa hàng này."));
        }

        if (string.IsNullOrWhiteSpace(providerSetting.SupplierTaxCode) ||
            string.IsNullOrWhiteSpace(providerSetting.TemplateCode) ||
            string.IsNullOrWhiteSpace(providerSetting.InvoiceSeries))
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.ProviderSettingInvalid",
                    "Cấu hình Viettel thiếu MST phát hành, mẫu số hoặc ký hiệu hóa đơn."));
        }

        var invoiceHead = new InvoiceHead
        {
            StoreId = order.StoreId,
            OrderId = order.Id,
            InvoiceDate = DateTime.Now,

            BuyerType = InvoiceBuyerTypes.NoInvoice,
            BuyerName = null,
            BuyerLegalName = null,
            BuyerTaxCode = null,
            BuyerAddress = null,
            BuyerEmail = null,
            BuyerPhone = null,

            TotalQuantity = 0,
            SubTotal = 0,
            VatAmount = 0,
            GrandTotal = 0,

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

            Note = null
        };

        await _invoiceRepository.AddInvoiceHeadAsync(
            invoiceHead,
            ct);

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHeadDto>.Success(
            InvoiceDtoMapper.ToDetailDto(invoiceHead));
    }

    public async Task<Result<InvoiceHeadDto>> GenerateDetailsFromOrderLinesAsync(
        int orderId,
        CancellationToken ct = default)
    {
        if (orderId <= 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidOrderId",
                    "OrderId không hợp lệ."));
        }

        var order = await _invoiceRepository.GetOrderWithLinesForInvoiceAsync(
            orderId,
            ct);

        if (order == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy đơn hàng để sinh chi tiết hóa đơn."));
        }

        var invoiceHead = await _invoiceRepository
            .GetInvoiceHeadWithDetailsByOrderIdAsync(orderId, ct);

        if (invoiceHead == null)
        {
            var createResult = await CreateInvoiceHeadFromOrderAsync(
                orderId,
                ct);

            if (!createResult.IsSuccess)
                return createResult;

            invoiceHead = await _invoiceRepository
                .GetInvoiceHeadWithDetailsByOrderIdAsync(orderId, ct);

            if (invoiceHead == null)
            {
                return Result<InvoiceHeadDto>.Failure(
                    Error.Failure("Không tạo được InvoiceHead."));
            }
        }

        if (invoiceHead.IsLocked)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
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

        if (!eligibleLines.Any())
        {
            InvoiceAmountCalculator.RecalculateHead(invoiceHead);

            await _invoiceRepository.SaveChangesAsync(ct);

            return Result<InvoiceHeadDto>.Success(
                InvoiceDtoMapper.ToDetailDto(invoiceHead));
        }

        var details = eligibleLines
            .Select(line => InvoiceDetailFactory.FromOrderLine(invoiceHead, line))
            .ToList();

        await _invoiceRepository.AddInvoiceDetailsAsync(
            details,
            ct);

        await _invoiceRepository.SaveChangesAsync(ct);

        var updatedInvoice = await _invoiceRepository
            .GetInvoiceHeadWithDetailsByOrderIdAsync(orderId, ct);

        if (updatedInvoice == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn sau khi sinh chi tiết."));
        }

        InvoiceAmountCalculator.RecalculateHead(updatedInvoice);

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHeadDto>.Success(
            InvoiceDtoMapper.ToDetailDto(updatedInvoice));
    }

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