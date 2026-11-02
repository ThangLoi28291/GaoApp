using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Security.Cryptography;
using System.Text;

namespace GaoApp.Application.Services.Invoices;

public sealed class InvoiceBuyerSelfServiceService(
    IInvoiceBuyerSelfServiceRepository requests,
    IOrderRepository orders,
    IInvoiceBuyerService buyers,
    IAppUnitOfWork unitOfWork,
    ITenantContext tenant,
    TimeProvider clock) : IInvoiceBuyerSelfServiceService
{
    private static readonly TimeSpan SelfServiceWindow = TimeSpan.FromHours(2);
    private const string ClosedMessage = "Liên kết nhập thông tin hóa đơn không còn khả dụng. Vui lòng liên hệ cửa hàng.";
    private const string ExpiredMessage = "Thời gian tự cập nhật thông tin hóa đơn đã kết thúc. Vui lòng liên hệ cửa hàng.";
    private const string PreparingMessage = "Hóa đơn đang được chuẩn bị. Vui lòng thử lại sau.";

    public Task<Result<InvoiceBuyerSelfServiceLinkDto>> CreateLinkAsync(int orderId, CancellationToken ct = default)
        => CreateAsync(orderId, replace: false, ct);

    public Task<Result<InvoiceBuyerSelfServiceLinkDto>> ReplaceLinkAsync(int orderId, CancellationToken ct = default)
        => CreateAsync(orderId, replace: true, ct);

    private async Task<Result<InvoiceBuyerSelfServiceLinkDto>> CreateAsync(int orderId, bool replace, CancellationToken ct)
    {
        if (tenant.StoreId is not > 0)
            return Result<InvoiceBuyerSelfServiceLinkDto>.Failure(NotFound());

        await using var tx = await unitOfWork.BeginTransactionAsync(ct);
        await requests.LockStoreAsync(tenant.StoreId.Value, ct);
        var order = await orders.GetForInvoiceRouteChangeAsync(tenant.StoreId.Value, orderId, ct);
        if (order == null || order.StoreId != tenant.StoreId.Value)
            return Result<InvoiceBuyerSelfServiceLinkDto>.Failure(NotFound());

        if (order.Status != OrderStatus.Completed || !order.CompletedAtUtc.HasValue ||
            order.InvoiceIssuanceRoute != InvoiceIssuanceRoute.Manual)
            return Result<InvoiceBuyerSelfServiceLinkDto>.Failure(new Error("Closed", ClosedMessage));

        var expiresAtUtc = Utc(order.CompletedAtUtc.Value) + SelfServiceWindow;
        if (clock.GetUtcNow().UtcDateTime >= expiresAtUtc)
            return Result<InvoiceBuyerSelfServiceLinkDto>.Failure(new Error("Closed", ExpiredMessage));

        var heads = await requests.GetHeadsForAccessAsync(order.StoreId, orderId, ct);
        if (heads.Any(x => x.StoreId != order.StoreId) ||
            heads.Any(x => !x.IsAutoInvoiceGroup && !InvoiceIssuanceStatePolicy.CanEditBuyerInformation(x)))
            return Result<InvoiceBuyerSelfServiceLinkDto>.Failure(new Error("Closed", ClosedMessage));

        if (replace)
            await requests.RevokeForOrderAsync(order.StoreId, order.Id, clock.GetUtcNow().UtcDateTime, ct);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await requests.AddAsync(new InvoiceBuyerSelfServiceRequest
        {
            StoreId = order.StoreId,
            OrderId = order.Id,
            TokenHash = HashToken(token),
            ExpiresAtUtc = expiresAtUtc
        }, ct);
        await requests.SaveChangesAsync(ct);
        if (clock.GetUtcNow().UtcDateTime >= expiresAtUtc)
        {
            await tx.RollbackAsync(ct);
            return Result<InvoiceBuyerSelfServiceLinkDto>.Failure(new Error("Closed", ExpiredMessage));
        }
        await tx.CommitAsync(ct);
        return Result<InvoiceBuyerSelfServiceLinkDto>.Success(new()
        {
            OrderId = order.Id, Token = token, ExpiresAtUtc = expiresAtUtc
        });
    }

    public async Task<Result<InvoiceBuyerSelfServiceViewDto>> GetAsync(string token, CancellationToken ct = default)
    {
        var access = await ResolveAsync(token, ct);
        return access.IsSuccess
            ? Result<InvoiceBuyerSelfServiceViewDto>.Success(BuildView(access.Value))
            : Result<InvoiceBuyerSelfServiceViewDto>.Failure(access.Error);
    }

    public async Task<Result<InvoiceBuyerSelfServiceViewDto>> SubmitAsync(
        string token, SubmitInvoiceBuyerSelfServiceRequest request, CancellationToken ct = default)
    {
        if (!ValidToken(token) || tenant.StoreId is not > 0)
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(NotFound());

        // Resolve only after the store lock; issuance, route changes and explicit replacement
        // cannot race a public update. Re-check expiry after waiting and before commit.
        await using var tx = await unitOfWork.BeginTransactionAsync(ct);
        await requests.LockStoreAsync(tenant.StoreId.Value, ct);
        var resolved = await ResolveAsync(token, ct);
        if (!resolved.IsSuccess)
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(resolved.Error);
        var access = resolved.Value;
        var view = BuildView(access);
        if (!view.CanView)
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(Error.Conflict(view.PublicMessage ?? ClosedMessage));

        var first = access.Heads.OrderBy(x => x.LegalEntityId).ThenBy(x => x.Id).First();
        var update = await buyers.UpdateBuyerInfoAsync(new UpdateInvoiceBuyerInfoRequest
        {
            InvoiceHeadId = first.Id,
            BuyerType = request.BuyerType,
            BuyerName = request.BuyerName,
            BuyerLegalName = request.BuyerLegalName,
            BuyerTaxCode = request.BuyerTaxCode,
            BuyerCitizenId = request.BuyerCitizenId,
            ClearBuyerCitizenId = string.IsNullOrWhiteSpace(request.BuyerCitizenId),
            BuyerAddress = request.BuyerAddress,
            BuyerEmail = request.BuyerEmail,
            BuyerPhone = request.BuyerPhone,
            SaveToProfile = false,
            Source = "qr-self-service"
        }, ct);
        if (!update.IsSuccess)
        {
            await tx.RollbackAsync(ct);
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(update.Error);
        }
        if (clock.GetUtcNow().UtcDateTime >= access.ExpiresAtUtc)
        {
            await tx.RollbackAsync(ct);
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(Error.Conflict(ExpiredMessage));
        }
        await requests.MarkSubmittedAsync(access.Request.StoreId, access.Request.Id, clock.GetUtcNow().UtcDateTime, ct);
        await requests.SaveChangesAsync(ct);
        if (clock.GetUtcNow().UtcDateTime >= access.ExpiresAtUtc)
        {
            await tx.RollbackAsync(ct);
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(Error.Conflict(ExpiredMessage));
        }
        await tx.CommitAsync(ct);
        return await GetAsync(token, ct);
    }

    private async Task<Result<ResolvedAccess>> ResolveAsync(string token, CancellationToken ct)
    {
        if (!ValidToken(token) || tenant.StoreId is not > 0)
            return Result<ResolvedAccess>.Failure(NotFound());
        var entity = await requests.GetByTokenHashAsync(HashToken(token.Trim()), ct);
        if (entity == null || entity.RevokedAtUtc.HasValue || entity.Order == null ||
            entity.StoreId != tenant.StoreId.Value || entity.StoreId != entity.Order.StoreId)
            return Result<ResolvedAccess>.Failure(NotFound());

        // Old links also obey the original sale deadline, even if their stored expiry is longer.
        var expiresAtUtc = Utc(entity.ExpiresAtUtc);
        if (entity.Order.CompletedAtUtc.HasValue)
        {
            var saleDeadline = Utc(entity.Order.CompletedAtUtc.Value) + SelfServiceWindow;
            if (saleDeadline < expiresAtUtc) expiresAtUtc = saleDeadline;
        }
        var unavailable = clock.GetUtcNow().UtcDateTime >= expiresAtUtc ? ExpiredMessage :
            entity.Order.Status != OrderStatus.Completed || !entity.Order.CompletedAtUtc.HasValue ||
            entity.Order.InvoiceIssuanceRoute != InvoiceIssuanceRoute.Manual ? ClosedMessage : null;
        if (unavailable != null)
            return Result<ResolvedAccess>.Success(new(entity, entity.Order, [], expiresAtUtc, unavailable));

        // Never load buyer heads for an expired, cancelled or rerouted request.
        var heads = await requests.GetHeadsForAccessAsync(entity.StoreId, entity.OrderId, ct);
        if (heads.Any(x => x.StoreId != entity.StoreId))
            return Result<ResolvedAccess>.Failure(NotFound());
        heads = heads.Where(x => !x.IsAutoInvoiceGroup && !x.IsDeleted).ToList();
        unavailable = heads.Count == 0 ? PreparingMessage :
            heads.Any(x => !InvoiceIssuanceStatePolicy.CanEditBuyerInformation(x)) ? ClosedMessage : null;
        return Result<ResolvedAccess>.Success(new(entity, entity.Order, heads, expiresAtUtc, unavailable));
    }

    private InvoiceBuyerSelfServiceViewDto BuildView(ResolvedAccess access)
    {
        var expired = clock.GetUtcNow().UtcDateTime >= access.ExpiresAtUtc;
        if (expired || access.UnavailableMessage != null)
            return new() { IsExpired = expired, PublicMessage = expired ? ExpiredMessage : access.UnavailableMessage };

        var head = access.Heads.OrderBy(x => x.LegalEntityId).ThenBy(x => x.Id).First();
        return new()
        {
            CanView = true, CanEdit = true,
            OrderId = access.Order.Id, OrderNumber = access.Order.OrderNumber,
            CompletedAtUtc = access.Order.CompletedAtUtc.HasValue ? Utc(access.Order.CompletedAtUtc.Value) : null,
            GrandTotal = access.Order.GrandTotal, ExpiresAtUtc = access.ExpiresAtUtc,
            BuyerType = string.IsNullOrWhiteSpace(head.BuyerType) || head.BuyerType == InvoiceBuyerTypes.NoInvoice
                ? InvoiceBuyerTypes.Individual : head.BuyerType,
            BuyerName = head.BuyerName, BuyerLegalName = head.BuyerLegalName,
            BuyerTaxCode = head.BuyerTaxCode, BuyerCitizenId = head.BuyerCitizenId,
            BuyerAddress = head.BuyerAddress, BuyerEmail = head.BuyerEmail, BuyerPhone = head.BuyerPhone,
            LastSubmittedAtUtc = access.Request.LastSubmittedAtUtc
        };
    }

    private static bool ValidToken(string? token)
        => token?.Trim() is { Length: 64 } value && value.All(Uri.IsHexDigit);
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
    private static Error NotFound() => Error.NotFound("Không tìm thấy yêu cầu thông tin hóa đơn.");
    private sealed record ResolvedAccess(InvoiceBuyerSelfServiceRequest Request, Order Order,
        List<InvoiceHead> Heads, DateTime ExpiresAtUtc, string? UnavailableMessage);
}
