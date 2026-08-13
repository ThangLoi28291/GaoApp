using FluentValidation;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Globalization;


namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Service quản lý phiếu điều chỉnh kho.
/// Phase ADJ.3:
/// - Tạo phiếu Draft.
/// - Sửa phiếu Draft.
/// - Xóa mềm phiếu Draft.
/// - Gửi duyệt.
/// - Xem chi tiết / danh sách.
/// 
/// Lưu ý cực quan trọng:
/// Phase này chưa tạo InventoryMovement, chưa tác động tồn kho.
/// </summary>
public class InventoryAdjustmentDocumentService
    : IInventoryAdjustmentDocumentService
{
    private readonly IInventoryAdjustmentDocumentRepository _repository;
    private readonly IWarehouseRepository _warehouseRepository;
    private readonly IInventoryUnitResolver _inventoryUnitResolver;
    private readonly IInventoryAdjustmentDocumentNumberService _numberService;
    private readonly IValidator<CreateInventoryAdjustmentDocumentRequest> _createValidator;
    private readonly IValidator<UpdateInventoryAdjustmentDocumentRequest> _updateValidator;
    private readonly IInventoryMovementService _inventoryMovementService;
    private readonly IInventoryMovementFactory _inventoryMovementFactory;
    private readonly IInventoryMovementNoteBuilder _inventoryMovementNoteBuilder;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInventoryCostSuggestionService _costSuggestionService;

    public InventoryAdjustmentDocumentService(
        IInventoryAdjustmentDocumentRepository repository,
        IWarehouseRepository warehouseRepository,
        IInventoryUnitResolver inventoryUnitResolver,
        IInventoryAdjustmentDocumentNumberService numberService,
        IValidator<CreateInventoryAdjustmentDocumentRequest> createValidator,
        IValidator<UpdateInventoryAdjustmentDocumentRequest> updateValidator,
        IInventoryMovementService inventoryMovementService,
IInventoryMovementFactory inventoryMovementFactory,
IInventoryMovementNoteBuilder inventoryMovementNoteBuilder,
IUnitOfWork unitOfWork,
IInventoryCostSuggestionService costSuggestionService)
    {
        _repository = repository;
        _warehouseRepository = warehouseRepository;
        _inventoryUnitResolver = inventoryUnitResolver;
        _numberService = numberService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _inventoryMovementService = inventoryMovementService;
        _inventoryMovementFactory = inventoryMovementFactory;
        _inventoryMovementNoteBuilder = inventoryMovementNoteBuilder;
        _unitOfWork = unitOfWork;
        _costSuggestionService = costSuggestionService;
    }

    public async Task<InventoryAdjustmentDocumentDetailDto> CreateAsync(
        CreateInventoryAdjustmentDocumentRequest request,
        CancellationToken ct = default)
    {
        var validation = await _createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            throw new ValidationException(validation.Errors);

        await EnsureWarehouseExistsAsync(request.WarehouseId, ct);

        var documentNo = await _numberService.GenerateAsync(ct);

        var document = new InventoryAdjustmentDocument
        {
            DocumentNo = documentNo,
            DocumentDate = request.DocumentDate ?? DateTime.UtcNow,
            WarehouseId = request.WarehouseId,
            AdjustmentType = request.AdjustmentType,
            ReasonType = request.ReasonType,
            Status = InventoryAdjustmentDocumentStatus.Draft,
            Note = NormalizeText(request.Note)
        };

        foreach (var requestLine in request.Lines)
        {
            var line = await BuildLineAsync(
                requestLine,
                request.AdjustmentType,
                ct);

            document.Lines.Add(line);
        }

        await _repository.AddAsync(document, ct);

        // SaveChanges do UnitOfWork xử lý nếu project bạn có UnitOfWork.
        // Nếu service khác trong project gọi repository rồi unitOfWork.SaveChangesAsync,
        // hãy giữ đúng pattern đó.
        await _unitOfWork.SaveChangesAsync(ct);

        var detail = await _repository.GetDetailDtoAsync(document.Id, ct);
        if (detail == null)
            throw new InvalidOperationException("Không tải được phiếu điều chỉnh vừa tạo.");

        return detail;
    }

    public async Task<InventoryAdjustmentDocumentDetailDto> UpdateAsync(
        UpdateInventoryAdjustmentDocumentRequest request,
        CancellationToken ct = default)
    {
        var validation = await _updateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            throw new ValidationException(validation.Errors);

        var document = await _repository.GetDetailEntityAsync(request.Id, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu điều chỉnh không tồn tại.");

        if (document.Status != InventoryAdjustmentDocumentStatus.Draft)
            throw new InvalidOperationException("Chỉ được sửa phiếu điều chỉnh ở trạng thái nháp.");

        await EnsureWarehouseExistsAsync(request.WarehouseId, ct);

        document.DocumentDate = request.DocumentDate ?? document.DocumentDate;
        document.WarehouseId = request.WarehouseId;
        document.AdjustmentType = request.AdjustmentType;
        document.ReasonType = request.ReasonType;
        document.Note = NormalizeText(request.Note);

        // Xóa line cũ và tạo lại line mới để tránh lệch dữ liệu quy đổi.
        document.Lines.Clear();

        foreach (var requestLine in request.Lines)
        {
            var line = await BuildLineAsync(
                requestLine,
                request.AdjustmentType,
                ct);

            document.Lines.Add(line);
        }

        _repository.Update(document);

        await _unitOfWork.SaveChangesAsync(ct);

        var detail = await _repository.GetDetailDtoAsync(document.Id, ct);
        if (detail == null)
            throw new InvalidOperationException("Không tải được phiếu điều chỉnh sau khi cập nhật.");

        return detail;
    }

    public async Task<InventoryAdjustmentDocumentDetailDto> SubmitAsync(
        SubmitInventoryAdjustmentDocumentRequest request,
        CancellationToken ct = default)
    {
        if (request.Id <= 0)
            throw new InvalidOperationException("Phiếu điều chỉnh không hợp lệ.");

        var document = await _repository.GetDetailEntityAsync(request.Id, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu điều chỉnh không tồn tại.");

        if (document.Status != InventoryAdjustmentDocumentStatus.Draft)
            throw new InvalidOperationException("Chỉ phiếu nháp mới được gửi duyệt.");

        if (document.Lines == null || document.Lines.Count == 0)
            throw new InvalidOperationException("Phiếu điều chỉnh phải có ít nhất 1 dòng sản phẩm.");

        document.Status = InventoryAdjustmentDocumentStatus.PendingApproval;
        document.SubmittedAtUtc = DateTime.UtcNow;

        _repository.Update(document);

        await _unitOfWork.SaveChangesAsync(ct);

        var detail = await _repository.GetDetailDtoAsync(document.Id, ct);
        if (detail == null)
            throw new InvalidOperationException("Không tải được phiếu điều chỉnh sau khi gửi duyệt.");

        return detail;
    }

    public async Task DeleteDraftAsync(
        int id,
        CancellationToken ct = default)
    {
        if (id <= 0)
            throw new InvalidOperationException("Phiếu điều chỉnh không hợp lệ.");

        var document = await _repository.GetDetailEntityAsync(id, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu điều chỉnh không tồn tại.");

        if (document.Status != InventoryAdjustmentDocumentStatus.Draft)
            throw new InvalidOperationException("Chỉ được xóa phiếu nháp.");

        _repository.Remove(document);

        await _unitOfWork.SaveChangesAsync(ct);
    }

    public Task<InventoryAdjustmentDocumentDetailDto?> GetDetailAsync(
        int id,
        CancellationToken ct = default)
    {
        return _repository.GetDetailDtoAsync(id, ct);
    }

    public Task<PagedResult<InventoryAdjustmentDocumentListItemDto>> GetPagedAsync(
        InventoryAdjustmentDocumentFilterDto filter,
        CancellationToken ct = default)
    {
        return _repository.GetPagedAsync(filter, ct);
    }

    /// <summary>
    /// Build line từ request.
    /// Backend tự resolve đơn vị và tự tính BaseQuantity,
    /// không tin Factor/BaseQuantity từ client.
    /// </summary>
    private async Task<InventoryAdjustmentLine> BuildLineAsync(
       InventoryAdjustmentLineRequestDto requestLine,
       InventoryTransactionType adjustmentType,
       CancellationToken ct)
    {
        var unitInfo = await _inventoryUnitResolver.ResolveAsync(
            requestLine.ProductVariantId,
            requestLine.UnitId,
            ct);

        var factor = unitInfo.Factor <= 0 ? 1m : unitInfo.Factor;
        var quantity = requestLine.Quantity;
        var baseQuantity = quantity * factor;

        return new InventoryAdjustmentLine
        {
            ProductVariantId = requestLine.ProductVariantId,
            UnitId = unitInfo.UnitId,
            ProductUnitConversionId = requestLine.ProductUnitConversionId,
            Quantity = quantity,
            Factor = factor,
            BaseQuantity = baseQuantity,

            // Không bắt nhân viên kho nhập giá vốn.
            // Nếu có nhập thì lưu lại, nếu không có thì lúc duyệt tự đề xuất.
            UnitCost = adjustmentType == InventoryTransactionType.AdjustmentIncrease
                ? requestLine.UnitCost
                : null,

            ProvisionalUnitCost = adjustmentType == InventoryTransactionType.AdjustmentDecrease
                ? requestLine.ProvisionalUnitCost
                : null,

            Note = NormalizeText(requestLine.Note)
        };
    }

    private async Task EnsureWarehouseExistsAsync(
        int warehouseId,
        CancellationToken ct)
    {
        var warehouse = await _warehouseRepository.GetByIdAsync(warehouseId, ct);
        if (warehouse == null)
            throw new InvalidOperationException("Kho không tồn tại.");

        if (!warehouse.IsActive)
            throw new InvalidOperationException("Kho đã ngưng hoạt động.");
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    public async Task<InventoryAdjustmentDocumentDetailDto> ApproveAsync(
    ApproveInventoryAdjustmentDocumentRequest request,
    CancellationToken ct = default)
    {
        if (request.Id <= 0)
            throw new InvalidOperationException("Phiếu điều chỉnh không hợp lệ.");

        var document = await _repository.GetDetailEntityAsync(request.Id, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu điều chỉnh không tồn tại.");

        if (document.Status == InventoryAdjustmentDocumentStatus.Approved)
        {
            return await _repository.GetDetailDtoAsync(document.Id, ct)
                ?? throw new InvalidOperationException(
                    "Không tải được phiếu điều chỉnh đã duyệt.");
        }

        if (document.Status != InventoryAdjustmentDocumentStatus.PendingApproval)
            throw new InvalidOperationException("Chỉ phiếu đang chờ duyệt mới được duyệt.");

        if (document.Lines == null || document.Lines.Count == 0)
            throw new InvalidOperationException("Phiếu điều chỉnh phải có ít nhất 1 dòng sản phẩm.");

        var documentReferenceId =
            document.Id.ToString(CultureInfo.InvariantCulture);
        var occurredAtUtc = DateTime.UtcNow;
        var movementRequests = new List<CreateInventoryMovementRequest>();

        foreach (var line in document.Lines)
        {
            var note = _inventoryMovementNoteBuilder.BuildAdjustmentNote(
                line.Note ?? document.Note,
                line.Unit?.Name ?? string.Empty,
                line.Quantity,
                line.Factor,
                line.BaseQuantity);

            CreateInventoryMovementRequest movementRequest;

            if (document.AdjustmentType == InventoryTransactionType.AdjustmentIncrease)
            {
                var finalUnitCost = line.UnitCost;

                if (!finalUnitCost.HasValue || finalUnitCost.Value <= 0)
                {
                    var suggestion = await _costSuggestionService.GetSuggestedCostAsync(
                        line.ProductVariantId,
                        ct);

                    if (!suggestion.HasValidCost)
                    {
                        var productName =
                            line.ProductVariant?.ProductVariantName
                            ?? line.ProductVariant?.Product?.Name
                            ?? $"VariantId={line.ProductVariantId}";

                        throw new InvalidOperationException(
                            $"Sản phẩm '{productName}' chưa có giá vốn hợp lệ. Vui lòng cập nhật giá vốn hoặc nhập giá vốn khi duyệt.");
                    }

                    finalUnitCost = suggestion.UnitCost!.Value;
                }

                movementRequest = _inventoryMovementFactory.CreateAdjustmentIncrease(
                    document.WarehouseId,
                    line.ProductVariantId,
                    line.BaseQuantity,
                    finalUnitCost.Value,
                    documentReferenceId,
                    line.Id,
                    note,
                    occurredAtUtc);
            }
            else if (document.AdjustmentType == InventoryTransactionType.AdjustmentDecrease)
            {
                movementRequest = _inventoryMovementFactory.CreateAdjustmentDecrease(
                    document.WarehouseId,
                    line.ProductVariantId,
                    line.BaseQuantity,
                    line.ProvisionalUnitCost,
                    documentReferenceId,
                    line.Id,
                    note,
                    occurredAtUtc);
            }
            else
            {
                throw new InvalidOperationException("Loại điều chỉnh không hợp lệ.");
            }

            movementRequests.Add(movementRequest);
        }

        await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            await _inventoryMovementService.PreLockBalancesAsync(
                movementRequests.Select(movement => new InventoryPostingLockKey(
                    document.StoreId,
                    movement.WarehouseId,
                    movement.ProductVariantId)),
                ct);

            foreach (var movementRequest in movementRequests)
            {
                await _inventoryMovementService.CreateAsync(
                    movementRequest,
                    ct);
            }

            document.Status = InventoryAdjustmentDocumentStatus.Approved;
            document.ApprovedAtUtc = occurredAtUtc;
            document.ApprovalNote = NormalizeText(request.ApprovalNote);

            _repository.Update(document);

            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(
                CancellationToken.None);
            throw;
        }

        var detail = await _repository.GetDetailDtoAsync(document.Id, ct);
        if (detail == null)
            throw new InvalidOperationException("Không tải được phiếu sau khi duyệt.");

        return detail;
    }
    public async Task<InventoryAdjustmentDocumentDetailDto> RejectAsync(
    RejectInventoryAdjustmentDocumentRequest request,
    CancellationToken ct = default)
    {
        if (request.Id <= 0)
            throw new InvalidOperationException("Phiếu điều chỉnh không hợp lệ.");

        var document = await _repository.GetDetailEntityAsync(request.Id, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu điều chỉnh không tồn tại.");

        if (document.Status != InventoryAdjustmentDocumentStatus.PendingApproval)
            throw new InvalidOperationException("Chỉ phiếu đang chờ duyệt mới được từ chối.");

        document.Status = InventoryAdjustmentDocumentStatus.Rejected;
        document.RejectedAtUtc = DateTime.UtcNow;
        document.ApprovalNote = NormalizeText(request.ApprovalNote);

        _repository.Update(document);

        await _unitOfWork.SaveChangesAsync(ct);

        var detail = await _repository.GetDetailDtoAsync(document.Id, ct);
        if (detail == null)
            throw new InvalidOperationException("Không tải được phiếu sau khi từ chối.");

        return detail;
    }
    public async Task<InventoryAdjustmentDocumentDetailDto> CancelAsync(
    CancelInventoryAdjustmentDocumentRequest request,
    CancellationToken ct = default)
    {
        if (request.Id <= 0)
            throw new InvalidOperationException("Phiếu điều chỉnh không hợp lệ.");

        var document = await _repository.GetDetailEntityAsync(request.Id, ct);
        if (document == null)
            throw new InvalidOperationException("Phiếu điều chỉnh không tồn tại.");

        if (document.Status == InventoryAdjustmentDocumentStatus.Approved)
            throw new InvalidOperationException("Phiếu đã duyệt không được hủy.");

        if (document.Status == InventoryAdjustmentDocumentStatus.Cancelled)
            throw new InvalidOperationException("Phiếu đã được hủy trước đó.");

        document.Status = InventoryAdjustmentDocumentStatus.Cancelled;
        document.CancelledAtUtc = DateTime.UtcNow;
        document.ApprovalNote = NormalizeText(request.CancelNote);

        _repository.Update(document);

        await _unitOfWork.SaveChangesAsync(ct);

        var detail = await _repository.GetDetailDtoAsync(document.Id, ct);
        if (detail == null)
            throw new InvalidOperationException("Không tải được phiếu sau khi hủy.");

        return detail;
    }
}
