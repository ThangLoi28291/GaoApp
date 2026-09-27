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

public sealed class InvoiceBuyerSelfServiceService
    : IInvoiceBuyerSelfServiceService
{
    private static readonly TimeSpan SelfServiceWindow =
        TimeSpan.FromHours(2);

    private readonly IInvoiceBuyerSelfServiceRepository _requests;
    private readonly IOrderRepository _orders;
    private readonly IInvoiceRepository _invoices;
    private readonly IInvoiceBuyerService _buyers;
    private readonly IAppUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly ITenantContext _tenant;

    public InvoiceBuyerSelfServiceService(
     IInvoiceBuyerSelfServiceRepository requests,
     IOrderRepository orders,
     IInvoiceRepository invoices,
     IInvoiceBuyerService buyers,
     IAppUnitOfWork unitOfWork,
     ITenantContext tenant,
     TimeProvider clock)
    {
        _requests = requests;
        _orders = orders;
        _invoices = invoices;
        _buyers = buyers;
        _unitOfWork = unitOfWork;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<Result<InvoiceBuyerSelfServiceLinkDto>>
        CreateLinkAsync(
            int orderId,
            CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);

        if (order == null)
        {
            return Result<InvoiceBuyerSelfServiceLinkDto>.Failure(
                Error.NotFound("Không tìm thấy đơn hàng."));
        }

        if (order.Status != OrderStatus.Completed ||
            !order.CompletedAtUtc.HasValue)
        {
            return Result<InvoiceBuyerSelfServiceLinkDto>.Failure(
                Error.Conflict(
                    "Chỉ tạo QR hóa đơn cho đơn hàng đã hoàn tất."));
        }

        if (order.InvoiceIssuanceRoute !=
            InvoiceIssuanceRoute.Manual)
        {
            return Result<InvoiceBuyerSelfServiceLinkDto>.Failure(
                Error.Conflict(
                    "Đơn hàng không thuộc luồng phát hành thủ công."));
        }

        var token = CreateOpaqueToken();
        var hash = HashToken(token);

        var expiresAtUtc =
            order.CompletedAtUtc.Value + SelfServiceWindow;

        var entity = new InvoiceBuyerSelfServiceRequest
        {
            StoreId = order.StoreId,
            OrderId = order.Id,
            TokenHash = hash,
            ExpiresAtUtc = expiresAtUtc
        };

        await _requests.AddAsync(entity, ct);
        await _requests.SaveChangesAsync(ct);

        return Result<InvoiceBuyerSelfServiceLinkDto>.Success(
            new InvoiceBuyerSelfServiceLinkDto
            {
                OrderId = order.Id,
                Token = token,
                ExpiresAtUtc = expiresAtUtc,
                IsExpired =
                    _clock.GetUtcNow().UtcDateTime >= expiresAtUtc
            });
    }

    public async Task<Result<InvoiceBuyerSelfServiceViewDto>>
        GetAsync(
            string token,
            CancellationToken ct = default)
    {
        var resolved =
            await ResolveAsync(token, ct);

        if (!resolved.IsSuccess)
        {
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(
                resolved.Error);
        }

        return Result<InvoiceBuyerSelfServiceViewDto>.Success(
            BuildView(
                resolved.Value.Request,
                resolved.Value.Order,
                resolved.Value.Heads));
    }

    public async Task<Result<InvoiceBuyerSelfServiceViewDto>>
        SubmitAsync(
            string token,
            SubmitInvoiceBuyerSelfServiceRequest request,
            CancellationToken ct = default)
    {
        var resolved =
            await ResolveAsync(token, ct);

        if (!resolved.IsSuccess)
        {
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(
                resolved.Error);
        }

        var access = resolved.Value;

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        if (nowUtc >= access.Request.ExpiresAtUtc)
        {
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(
                Error.Conflict(
                    "Thời gian tự cập nhật thông tin hóa đơn đã kết thúc. Vui lòng liên hệ cửa hàng."));
        }

        if (access.Order.InvoiceIssuanceRoute !=
            InvoiceIssuanceRoute.Manual)
        {
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(
                Error.Conflict(
                    "Hóa đơn hiện không còn thuộc luồng khách tự nhập thông tin."));
        }

        if (access.Heads.Count == 0)
        {
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(
                Error.Conflict(
                    "Hóa đơn đang được chuẩn bị. Vui lòng thử lại sau."));
        }

        if (access.Heads.Any(
                x => !InvoiceIssuanceStatePolicy
                    .CanEditBuyerInformation(x)))
        {
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(
                Error.Conflict(
                    "Hóa đơn đang được xử lý hoặc đã phát hành. Vui lòng liên hệ cửa hàng."));
        }

        await using var tx =
            await _unitOfWork.BeginTransactionAsync(ct);

        // InvoiceBuyerService hiện đã có logic áp buyer info cho
        // toàn bộ original sibling heads của cùng Order.
        var firstHead = access.Heads
            .OrderBy(x => x.LegalEntityId)
            .ThenBy(x => x.Id)
            .First();

        var update = await _buyers.UpdateBuyerInfoAsync(
            new UpdateInvoiceBuyerInfoRequest
            {
                InvoiceHeadId = firstHead.Id,
                BuyerType = request.BuyerType,
                BuyerName = request.BuyerName,
                BuyerLegalName = request.BuyerLegalName,
                BuyerTaxCode = request.BuyerTaxCode,
                BuyerCitizenId = request.BuyerCitizenId,
                ClearBuyerCitizenId = string.IsNullOrWhiteSpace(request.BuyerCitizenId),
                BuyerAddress = request.BuyerAddress,
                BuyerEmail = request.BuyerEmail,
                BuyerPhone = request.BuyerPhone,

                // Public QR không tự ghi hồ sơ buyer lâu dài.
                SaveToProfile = false,
                Source = "qr-self-service"
            },
            ct);

        if (!update.IsSuccess)
        {
            await tx.RollbackAsync(ct);

            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(
                update.Error);
        }

        access.Request.LastSubmittedAtUtc = nowUtc;

        await _requests.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Re-read để response phản ánh toàn bộ dữ liệu vừa lưu.
        var refreshed =
            await ResolveAsync(token, ct);

        if (!refreshed.IsSuccess)
        {
            return Result<InvoiceBuyerSelfServiceViewDto>.Failure(
                refreshed.Error);
        }

        return Result<InvoiceBuyerSelfServiceViewDto>.Success(
            BuildView(
                refreshed.Value.Request,
                refreshed.Value.Order,
                refreshed.Value.Heads));
    }

    private async Task<Result<ResolvedAccess>> ResolveAsync(
        string token,
        CancellationToken ct)
    {
        token = (token ?? string.Empty).Trim();

        if (token.Length != 64 ||
            !token.All(Uri.IsHexDigit))
        {
            return Result<ResolvedAccess>.Failure(
                Error.NotFound(
                    "Không tìm thấy yêu cầu thông tin hóa đơn."));
        }

        var hash = HashToken(token);

        var entity =
            await _requests.GetByTokenHashAsync(hash, ct);

        if (entity == null ||
            entity.RevokedAtUtc.HasValue ||
            entity.Order == null)
        {
            return Result<ResolvedAccess>.Failure(
                Error.NotFound(
                    "Không tìm thấy yêu cầu thông tin hóa đơn."));
        }
        if (_tenant.StoreId is not > 0 ||
    _tenant.StoreId.Value != entity.StoreId)
        {
            return Result<ResolvedAccess>.Failure(
                Error.NotFound(
                    "Không tìm thấy yêu cầu thông tin hóa đơn."));
        }

        if (entity.StoreId != entity.Order.StoreId)
        {
            return Result<ResolvedAccess>.Failure(
                Error.NotFound(
                    "Không tìm thấy yêu cầu thông tin hóa đơn."));
        }

        var heads =
            await _invoices
                .GetOriginalInvoiceHeadsWithDetailsByOrderIdAsync(
                    entity.OrderId,
                    ct);

        heads = heads
            .Where(x =>
                x.StoreId == entity.StoreId &&
                !x.IsAutoInvoiceGroup)
            .ToList();

        return Result<ResolvedAccess>.Success(
            new ResolvedAccess(
                entity,
                entity.Order,
                heads));
    }

    private InvoiceBuyerSelfServiceViewDto BuildView(
        InvoiceBuyerSelfServiceRequest request,
        Order order,
        IReadOnlyCollection<InvoiceHead> heads)
    {
        var nowUtc =
            _clock.GetUtcNow().UtcDateTime;

        var expired =
            nowUtc >= request.ExpiresAtUtc;

        var head = heads
            .OrderBy(x => x.LegalEntityId)
            .ThenBy(x => x.Id)
            .FirstOrDefault();

        var safe =
            heads.Count > 0 &&
            heads.All(
                InvoiceIssuanceStatePolicy
                    .CanEditBuyerInformation);

        return new InvoiceBuyerSelfServiceViewDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CompletedAtUtc = order.CompletedAtUtc,
            GrandTotal = order.GrandTotal,

            ExpiresAtUtc = request.ExpiresAtUtc,
            IsExpired = expired,

            CanEdit =
                !expired &&
                order.InvoiceIssuanceRoute ==
                    InvoiceIssuanceRoute.Manual &&
                safe,

            BuyerType =
    head == null ||
    string.IsNullOrWhiteSpace(head.BuyerType) ||
    head.BuyerType == InvoiceBuyerTypes.NoInvoice
        ? InvoiceBuyerTypes.Individual
        : head.BuyerType,

            BuyerName = head?.BuyerName,
            BuyerLegalName = head?.BuyerLegalName,
            BuyerTaxCode = head?.BuyerTaxCode,
            BuyerCitizenId = head?.BuyerCitizenId,
            BuyerAddress = head?.BuyerAddress,
            BuyerEmail = head?.BuyerEmail,
            BuyerPhone = head?.BuyerPhone,

            LastSubmittedAtUtc =
                request.LastSubmittedAtUtc
        };
    }

    private static string CreateOpaqueToken()
    {
        // 256-bit entropy, URL-safe hex representation.
        return Convert.ToHexString(
                RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
    }

    private static byte[] HashToken(string token)
        => SHA256.HashData(
            Encoding.UTF8.GetBytes(token));

    private sealed record ResolvedAccess(
        InvoiceBuyerSelfServiceRequest Request,
        Order Order,
        List<InvoiceHead> Heads);
}