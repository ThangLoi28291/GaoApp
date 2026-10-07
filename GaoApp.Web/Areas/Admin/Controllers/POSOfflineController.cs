using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Exceptions.Pos;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Common.POS;
using GaoApp.Web.Services.Offline;
using GaoApp.Web.Services.Acb;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Route("admin/pos/offline")]
[Authorize(Policy = PermissionCodes.Pos.Order.View)]
[AutoValidateAntiforgeryToken, ServiceFilter(typeof(PosOperationFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class POSOfflineController(AppDbContext db, IPOSRuntimeContextAccessor runtime,
    IPOSService pos, IAuthorizationService authorization, IAntiforgery antiforgery,
    GaoApp.Web.Services.Printing.ReceiptTemplateService receiptTemplates) : Controller
{
    private async Task<POSShift> RequireShift(CancellationToken ct)
    {
        if (runtime.StoreId is not > 0 || runtime.TerminalId is not > 0 || runtime.UserId is not > 0)
            throw PosAppException.Business(PosErrorCodes.ContextSessionInvalid,
                "Chưa xác định cửa hàng, nhân viên và quầy POS.",
                "Đăng nhập lại và chọn đúng quầy POS.", statusCode: StatusCodes.Status409Conflict);
        return await db.POSShifts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == runtime.StoreId &&
            x.TerminalId == runtime.TerminalId && x.OpenedByUserId == runtime.UserId && x.Status == POSShiftStatus.Open, ct)
            ?? throw PosAppException.Business(PosErrorCodes.ShiftNotOpen,
                "Chưa có ca POS đang mở cho nhân viên tại quầy này.",
                "Mở ca trực tuyến trước khi bán hàng. Nếu quầy còn giao dịch chờ đồng bộ, cần đối soát ca gốc.",
                statusCode: StatusCodes.Status409Conflict);
    }

    [HttpGet("bootstrap")]
    public async Task<IActionResult> Bootstrap(CancellationToken ct)
    {
        var shift = await RequireShift(ct);
        // Also verifies that the additive retry migration is installed.
        _ = await db.Set<PosOperationReceipt>().AnyAsync(ct);
        var permissions = new List<string>();
        foreach (var permission in new[] { PermissionCodes.Pos.Order.Create, PermissionCodes.Pos.Order.Finalize,
            PermissionCodes.Pos.Order.Hold, PermissionCodes.Pos.Order.Discount, PermissionCodes.Pos.Order.Reprint,
            PermissionCodes.Pos.Payment.Create })
            if ((await authorization.AuthorizeAsync(User, permission)).Succeeded) permissions.Add(permission);
        var accounts = await db.Set<StoreBankAccount>().AsNoTracking().Where(x => x.StoreId == runtime.StoreId && x.IsActive)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.BankCode, x.BankName, x.AccountName, x.AccountNumber, x.VietQrBankBin, x.IsDefault }).ToListAsync(ct);
        return Ok(new
        {
            version = 1, storeId = runtime.StoreId, terminalId = runtime.TerminalId, userId = runtime.UserId,
            storeName = runtime.StoreName, terminalName = runtime.TerminalName, userName = runtime.UserName,
            shiftId = shift.Id, shiftCode = shift.ShiftCode, warehouseId = shift.WarehouseId,
            preparedAtUtc = DateTime.UtcNow, expiresAtUtc = DateTime.UtcNow.AddHours(24),
            permissions, accounts, screen = await pos.GetPOSScreenAsync(ct),
            receiptTemplates = await receiptTemplates.ListAsync(ct),
            receiptDefault = await receiptTemplates.GetDefaultAsync(ct),
            receiptStoreInfo = await receiptTemplates.GetStoreInfoAsync(ct),
            antiForgeryToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken
        });
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var shift = await RequireShift(ct);
        return Ok(new { storeId = runtime.StoreId, terminalId = runtime.TerminalId, userId = runtime.UserId,
            shiftId = shift.Id, receiptStoreInfo = await receiptTemplates.GetStoreInfoAsync(ct),
            receiptDefault = await receiptTemplates.GetDefaultAsync(ct),
            antiForgeryToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog([FromQuery] int afterId = 0, CancellationToken ct = default)
    {
        var shift = await RequireShift(ct);
        var items = await db.ProductVariants.AsNoTracking()
            .Where(x => x.StoreId == runtime.StoreId && x.Id > afterId && x.IsActive && !x.Product.IsDeleted && x.Product.IsActive && x.Product.IsSellable)
            .OrderBy(x => x.Id).Take(250).Select(x => new
            {
                id = x.Id, x.ProductId, productName = x.Product.Name, x.ProductVariantName, x.Sku,
                x.Product.BaseUnitId, baseUnitName = x.Product.BaseUnit.Name,
                price = x.Price > 0 ? x.Price.Value : x.Product.BasePrice, x.WholesalePrice, x.IsActive,
                onHandQty = x.InventoryBalances.Where(b => b.StoreId == runtime.StoreId && b.WarehouseId == shift.WarehouseId && !b.IsDeleted)
                    .Sum(b => (decimal?)b.OnHandQty) ?? 0,
                units = x.UnitConversions.Where(u => u.StoreId == runtime.StoreId && !u.IsDeleted && u.IsActive && !u.Unit.IsDeleted && u.Factor > 0)
                    .OrderByDescending(u => u.IsDefaultForSale).ThenByDescending(u => u.IsBaseUnit).ThenBy(u => u.SortOrder).ThenBy(u => u.Factor).ThenBy(u => u.Id).Select(u => new
                    {
                        id = u.Id, u.UnitId, unitName = u.Unit.Name, u.Factor, u.IsBaseUnit, u.IsDefaultForSale, u.Price, u.WholesalePrice,
                        barcodes = u.Barcodes.Where(b => b.StoreId == runtime.StoreId && b.IsActive && !b.IsDeleted)
                            .OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Id).Select(b => b.Barcode).ToList()
                    }).ToList()
            }).ToListAsync(ct);
        return Ok(new { items, nextAfterId = items.Count == 250 ? (int?)items[^1].id : null });
    }

    [HttpGet("customers")]
    public async Task<IActionResult> Customers([FromQuery] int afterId = 0, CancellationToken ct = default)
    {
        await RequireShift(ct);
        var items = await db.Customers.AsNoTracking().Where(x => x.StoreId == runtime.StoreId && x.Id > afterId && x.IsActive)
            .OrderBy(x => x.Id).Take(250).Select(x => new { customerId = x.Id, x.Name, x.Phone, x.Address, x.PriceTier, x.AskBeforePrintingReceipt }).ToListAsync(ct);
        return Ok(new { items, nextAfterId = items.Count == 250 ? (int?)items[^1].customerId : null });
    }

    [HttpGet("promotions")]
    public async Task<IActionResult> Promotions(CancellationToken ct)
    {
        await RequireShift(ct);
        var now = DateTime.UtcNow;
        return Ok(await db.Promotions.AsNoTracking().Where(x => x.StoreId == runtime.StoreId && x.IsActive && x.StartAtUtc <= now && x.EndAtUtc >= now)
            .Select(x => new { x.Id, x.Name, x.Type, x.Priority, x.DiscountType, x.DiscountValue, x.CustomerPriceTier,
                x.ComboFixedPrice, x.ComboNote, x.ComboPricingMode, x.ComboQuantity, x.ComboBaseUnitId,
                x.BuyQuantity, x.GetQuantity, x.StartAtUtc, x.EndAtUtc,
                items = x.Items.Where(i => !i.IsDeleted).Select(i => new { i.ProductId, i.VariantId, i.ProductUnitConversionId, i.MinQuantity }).ToList(),
                comboRules = x.ComboRules.Where(i => !i.IsDeleted).Select(i => new { i.ProductId, i.VariantId, i.ProductUnitConversionId, i.RequiredQuantity }).ToList()
            }).ToListAsync(ct));
    }

    public sealed class ManualTransferRequest
    {
        public int OrderId { get; set; }
        public Guid ClientRequestId { get; set; }
        public int BankAccountId { get; set; }
        public decimal Amount { get; set; }
        public string ReferenceCode { get; set; } = "";
        public int? ExistingQrId { get; set; }
    }

    [HttpPost("manual-transfer")]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Create)]
    [Authorize(Policy = PermissionCodes.Pos.Order.Finalize)]
    public async Task<IActionResult> ManualTransfer([FromBody] ManualTransferRequest request, CancellationToken ct)
    {
        var shift = await RequireShift(ct);
        if (!Request.Headers.ContainsKey("X-POS-Operation-Id"))
            throw new ConflictAppException("Khoản thu offline phải có mã thao tác bền vững.");
        // Share the bank callback lock; a late bank confirmation and a cashier receipt must link one payment.
        await using var gate = await AcbOrderLock.AcquireAsync(db, request.OrderId, ct);
        var order = await db.Orders.Include(x => x.Lines).Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.StoreId == runtime.StoreId && x.Id == request.OrderId && x.POSShiftId == shift.Id, ct)
            ?? throw new ConflictAppException("Đơn chuyển khoản không thuộc ca của quầy này.");
        var account = await db.Set<StoreBankAccount>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == runtime.StoreId && x.Id == request.BankAccountId && x.IsActive, ct)
            ?? throw new ConflictAppException("Tài khoản nhận tiền đã thay đổi; cần đối soát khoản thu offline.");
        if (request.ReferenceCode.Length is < 1 or > 100 || request.ClientRequestId == Guid.Empty)
            throw new ConflictAppException("Thiếu mã tham chiếu khoản chuyển khoản offline.");
        PosPaymentQrRequest? qr = null;
        AcbQrSession? session = null;
        OrderPayment? linkedPayment = null;
        var reference = request.ReferenceCode;
        if (request.ExistingQrId.HasValue)
        {
            qr = await db.PosPaymentQrRequests.SingleOrDefaultAsync(x => x.StoreId == runtime.StoreId &&
                x.Id == request.ExistingQrId && x.OrderId == order.Id && x.BankAccountId == account.Id, ct)
                ?? throw new ConflictAppException("Không tìm thấy QR gốc của khoản thu.");
            session = await db.Set<AcbQrSession>().SingleOrDefaultAsync(x => x.StoreId == runtime.StoreId && x.QrRequestId == qr.Id, ct);
            if (qr.Amount != request.Amount || qr.RequestCode != request.ReferenceCode ||
                qr.Status is PosPaymentQrStatus.Cancelled or PosPaymentQrStatus.Failed ||
                (session != null && (session.ShiftId != shift.Id || session.TerminalId != runtime.TerminalId ||
                    session.Status is AcbSessionStatus.Cancelled or AcbSessionStatus.ReviewRequired ||
                    !AcbPaymentPolicy.MatchesFingerprint(order, session.CartFingerprint))))
                throw new ConflictAppException("QR hoặc giỏ đã thay đổi. Giữ khoản thu thủ công để đối soát.");
            reference = session?.ProviderOrderId ?? qr.RequestCode;
            var paymentId = session?.PaymentId ?? qr.PaymentId;
            if (paymentId.HasValue)
                linkedPayment = order.Payments.SingleOrDefault(x => !x.IsDeleted && x.Id == paymentId &&
                    x.Amount == request.Amount && x.ReferenceCode == reference)
                    ?? throw new ConflictAppException("Khoản thu của QR gốc đã thay đổi; cần đối soát.");
        }
        var draft = linkedPayment != null ? await RecordedDraft(order, ct) : await pos.AddPaymentAsync(request.OrderId, new UpsertPaymentRequest
        {
            ClientRequestId = request.ClientRequestId, Amount = request.Amount, Method = PaymentMethod.BankTransfer,
            ReferenceCode = reference, Provider = "OFFLINE-MANUAL:" + account.BankCode
        }, ct);
        if (linkedPayment == null)
        {
            linkedPayment = await db.OrderPayments.SingleAsync(x => x.StoreId == runtime.StoreId && x.ClientRequestId == request.ClientRequestId, ct);
            linkedPayment.MetadataJson = JsonSerializer.Serialize(new { source = "offline-manual", confirmedBy = runtime.UserId, originalQrId = qr?.Id });
            if (qr != null)
            {
                qr.PaymentId = linkedPayment.Id; qr.Status = PosPaymentQrStatus.ManualConfirmed;
                qr.PaidAtUtc = qr.ManualConfirmedAtUtc = DateTime.UtcNow; qr.ManualConfirmedByUserId = runtime.UserId;
            }
            if (session != null)
            {
                session.PaymentId = linkedPayment.Id; session.Status = AcbSessionStatus.Received;
                session.ConfirmationSource = AcbConfirmationSource.OfflineManual;
                session.ConfirmedAtUtc = DateTime.UtcNow; session.ConfirmedByUserId = runtime.UserId;
            }
            await db.SaveChangesAsync(ct);
        }
        var finalized = draft.BalanceDue == 0;
        if (finalized && draft.Status == OrderStatus.Draft) draft = await pos.FinalizeAsync(draft.OrderId, ct);
        if (finalized)
        {
            if (session != null) { session.Status = AcbSessionStatus.Completed; session.PrintClaimedAtUtc ??= DateTime.UtcNow; }
            if (qr != null) qr.PrintClaimedAtUtc ??= DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return Ok(new { orderId = draft.OrderId, finalized, draft, paidAmount = request.Amount,
            paidTotal = draft.PaidTotal, remainingAmount = draft.BalanceDue, confirmationSource = "offline-manual",
            printUrl = finalized ? $"/admin/pos/orders/{draft.OrderId}/print?autoPrint=true" : null });
    }

    private async Task<OrderDraftDto> RecordedDraft(Order order, CancellationToken ct)
    {
        if (order.Status == OrderStatus.Draft) return await pos.GetDraftAsync(order.Id, ct);
        if (order.Status != OrderStatus.Completed) throw new ConflictAppException("Đơn có khoản thu đã đổi trạng thái; cần đối soát.");
        var receipt = await pos.GetReceiptAsync(order.Id, ct);
        return new OrderDraftDto { OrderId = order.Id, OrderNumber = receipt.OrderNumber, Status = OrderStatus.Completed,
            CustomerId = receipt.CustomerId, CustomerName = receipt.CustomerName, CustomerPhone = receipt.CustomerPhone,
            Note = receipt.Note, Subtotal = receipt.Subtotal, DiscountTotal = receipt.DiscountTotal, OrderDiscount = order.OrderDiscount,
            GrandTotal = receipt.GrandTotal, PaidTotal = receipt.PaidTotal, BalanceDue = receipt.BalanceDue, ChangeDue = receipt.ChangeDue,
            VoucherDiscountTotal = receipt.VoucherDiscountTotal, PromotionDiscountTotal = order.PromotionDiscountTotal,
            ComboDiscountTotal = order.ComboDiscountTotal, Lines = receipt.Lines, Payments = receipt.Payments };
    }
}
