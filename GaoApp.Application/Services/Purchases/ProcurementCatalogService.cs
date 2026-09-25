using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.DTOs.Units;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Application.Interfaces.Services.Units;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Purchases;

/// <summary>
/// Cầu nối có kiểm soát giữa hàng mô tả trong quy trình mua và danh mục tồn kho thật.
/// Không bao giờ thay snapshot người dùng đã gõ trên yêu cầu/đơn đặt hàng.
/// </summary>
public sealed class ProcurementCatalogService : IProcurementCatalogService
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly IProductService _products;
    private readonly IUnitService _units;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IAppUnitOfWork _unitOfWork;

    public ProcurementCatalogService(
        IPurchaseOrderRepository orders,
        IProductService products,
        IUnitService units,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IAppUnitOfWork unitOfWork)
    {
        _orders = orders;
        _products = products;
        _units = units;
        _tenant = tenant;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<ProcurementQuickCreateOptionsDto> GetQuickCreateOptionsAsync(
        CancellationToken ct = default)
    {
        var categories = await _orders.GetActiveCategoriesAsync(ct);
        var units = await _orders.GetActiveUnitsAsync(ct);

        return new ProcurementQuickCreateOptionsDto
        {
            Categories = categories.Select(x => new ProcurementCatalogOptionDto
            {
                Id = x.Id,
                Text = x.Name,
                Code = x.Code
            }).ToList(),
            Units = units.Select(x => new ProcurementCatalogOptionDto
            {
                Id = x.Id,
                Text = x.Name,
                Code = x.Code
            }).ToList()
        };
    }

    public Task<PurchaseLookupPageDto<PurchaseSupplierLookupDto>> SearchSuppliersAsync(
        string? term,
        int page,
        CancellationToken ct = default)
        => _orders.SearchSuppliersAsync(
            RequireStoreId(),
            NormalizeTerm(term),
            Math.Max(1, page),
            20,
            ct);

    public async Task ResolvePurchaseOrderLineAsync(
        int purchaseOrderId,
        int purchaseOrderLineId,
        ResolvePurchaseOrderLineRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var order = await LoadOrderForResolutionAsync(
            purchaseOrderId,
            purchaseOrderLineId,
            request.RowVersion,
            ct);
        var line = FindLine(order, purchaseOrderLineId);

        await ResolveLineCoreAsync(
            order,
            line,
            request.ProductVariantId,
            request.ProductUnitConversionId,
            request.Note,
            ct);
        await _orders.SaveChangesAsync(ct);
    }

    public async Task<ProcurementCreatedProductDto> QuickCreateProductAsync(
        QuickCreateProcurementProductRequest request,
        bool canCreateUnit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var result = await CreateProductCoreAsync(request, canCreateUnit, ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public Task<ProcurementCreatedProductDto> CreateProductWithinTransactionAsync(
        QuickCreateProcurementProductRequest request,
        bool canCreateUnit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateProductCoreAsync(request, canCreateUnit, ct);
    }

    public async Task<ProcurementCreatedProductDto> QuickCreateAndResolvePurchaseOrderLineAsync(
        int purchaseOrderId,
        int purchaseOrderLineId,
        QuickCreateAndResolvePurchaseOrderLineRequest request,
        bool canCreateUnit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Product);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // Khóa logic trước khi tạo danh mục để một lỗi concurrency không để lại
            // sản phẩm mồ côi.
            var order = await LoadOrderForResolutionAsync(
                purchaseOrderId,
                purchaseOrderLineId,
                request.RowVersion,
                ct);
            var line = FindLine(order, purchaseOrderLineId);
            if (request.Product.SupplierId != order.SupplierId)
                throw new BusinessRuleException(
                    "Nhà cung cấp của sản phẩm tạo nhanh phải trùng nhà cung cấp trên đơn đặt hàng.");

            var created = await CreateProductCoreAsync(request.Product, canCreateUnit, ct);
            await ResolveLineCoreAsync(
                order,
                line,
                created.ProductVariantId,
                created.ProductUnitConversionId,
                request.ResolutionNote,
                ct);
            await _orders.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return created;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<PurchaseOrder> LoadOrderForResolutionAsync(
        int purchaseOrderId,
        int purchaseOrderLineId,
        string? postedRowVersion,
        CancellationToken ct)
    {
        var order = await _orders.GetDetailAsync(purchaseOrderId, tracking: true, ct)
            ?? throw new BusinessRuleException("Đơn đặt hàng không tồn tại trong cửa hàng hiện tại.");

        EnsureRowVersion(order.RowVersion, postedRowVersion);
        if (order.Status is PurchaseOrderStatus.Rejected or
            PurchaseOrderStatus.FullyReceived or
            PurchaseOrderStatus.ShortClosed or
            PurchaseOrderStatus.Cancelled)
            throw new BusinessRuleException(
                "Không thể liên kết sản phẩm khi đơn đặt hàng đã kết thúc hoặc bị hủy/từ chối.");

        var line = FindLine(order, purchaseOrderLineId);
        if (line.ItemKind != PurchaseItemKind.FreeText)
            throw new BusinessRuleException("Chỉ dòng hàng tự gõ mới cần liên kết danh mục.");
        if (line.PendingQuantity <= 0m)
            throw new BusinessRuleException("Dòng hàng không còn số lượng chờ nhận.");

        return order;
    }

    private async Task ResolveLineCoreAsync(
        PurchaseOrder order,
        PurchaseOrderLine line,
        int variantId,
        int conversionId,
        string? note,
        CancellationToken ct)
    {
        var storeId = RequireStoreId();
        var variant = await _orders.GetVariantAsync(variantId, ct)
            ?? throw new BusinessRuleException("Sản phẩm được chọn không tồn tại hoặc đã ngừng hoạt động.");
        var conversion = await _orders.GetConversionAsync(conversionId, ct)
            ?? throw new BusinessRuleException("Đơn vị mua được chọn không tồn tại hoặc đã ngừng hoạt động.");

        if (variant.StoreId != storeId || variant.Product.StoreId != storeId ||
            conversion.StoreId != storeId || conversion.Unit.StoreId != storeId)
            throw new BusinessRuleException("Sản phẩm/đơn vị không thuộc cửa hàng hiện tại.");
        if (!variant.Product.IsActive || !conversion.IsActive || !conversion.Unit.IsActive || conversion.Factor <= 0m)
            throw new BusinessRuleException("Sản phẩm/đơn vị mua đã ngừng hoạt động hoặc có hệ số quy đổi không hợp lệ.");
        if (conversion.ProductVariantId != variant.Id)
            throw new BusinessRuleException("Đơn vị mua không thuộc sản phẩm đã chọn.");

        var alreadyUsed = await _orders.HasReceiptLineAsync(line.Id, ct);
        var mappingChanged = line.ProductVariantId != variant.Id ||
                             line.ProductUnitConversionId != conversion.Id;
        if (alreadyUsed && mappingChanged)
            throw new BusinessRuleException(
                "Dòng hàng đã được đưa vào một phiếu nhập. Không thể đổi liên kết sản phẩm; hãy xử lý phiếu nhập liên quan trước.");

        var now = DateTime.UtcNow;
        line.ProductVariantId = variant.Id;
        line.ProductUnitConversionId = conversion.Id;
        line.UnitId = conversion.UnitId;
        line.ConversionFactor = conversion.Factor;
        line.ResolvedAtUtc = now;
        line.ResolvedByUserId = _currentUser.UserId;
        line.ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        // Chủ ý không sửa ProductNameSnapshot, UnitNameSnapshot, SkuSnapshot hoặc
        // ItemKind: đó là bằng chứng về nội dung người dùng đã yêu cầu ban đầu.
        order.UpdatedAtUtc = now;
        order.UpdatedBy = _currentUser.UserId;
        order.Actions.Add(new PurchaseOrderAction
        {
            ActionType = PurchaseOrderActionType.Updated,
            FromStatus = order.Status,
            ToStatus = order.Status,
            ActorUserId = _currentUser.UserId,
            OccurredAtUtc = now,
            Note = $"Liên kết dòng {line.LineNo} '{line.ProductNameSnapshot}' với SKU {variant.Sku}, đơn vị {conversion.Unit.Name}." +
                   (string.IsNullOrWhiteSpace(note) ? string.Empty : $" {note.Trim()}")
        });
    }

    private async Task<ProcurementCreatedProductDto> CreateProductCoreAsync(
        QuickCreateProcurementProductRequest request,
        bool canCreateUnit,
        CancellationToken ct)
    {
        var storeId = RequireStoreId();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleException("Vui lòng nhập tên sản phẩm.");
        if (name.Length > 200)
            throw new BusinessRuleException("Tên sản phẩm không được vượt quá 200 ký tự.");

        var category = await _orders.GetCategoryAsync(request.CategoryId, ct)
            ?? throw new BusinessRuleException("Danh mục không tồn tại hoặc đã ngừng hoạt động.");
        var supplier = await _orders.GetSupplierAsync(request.SupplierId, ct)
            ?? throw new BusinessRuleException("Nhà cung cấp không tồn tại hoặc đã ngừng hoạt động.");
        if (category.StoreId != storeId || supplier.StoreId != storeId)
            throw new BusinessRuleException("Danh mục/nhà cung cấp không thuộc cửa hàng hiện tại.");

        var unitId = await ResolveUnitIdAsync(request, canCreateUnit, ct);
        var create = await _products.CreateAsync(
            storeId,
            new ProductCreateDto
            {
                Name = name,
                Alias = null,
                CategoryId = category.Id,
                SupplierId = supplier.Id,
                BaseUnitId = unitId,
                BasePrice = 0m,
                Description = "Tạo nhanh từ quy trình nhập hàng; chờ hoàn thiện danh mục.",
                IsSellable = false
            },
            _currentUser.UserId,
            ct);
        if (!create.IsSuccess)
            throw new BusinessRuleException(
                create.HasValidationErrors
                    ? string.Join(" ", create.ValidationErrors.Select(x => x.ErrorMessage))
                    : create.Error.Message);

        var conversion = await _orders.GetDefaultProductConversionAsync(create.Value, ct)
            ?? throw new InvalidOperationException(
                "Created product is missing its default unit conversion.");
        if (conversion.ProductVariant.Product.StoreId != storeId || conversion.Unit.StoreId != storeId)
            throw new InvalidOperationException(
                "Created product has an inconsistent store relation.");

        return MapCreatedProduct(create.Value, conversion);
    }

    private async Task<int> ResolveUnitIdAsync(
        QuickCreateProcurementProductRequest request,
        bool canCreateUnit,
        CancellationToken ct)
    {
        var storeId = RequireStoreId();
        if (request.UnitId is > 0)
        {
            var unit = await _orders.GetUnitAsync(request.UnitId.Value, ct)
                ?? throw new BusinessRuleException("Đơn vị không tồn tại hoặc đã ngừng hoạt động.");
            if (unit.StoreId != storeId)
                throw new BusinessRuleException("Đơn vị không thuộc cửa hàng hiện tại.");
            return unit.Id;
        }

        var newUnitName = request.NewUnitName?.Trim();
        if (string.IsNullOrWhiteSpace(newUnitName))
            throw new BusinessRuleException("Vui lòng chọn đơn vị hoặc nhập tên tại 'Đơn vị khác'.");
        if (newUnitName.Length > 200)
            throw new BusinessRuleException("Tên đơn vị không được vượt quá 200 ký tự.");

        var existing = (await _orders.GetActiveUnitsAsync(ct))
            .FirstOrDefault(x => string.Equals(x.Name.Trim(), newUnitName, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing.Id;
        if (!canCreateUnit)
            throw new BusinessRuleException(
                "Đơn vị này chưa có trong danh mục. Bạn cần quyền tạo đơn vị hoặc chọn một đơn vị đã có.");

        var created = await _units.CreateAsync(
            storeId,
            new CreateUnitRequest
            {
                Name = newUnitName,
                Code = null,
                Status = true,
                IsBase = false,
                SortOrder = 0
            },
            _currentUser.UserId,
            ct);
        if (!created.IsSuccess)
            throw new BusinessRuleException(
                created.HasValidationErrors
                    ? string.Join(" ", created.ValidationErrors.Select(x => x.ErrorMessage))
                    : created.Error.Message);
        return created.Value;
    }

    private static ProcurementCreatedProductDto MapCreatedProduct(
        int productId,
        ProductUnitConversion conversion)
    {
        var variant = conversion.ProductVariant;
        return new ProcurementCreatedProductDto
        {
            ProductId = productId,
            ProductVariantId = variant.Id,
            ProductUnitConversionId = conversion.Id,
            UnitId = conversion.UnitId,
            ProductName = string.IsNullOrWhiteSpace(variant.ProductVariantName)
                ? variant.Product.Name
                : variant.ProductVariantName,
            Sku = variant.Sku,
            UnitName = conversion.Unit.Name,
            Factor = conversion.Factor,
            IsSellable = variant.Product.IsSellable
        };
    }

    private int RequireStoreId()
        => _tenant.StoreId is > 0
            ? _tenant.StoreId.Value
            : throw new InvalidOperationException(
                "Current store context is unavailable.");

    private static PurchaseOrderLine FindLine(PurchaseOrder order, int lineId)
        => order.Lines.FirstOrDefault(x => x.Id == lineId && !x.IsDeleted)
           ?? throw new BusinessRuleException("Dòng đặt hàng không tồn tại trong đơn hiện tại.");

    private static void EnsureRowVersion(byte[] current, string? posted)
    {
        if (string.IsNullOrWhiteSpace(posted))
            throw new BusinessRuleException("Thiếu RowVersion. Vui lòng tải lại đơn.");
        byte[] expected;
        try { expected = Convert.FromBase64String(posted); }
        catch (FormatException) { throw new BusinessRuleException("RowVersion không hợp lệ."); }
        if (!current.SequenceEqual(expected))
            throw new BusinessRuleException(
                "Đơn đã được người khác cập nhật. Vui lòng tải lại trước khi liên kết sản phẩm.");
    }

    private static string? NormalizeTerm(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
