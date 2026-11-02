using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public sealed class InvoiceOrderDraftService(
    IInvoiceOrderDraftRepository drafts,
    IInvoiceRepository invoices,
    IOrderRepository orders,
    IOrderLegalEntityAllocationRepository allocations,
    ILegalEntityRepository legalEntities,
    IInvoiceInputStockRepository inputStock,
    IAppUnitOfWork unitOfWork,
    ITenantContext tenant,
    ICurrentUser currentUser,
    ICurrentStorePermissionService permissions,
    TimeProvider clock) : IInvoiceOrderDraftService
{
    public async Task<Result<List<InvoiceOrderDraftOrderDto>>> SearchAsync(string? term, CancellationToken ct = default)
    {
        var error = ActorError();
        if (error != null) return Result<List<InvoiceOrderDraftOrderDto>>.Failure(error);
        term = term?.Trim() ?? "";
        if (term.Length > 100)
            return Result<List<InvoiceOrderDraftOrderDto>>.Failure(Error.Validation("Nội dung tìm kiếm tối đa 100 ký tự."));
        if (term.Length < 2 && !int.TryParse(term, out _))
            return Result<List<InvoiceOrderDraftOrderDto>>.Success([]);
        return Result<List<InvoiceOrderDraftOrderDto>>.Success(await drafts.SearchAsync(tenant.StoreId!.Value, term, ct));
    }

    public async Task<Result<InvoiceOrderDraftResultDto>> PreviewAsync(int orderId, CancellationToken ct = default)
    {
        var error = ActorError();
        if (error != null) return Result<InvoiceOrderDraftResultDto>.Failure(error);
        var order = await drafts.GetOrderAsync(tenant.StoreId!.Value, orderId, false, ct);
        if (order == null) return Result<InvoiceOrderDraftResultDto>.Failure(Error.NotFound("Không tìm thấy đơn POS trong cửa hàng này."));
        var existing = await drafts.GetExistingAsync(order.StoreId, order.Id, ct);
        if (existing.Count == 0 && order.Status != OrderStatus.Completed)
            return Result<InvoiceOrderDraftResultDto>.Failure(Error.Validation("Chỉ tạo hóa đơn từ đơn POS đã hoàn tất."));
        return Result<InvoiceOrderDraftResultDto>.Success(Map(order, existing));
    }

    public async Task<Result<InvoiceOrderDraftResultDto>> CreateAsync(int orderId, CancellationToken ct = default)
    {
        var error = ActorError();
        if (error != null) return Result<InvoiceOrderDraftResultDto>.Failure(error);
        var storeId = tenant.StoreId!.Value;
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        // Share the issuance lock before reading order/header state or changing the route.
        await inputStock.LockStoreForIssueAsync(storeId, ct);
        var order = await drafts.GetOrderAsync(storeId, orderId, true, ct);
        if (order == null) return Result<InvoiceOrderDraftResultDto>.Failure(Error.NotFound("Không tìm thấy đơn POS trong cửa hàng này."));
        var existing = await drafts.GetExistingAsync(storeId, orderId, ct);
        if (existing.Count > 0)
            return Result<InvoiceOrderDraftResultDto>.Success(Map(order, existing));
        if (order.Status != OrderStatus.Completed)
            return Result<InvoiceOrderDraftResultDto>.Failure(Error.Validation("Chỉ tạo hóa đơn từ đơn POS đã hoàn tất."));

        if (order.InvoiceIssuanceRoute == InvoiceIssuanceRoute.Automatic &&
            !await permissions.HasPermissionAsync(storeId, currentUser.UserId!.Value, PermissionCodes.System.Invoice.Route, ct))
            return Result<InvoiceOrderDraftResultDto>.Failure(Error.Validation(
                "Đơn đang phát hành tự động. Cần quyền chuyển phương thức phát hành để tạo hóa đơn thủ công."));

        var persisted = await allocations.GetForOrderAsync(orderId, ct);
        if (persisted.Any(x => x.StoreId != storeId))
            return Result<InvoiceOrderDraftResultDto>.Failure(Error.Conflict("Phân bổ HKD của đơn không thuộc cửa hàng hiện tại."));
        if (persisted.Count == 0 && (order.HasMultipleLegalEntities || order.LegalEntityCount > 0))
            return Result<InvoiceOrderDraftResultDto>.Failure(Error.Conflict("Đơn có HKD nhưng thiếu dữ liệu phân bổ. Vui lòng kiểm tra trước khi tạo hóa đơn."));

        var heads = new List<InvoiceHead>();
        var entityIds = persisted.Select(x => x.LegalEntityId).Distinct().OrderBy(x => x).ToList();
        if (entityIds.Count == 0)
        {
            var setting = await drafts.GetProviderMetadataAsync(storeId, null, ct);
            error = InvoiceCommandService.ValidateProviderSetting(setting, null);
            if (error != null) return Result<InvoiceOrderDraftResultDto>.Failure(error);
            heads.Add(InvoiceCommandService.BuildInvoiceHead(order, setting!, null));
        }
        else
        {
            // Validate every HKD before inserting any header. Never generate OrderLine details here.
            foreach (var entityId in entityIds)
            {
                var entity = await legalEntities.GetByIdAsync(entityId, ct);
                if (entity == null || entity.StoreId != storeId || !entity.IsActive || !entity.InvoiceProviderSettingId.HasValue)
                    return Result<InvoiceOrderDraftResultDto>.Failure(Error.Conflict("HKD của đơn chưa có cấu hình phát hành hợp lệ."));
                var setting = await drafts.GetProviderMetadataAsync(storeId, entity.InvoiceProviderSettingId, ct);
                error = InvoiceCommandService.ValidateProviderSetting(setting, entity);
                if (error != null) return Result<InvoiceOrderDraftResultDto>.Failure(error);
                heads.Add(InvoiceCommandService.BuildInvoiceHead(order, setting!, entity));
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var head in heads)
        {
            head.Note = $"Tạo hóa đơn trống thủ công từ đơn POS #{orderId}.";
            head.LastIssuanceRelevantChangeAtUtc = now;
        }
        if (order.InvoiceIssuanceRoute != InvoiceIssuanceRoute.Manual)
        {
            order.InvoiceIssuanceRoute = InvoiceIssuanceRoute.Manual;
            order.InvoiceIssuanceRouteSelectedAtUtc = now;
            order.InvoiceIssuanceRouteSelectedByUserId = currentUser.UserId;
            orders.Update(order);
        }
        await invoices.AddInvoiceHeadsAsync(heads, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        var result = Map(order, heads.Select(x => new InvoiceOrderDraftReferenceDto(x.Id, x.LegalEntity?.Name)).ToList());
        result.Created = true;
        return Result<InvoiceOrderDraftResultDto>.Success(result);
    }

    private Error? ActorError()
        => tenant.StoreId is not > 0 || !currentUser.IsAuthenticated || currentUser.UserId is not > 0
            ? Error.Failure("Chưa xác định được cửa hàng hoặc người dùng hiện tại.") : null;

    private static InvoiceOrderDraftResultDto Map(Order order, List<InvoiceOrderDraftReferenceDto> references)
        => new()
        {
            Order = new() { OrderId = order.Id, OrderNumber = order.OrderNumber,
                CompletedAtUtc = order.CompletedAtUtc, GrandTotal = order.GrandTotal, HasInvoice = references.Count > 0 },
            Invoices = references
        };
}
