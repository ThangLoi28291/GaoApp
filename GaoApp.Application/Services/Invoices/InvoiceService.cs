using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public class InvoiceService : IInvoiceService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IProductVariantRepository _productVariantRepository;
    public InvoiceService(IInvoiceRepository invoiceRepository, IProductVariantRepository productVariantRepository)
    {
        _invoiceRepository = invoiceRepository;
        _productVariantRepository = productVariantRepository;
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

        var order = await _invoiceRepository.GetOrderForInvoiceAsync(orderId, ct);

        if (order == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound(
                 
                    "Không tìm thấy đơn hàng để tạo hóa đơn bán ra."));
        }

        var existedInvoice = await _invoiceRepository.GetInvoiceHeadWithDetailsByOrderIdAsync(orderId, ct);

        if (existedInvoice != null)
        {
            return Result<InvoiceHeadDto>.Success(MapToDto(existedInvoice));
        }

        var invoiceHead = new InvoiceHead
        {
            OrderId = order.Id,
            InvoiceDate = DateTime.Now,

            BuyerName = null,
            BuyerTaxCode = null,
            BuyerAddress = null,

            TotalQuantity = 0,
            SubTotal = 0,
            VatAmount = 0,
            GrandTotal = 0,

            ProviderCode = "VIETTEL",
            ProviderStatus = InvoiceProviderStatus.LocalDraft,
            TransactionUuid = Guid.NewGuid().ToString(),

            SupplierTaxCode = "0100109106-509",
            InvoiceType = "1",
            TemplateCode = "1/002",
            InvoiceSeries = "C26THA",

            Note = null
        };

        await _invoiceRepository.AddInvoiceHeadAsync(invoiceHead, ct);
        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHeadDto>.Success(MapToDto(invoiceHead));
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

        var order = await _invoiceRepository.GetOrderWithLinesForInvoiceAsync(orderId, ct);

        if (order == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound(
                    "Không tìm thấy đơn hàng để sinh chi tiết hóa đơn."));
        }

        var invoiceHead = await _invoiceRepository.GetInvoiceHeadWithDetailsByOrderIdAsync(orderId, ct);
        if (invoiceHead.IsLocked)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.Locked",
                    "Hóa đơn đã khóa, không thể sinh lại dòng chi tiết."));
        }
        if (invoiceHead == null)
        {
            var createResult = await CreateInvoiceHeadFromOrderAsync(orderId, ct);

            if (!createResult.IsSuccess)
                return createResult;

            invoiceHead = await _invoiceRepository.GetInvoiceHeadWithDetailsByOrderIdAsync(orderId, ct);

            if (invoiceHead == null)
            {
                return Result<InvoiceHeadDto>.Failure(
                    Error.Failure(
                        "Không tạo được InvoiceHead."));
            }
        }

        // GHI CHÚ:
        // Invoice đã khóa thì không được sinh thêm dòng tự động từ POS.
        // Nếu không chặn, hóa đơn đã khóa vẫn có thể bị thay đổi tổng tiền.
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

        // GHI CHÚ:
        // Chỉ lấy các OrderLine:
        // - chưa từng sinh InvoiceDetail;
        // - có Variant;
        // - Variant có HasInputInvoice = true.
        //
        // Đây là nghiệp vụ chính:
        // Hàng chưa có hóa đơn đầu vào vẫn bán POS bình thường,
        // nhưng không đưa vào InvoiceDetail bán ra.
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
            // GHI CHÚ:
            // Không có dòng mới thì vẫn tính lại tổng để đảm bảo header khớp detail.
            // Sau khi tính lại phải SaveChanges để lưu DB.
            RecalculateInvoiceHead(invoiceHead);

            await _invoiceRepository.SaveChangesAsync(ct);

            return Result<InvoiceHeadDto>.Success(MapToDto(invoiceHead));
        }

        var details = eligibleLines
            .Select(line =>
            {
                var quantity = line.Quantity;
                var unitPrice = line.UnitPrice;
                var amount = quantity * unitPrice;

                return new InvoiceDetail
                {
                    InvoiceHeadId = invoiceHead.Id,

                    OrderLineId = line.Id,
                    ProductVariantId = line.VariantId,

                    SourceType = InvoiceDetailSourceType.FromOrderLine,

                    ItemName = line.ItemName,
                    UnitName = line.UnitName,

                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    Amount = amount,

                    // GHI CHÚ:
                    // Giai đoạn hiện tại để VAT = 0.
                    // Nếu sau này xuất hóa đơn GTGT thật thì phải lấy VAT theo Tax/Product.
                    VatRate = 0,
                    VatAmount = 0,
                    TotalAmount = amount,

                    Note = "Tự sinh từ POS OrderLine có ProductVariant.HasInputInvoice = true."
                };
            })
            .ToList();

        await _invoiceRepository.AddInvoiceDetailsAsync(details, ct);
        await _invoiceRepository.SaveChangesAsync(ct);

        var updatedInvoice = await _invoiceRepository.GetInvoiceHeadWithDetailsByOrderIdAsync(orderId, ct);

        if (updatedInvoice == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound(
                    "Không tìm thấy hóa đơn sau khi sinh chi tiết."));
        }

        RecalculateInvoiceHead(updatedInvoice);

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHeadDto>.Success(MapToDto(updatedInvoice));
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

        if (request.Quantity <= 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.QuantityInvalid",
                    "Số lượng phải lớn hơn 0."));
        }

        if (request.UnitPrice < 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.UnitPriceInvalid",
                    "Đơn giá không được âm."));
        }

        if (request.VatRate < 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.VatRateInvalid",
                    "Thuế suất VAT không được âm."));
        }

        var invoiceHead = await _invoiceRepository
            .GetInvoiceHeadWithDetailsByIdAsync(request.InvoiceHeadId, ct);

        // GHI CHÚ:
        // Phải check null trước khi check IsLocked.
        // Nếu hóa đơn không tồn tại mà gọi invoiceHead.IsLocked sẽ lỗi NullReferenceException.
        if (invoiceHead == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound(
                    "Không tìm thấy hóa đơn bán ra."));
        }

        // GHI CHÚ:
        // Hóa đơn đã khóa thì không cho thêm dòng manual.
        if (invoiceHead.IsLocked)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.Locked",
                    "Hóa đơn đã khóa, không thể thêm dòng manual."));
        }

        // GHI CHÚ:
        // Nếu người dùng chọn sản phẩm, hệ thống tự lấy tên/đơn vị/giá.
        // Vì vậy đoạn này phải chạy trước validate ItemName.
        if (request.ProductVariantId.HasValue && request.ProductVariantId.Value > 0)
        {
            var variant = await _productVariantRepository.GetActiveWithProductAsync(
                request.ProductVariantId.Value,
                ct);

            if (variant == null)
            {
                return Result<InvoiceHeadDto>.Failure(
                    Error.NotFound(
                        "Không tìm thấy sản phẩm được chọn."));
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

        // GHI CHÚ:
        // Validate ItemName sau khi đã có cơ hội tự fill từ ProductVariant.
        if (string.IsNullOrWhiteSpace(request.ItemName))
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.ItemNameRequired",
                    "Tên hàng hóa/dịch vụ không được để trống."));
        }

        var amount = request.Quantity * request.UnitPrice;
        var vatAmount = amount * request.VatRate / 100m;
        var totalAmount = amount + vatAmount;

        var detail = new InvoiceDetail
        {
            InvoiceHeadId = invoiceHead.Id,

            // GHI CHÚ:
            // Dòng manual không sinh từ POS OrderLine nên OrderLineId = null.
            OrderLineId = null,

            // GHI CHÚ:
            // Có thể null nếu thêm dòng tự do.
            // Có giá trị nếu chọn sản phẩm từ hệ thống.
            ProductVariantId = request.ProductVariantId,

            SourceType = InvoiceDetailSourceType.Manual,

            ItemName = request.ItemName.Trim(),
            UnitName = string.IsNullOrWhiteSpace(request.UnitName)
                ? null
                : request.UnitName.Trim(),

            Quantity = request.Quantity,
            UnitPrice = request.UnitPrice,
            Amount = amount,

            VatRate = request.VatRate,
            VatAmount = vatAmount,
            TotalAmount = totalAmount,

            Note = string.IsNullOrWhiteSpace(request.Note)
                ? null
                : request.Note.Trim()
        };

        invoiceHead.Details.Add(detail);

        // GHI CHÚ:
        // Sau khi thêm dòng, phải tính lại tổng header từ detail.
        RecalculateInvoiceHead(invoiceHead);

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHeadDto>.Success(MapToDto(invoiceHead));
    }
    private static void RecalculateInvoiceHead(InvoiceHead invoiceHead)
    {
        var activeDetails = invoiceHead.Details
            .Where(x => !x.IsDeleted)
            .ToList();

        invoiceHead.TotalQuantity = activeDetails.Sum(x => x.Quantity);
        invoiceHead.SubTotal = activeDetails.Sum(x => x.Amount);
        invoiceHead.VatAmount = activeDetails.Sum(x => x.VatAmount);
        invoiceHead.GrandTotal = activeDetails.Sum(x => x.TotalAmount);
    }

    private static InvoiceHeadDto MapToDto(InvoiceHead entity)
    {
        return new InvoiceHeadDto
        {
            Id = entity.Id,
            OrderId = entity.OrderId,
            InvoiceNumber = entity.InvoiceNumber,
            InvoiceDate = entity.InvoiceDate,
            BuyerName = entity.BuyerName,
            BuyerTaxCode = entity.BuyerTaxCode,
            BuyerAddress = entity.BuyerAddress,
            TotalQuantity = entity.TotalQuantity,
            SubTotal = entity.SubTotal,
            VatAmount = entity.VatAmount,
            GrandTotal = entity.GrandTotal,
            Note = entity.Note,
            IsLocked = entity.IsLocked,
            LockedAtUtc = entity.LockedAtUtc,
            LockedByUserId = entity.LockedByUserId,
            LockReason = entity.LockReason,
            TransactionUuid = entity.TransactionUuid,
            ProviderCode = entity.ProviderCode,
            SupplierTaxCode = entity.SupplierTaxCode,
            InvoiceType = entity.InvoiceType,
            TemplateCode = entity.TemplateCode,
            InvoiceSeries = entity.InvoiceSeries,
            ProviderStatus = entity.ProviderStatus,
            ProviderInvoiceNo = entity.ProviderInvoiceNo,
            ProviderTransactionId = entity.ProviderTransactionId,
            ReservationCode = entity.ReservationCode,
            CodeOfTax = entity.CodeOfTax,
            IssuedAtUtc = entity.IssuedAtUtc,
            LastSyncedAtUtc = entity.LastSyncedAtUtc,
            LastErrorCode = entity.LastErrorCode,
            LastErrorMessage = entity.LastErrorMessage,
            PdfFilePath = entity.PdfFilePath,
            ZipFilePath = entity.ZipFilePath,
            Details = entity.Details
    .Where(x => !x.IsDeleted)
    .OrderBy(x => x.Id)
    .Select(MapDetailToDto)
    .ToList()
        };
    }

    private static InvoiceDetailDto MapDetailToDto(InvoiceDetail entity)
    {
        return new InvoiceDetailDto
        {
            Id = entity.Id,
            InvoiceHeadId = entity.InvoiceHeadId,
            OrderLineId = entity.OrderLineId,
            ProductVariantId = entity.ProductVariantId,
            SourceType = entity.SourceType,
            ItemName = entity.ItemName,
            UnitName = entity.UnitName,
            Quantity = entity.Quantity,
            UnitPrice = entity.UnitPrice,
            Amount = entity.Amount,
            VatRate = entity.VatRate,
            VatAmount = entity.VatAmount,
            TotalAmount = entity.TotalAmount,
            Note = entity.Note
        };
    }
    public async Task<Result<PagedResult<InvoiceListItemDto>>> GetInvoicesAsync(
    InvoiceListQueryDto query,
    CancellationToken ct = default)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        var (items, total) = await _invoiceRepository.QueryInvoiceHeadsAsync(
     query.FromDate,
     query.ToDate,
     query.OrderId,
     query.Keyword,
     query.DisplayMode,

     // GHI CHÚ:
     // Truyền kiểu sắp xếp người dùng chọn từ màn hình.
     query.SortMode,

     page,
     pageSize,
     ct);

        var dtoItems = items.Select(x => new InvoiceListItemDto
        {
            Id = x.Id,
            OrderId = x.OrderId,
            OrderNumber = x.Order?.OrderNumber,
            InvoiceNumber = x.InvoiceNumber,
            InvoiceDate = x.InvoiceDate,
            BuyerName = x.BuyerName,
            TotalQuantity = x.TotalQuantity,
            SubTotal = x.SubTotal,
            VatAmount = x.VatAmount,
            GrandTotal = x.GrandTotal,

            // GHI CHÚ:
            // Đưa trạng thái khóa ra ngoài list để hiển thị badge và lọc dễ hơn.
            IsLocked = x.IsLocked,

            DetailCount = x.Details.Count(d => !d.IsDeleted),

            AutoLineCount = x.Details.Count(d =>
                !d.IsDeleted &&
                d.SourceType == GaoApp.Domain.Enums.InvoiceDetailSourceType.FromOrderLine),

            ManualLineCount = x.Details.Count(d =>
                !d.IsDeleted &&
                d.SourceType == GaoApp.Domain.Enums.InvoiceDetailSourceType.Manual)
        }).ToList();

        return Result<PagedResult<InvoiceListItemDto>>.Success(
            new PagedResult<InvoiceListItemDto>
            {
                Items = dtoItems,
                Page = page,
                PageSize = pageSize,
                TotalItems = total
            });
    }

    public async Task<Result<InvoiceHeadDto>> GetInvoiceDetailAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var invoice = await _invoiceRepository.GetInvoiceHeadDetailAsync(invoiceHeadId, ct);

        if (invoice == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound(
                    
                    "Không tìm thấy hóa đơn bán ra."));
        }

        return Result<InvoiceHeadDto>.Success(MapToDto(invoice));
    }
    public async Task<List<InvoiceProductVariantSearchItemDto>> SearchProductVariantsAsync(
    string keyword,
    int take = 20,
    CancellationToken ct = default)
    {
        keyword = (keyword ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(keyword))
            return new List<InvoiceProductVariantSearchItemDto>();

        var variants = await _productVariantRepository.SearchForPOSAsync(keyword, take, ct);

        return variants.Select(x =>
        {
            var productName = x.Product?.Name ?? string.Empty;
            var variantName = x.ProductVariantName ?? string.Empty;

            var displayName = string.IsNullOrWhiteSpace(variantName)
                ? productName
                : variantName;

            var unitName = x.Product?.BaseUnit?.Name;

            return new InvoiceProductVariantSearchItemDto
            {
                ProductVariantId = x.Id,
                ProductId = x.ProductId,
                DisplayName = displayName,
                Sku = x.Sku,
                Barcode = null,
                UnitName = unitName,
                UnitPrice = x.Price ?? x.Product?.BasePrice ?? 0m
            };
        }).ToList();
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

        var detail = await _invoiceRepository.GetInvoiceDetailByIdAsync(invoiceDetailId, ct);

        if (detail == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound(
                 
                    "Không tìm thấy dòng hóa đơn."));
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

        // GHI CHÚ:
        // Phải kiểm tra null trước.
        // Nếu invoiceHead null mà gọi invoiceHead.IsLocked thì sẽ lỗi NullReferenceException.
        if (invoiceHead == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound(
                    "Không tìm thấy hóa đơn bán ra."));
        }

        // GHI CHÚ:
        // Hóa đơn đã khóa thì không cho xóa dòng manual.
        if (invoiceHead.IsLocked)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.Locked",
                    "Hóa đơn đã khóa, không thể xóa dòng manual."));
        }

        detail.IsDeleted = true;
        detail.DeletedAtUtc = DateTime.UtcNow;

        RecalculateInvoiceHead(invoiceHead);

        await _invoiceRepository.SaveChangesAsync(ct);

        var updatedInvoice = await _invoiceRepository.GetInvoiceHeadDetailAsync(invoiceHead.Id, ct);

        if (updatedInvoice == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound(
               
                    "Không tìm thấy hóa đơn sau khi xóa dòng manual."));
        }

        return Result<InvoiceHeadDto>.Success(MapToDto(updatedInvoice));
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

        if (request.Quantity <= 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.QuantityInvalid",
                    "Số lượng phải lớn hơn 0."));
        }

        if (request.UnitPrice < 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.UnitPriceInvalid",
                    "Đơn giá không được âm."));
        }

        if (request.VatRate < 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.VatRateInvalid",
                    "VAT không được âm."));
        }

        var detail = await _invoiceRepository.GetInvoiceDetailByIdAsync(
            request.InvoiceDetailId,
            ct);

        if (detail == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound(
          
                    "Không tìm thấy dòng hóa đơn."));
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

        // GHI CHÚ:
        // Kiểm tra null trước để tránh NullReferenceException.
        if (invoiceHead == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound(
                    "Không tìm thấy hóa đơn bán ra."));
        }

        // GHI CHÚ:
        // Hóa đơn đã khóa thì không cho sửa dòng manual.
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
                Error.NotFound(
                
                    "Dòng hóa đơn đã bị xóa."));
        }

        var amount = request.Quantity * request.UnitPrice;
        var vatAmount = amount * request.VatRate / 100m;
        var totalAmount = amount + vatAmount;

        activeDetail.Quantity = request.Quantity;
        activeDetail.UnitPrice = request.UnitPrice;
        activeDetail.Amount = amount;
        activeDetail.VatRate = request.VatRate;
        activeDetail.VatAmount = vatAmount;
        activeDetail.TotalAmount = totalAmount;
        activeDetail.Note = string.IsNullOrWhiteSpace(request.Note)
            ? null
            : request.Note.Trim();

        RecalculateInvoiceHead(invoiceHead);

        await _invoiceRepository.SaveChangesAsync(ct);

        var updatedInvoice = await _invoiceRepository.GetInvoiceHeadDetailAsync(
            invoiceHead.Id,
            ct);

        if (updatedInvoice == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound(
                   
                    "Không tìm thấy hóa đơn sau khi cập nhật."));
        }

        return Result<InvoiceHeadDto>.Success(MapToDto(updatedInvoice));
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
                Error.NotFound(
           
                    "Không tìm thấy hóa đơn bán ra."));
        }

        if (invoiceHead.IsLocked)
        {
            return Result<InvoiceHeadDto>.Success(MapToDto(invoiceHead));
        }

        invoiceHead.IsLocked = true;
        invoiceHead.LockedAtUtc = DateTime.UtcNow;
        invoiceHead.LockedByUserId = request.UserId;
        invoiceHead.LockReason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Khóa hóa đơn."
            : request.Reason.Trim();

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHeadDto>.Success(MapToDto(invoiceHead));
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
                Error.NotFound(
                   
                    "Không tìm thấy hóa đơn bán ra."));
        }

        if (!invoiceHead.IsLocked)
        {
            return Result<InvoiceHeadDto>.Success(MapToDto(invoiceHead));
        }

        invoiceHead.IsLocked = false;
        invoiceHead.LockedAtUtc = null;
        invoiceHead.LockedByUserId = null;
        invoiceHead.LockReason = string.IsNullOrWhiteSpace(request.Reason)
            ? null
            : $"Đã mở khóa: {request.Reason.Trim()}";

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<InvoiceHeadDto>.Success(MapToDto(invoiceHead));
    }
}