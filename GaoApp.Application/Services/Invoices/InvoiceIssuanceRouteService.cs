using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Results;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public sealed class InvoiceIssuanceRouteService
    : IInvoiceIssuanceRouteService
{
    private readonly IOrderRepository _orders;
    private readonly IInvoiceRepository _invoices;
    private readonly IAutoInvoiceRepository _autoInvoices;
    private readonly IInvoiceInputStockRepository _inputStock;
    private readonly IAppUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStorePermissionService _permissions;
    private readonly TimeProvider _clock;

    public InvoiceIssuanceRouteService(
        IOrderRepository orders,
        IInvoiceRepository invoices,
        IAutoInvoiceRepository autoInvoices,
        IInvoiceInputStockRepository inputStock,
        IAppUnitOfWork unitOfWork,
        ITenantContext tenant,
        ICurrentUser currentUser,
        ICurrentStorePermissionService permissions,
        TimeProvider clock)
    {
        _orders = orders;
        _invoices = invoices;
        _autoInvoices = autoInvoices;
        _inputStock = inputStock;
        _unitOfWork = unitOfWork;
        _tenant = tenant;
        _currentUser = currentUser;
        _permissions = permissions;
        _clock = clock;
    }

    public async Task<Result<InvoiceIssuanceRouteDto>> SetInitialRouteAsync(
        int orderId,
        InvoiceIssuanceRoute route,
        CancellationToken ct = default)
    {
        var routeValidation = ValidateTargetRoute(route);
        if (!routeValidation.IsSuccess)
        {
            return Result<InvoiceIssuanceRouteDto>.Failure(
                routeValidation.Error);
        }

        var context = ValidateCurrentActor();
        if (!context.IsSuccess)
        {
            return Result<InvoiceIssuanceRouteDto>.Failure(
                context.Error);
        }

        await using var tx =
            await _unitOfWork.BeginTransactionAsync(ct);

        await _inputStock.LockStoreForIssueAsync(context.Value.StoreId, ct);
        var order = await _orders.GetForInvoiceRouteChangeAsync(context.Value.StoreId, orderId, ct);
        if (order == null)
        {
            await tx.RollbackAsync(ct);

            return Result<InvoiceIssuanceRouteDto>.Failure(
                Error.NotFound("Không tìm thấy đơn hàng."));
        }

        if (order.StoreId != context.Value.StoreId)
        {
            await tx.RollbackAsync(ct);

            return Result<InvoiceIssuanceRouteDto>.Failure(
                Error.NotFound("Không tìm thấy đơn hàng trong cửa hàng hiện tại."));
        }

        if (order.Status != OrderStatus.Completed)
        {
            await tx.RollbackAsync(ct);

            return Result<InvoiceIssuanceRouteDto>.Failure(
                Error.Conflict(
                    "Chỉ được chọn phương thức phát hành sau khi đơn đã hoàn tất thanh toán."));
        }

        // Retry cùng lựa chọn phải idempotent.
        if (order.InvoiceIssuanceRoute == route)
        {
            await tx.CommitAsync(ct);

            return Result<InvoiceIssuanceRouteDto>.Success(
                Map(order));
        }

        if (order.InvoiceIssuanceRoute !=
            InvoiceIssuanceRoute.Unselected)
        {
            await tx.RollbackAsync(ct);

            return Result<InvoiceIssuanceRouteDto>.Failure(
                Error.Conflict(
                    "Đơn hàng đã có phương thức phát hành. Muốn thay đổi phải dùng chức năng chuyển phương thức dành cho quản lý."));
        }

        order.InvoiceIssuanceRoute = route;
        order.InvoiceIssuanceRouteSelectedAtUtc =
            _clock.GetUtcNow().UtcDateTime;
        order.InvoiceIssuanceRouteSelectedByUserId =
            context.Value.UserId;

        _orders.Update(order);

        await _unitOfWork.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Result<InvoiceIssuanceRouteDto>.Success(
            Map(order));
    }

    public async Task<Result<InvoiceIssuanceRouteDto>> ChangeRouteAsync(
        int orderId,
        InvoiceIssuanceRoute route,
        CancellationToken ct = default)
    {
        var routeValidation = ValidateTargetRoute(route);
        if (!routeValidation.IsSuccess)
        {
            return Result<InvoiceIssuanceRouteDto>.Failure(
                routeValidation.Error);
        }

        var context = ValidateCurrentActor();
        if (!context.IsSuccess)
        {
            return Result<InvoiceIssuanceRouteDto>.Failure(
                context.Error);
        }

        var hasPermission =
            await _permissions.HasPermissionAsync(
                context.Value.StoreId,
                context.Value.UserId,
                PermissionCodes.System.Invoice.Route,
                ct);

        if (!hasPermission)
        {
            return Result<InvoiceIssuanceRouteDto>.Failure(
                Error.Failure(
                    "Bạn không có quyền thay đổi phương thức phát hành hóa đơn."));
        }

        await using var tx =
            await _unitOfWork.BeginTransactionAsync(ct);

        // Serialize the route decision with Single/Group claims, then read committed state.
        await _inputStock.LockStoreForIssueAsync(context.Value.StoreId, ct);
        var order = await _orders.GetForInvoiceRouteChangeAsync(context.Value.StoreId, orderId, ct);
        if (order == null ||
            order.StoreId != context.Value.StoreId)
        {
            await tx.RollbackAsync(ct);

            return Result<InvoiceIssuanceRouteDto>.Failure(
                Error.NotFound("Không tìm thấy đơn hàng."));
        }

        if (order.Status != OrderStatus.Completed)
        {
            await tx.RollbackAsync(ct);

            return Result<InvoiceIssuanceRouteDto>.Failure(
                Error.Conflict(
                    "Đơn hàng hiện không ở trạng thái cho phép đổi phương thức phát hành."));
        }

        if (order.InvoiceIssuanceRoute ==
            InvoiceIssuanceRoute.Unselected)
        {
            await tx.RollbackAsync(ct);

            return Result<InvoiceIssuanceRouteDto>.Failure(
                Error.Conflict(
                    "Đơn hàng chưa có lựa chọn phát hành ban đầu."));
        }

        if (order.InvoiceIssuanceRoute == route)
        {
            await tx.CommitAsync(ct);

            return Result<InvoiceIssuanceRouteDto>.Success(
                Map(order));
        }

        var heads =
            await _invoices
                .GetOriginalInvoiceHeadsWithDetailsByOrderIdAsync(
                    order.Id,
                    ct);

        foreach (var head in heads)
        {
            var currentHead = await _invoices.GetInvoiceHeadByIdAsync(head.Id, ct);
            if (currentHead == null || currentHead.StoreId != order.StoreId ||
                !InvoiceIssuanceStatePolicy.CanChangeRoute(currentHead))
            {
                await tx.RollbackAsync(ct);

                return Result<InvoiceIssuanceRouteDto>.Failure(
                    Error.Conflict(
                        $"Hóa đơn #{head.Id} đang ở trạng thái không cho phép đổi phương thức phát hành."));
            }

            if (await _autoInvoices.HasActiveSourceAsync(
                    order.StoreId,
                    head.Id,
                    ct) ||
                await _autoInvoices.HasSuccessfulSourceAsync(order.StoreId, head.Id, ct))
            {
                await tx.RollbackAsync(ct);

                return Result<InvoiceIssuanceRouteDto>.Failure(
                    Error.Conflict(
                        $"Hóa đơn #{head.Id} đang được hàng đợi phát hành xử lý. Vui lòng chờ xử lý hoàn tất."));
            }
        }

        order.InvoiceIssuanceRoute = route;
        order.InvoiceIssuanceRouteSelectedAtUtc =
            _clock.GetUtcNow().UtcDateTime;
        order.InvoiceIssuanceRouteSelectedByUserId =
            context.Value.UserId;

        _orders.Update(order);

        await _unitOfWork.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Result<InvoiceIssuanceRouteDto>.Success(
            Map(order));
    }

    private Result<(int StoreId, int UserId)> ValidateCurrentActor()
    {
        if (_tenant.StoreId is not > 0)
        {
            return Result<(int, int)>.Failure(
                Error.Failure(
                    "Chưa xác định được cửa hàng hiện tại."));
        }

        if (!_currentUser.IsAuthenticated ||
            _currentUser.UserId is not > 0)
        {
            return Result<(int, int)>.Failure(
                Error.Failure(
                    "Chưa xác định được người dùng hiện tại."));
        }

        return Result<(int, int)>.Success(
            (_tenant.StoreId.Value, _currentUser.UserId.Value));
    }

    private static Result ValidateTargetRoute(
        InvoiceIssuanceRoute route)
    {
        if (route is not
            (InvoiceIssuanceRoute.Automatic or
             InvoiceIssuanceRoute.Manual))
        {
            return Result.Failure(
                Error.Validation(
                    "InvoiceIssuance.RouteInvalid",
                    "Phương thức phát hành hóa đơn không hợp lệ."));
        }

        return Result.Success();
    }

    private static InvoiceIssuanceRouteDto Map(Order order)
        => new()
        {
            OrderId = order.Id,
            Route = order.InvoiceIssuanceRoute,
            SelectedAtUtc =
                order.InvoiceIssuanceRouteSelectedAtUtc,
            SelectedByUserId =
                order.InvoiceIssuanceRouteSelectedByUserId
        };
}
