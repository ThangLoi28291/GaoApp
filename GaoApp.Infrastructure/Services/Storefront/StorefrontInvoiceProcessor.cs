using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Constants;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

// Durable post-commit draft creation. Provider transport remains the existing authorized invoice queue.
public sealed class StorefrontInvoiceProcessor(AppDbContext db, IInvoiceService invoices, TimeProvider clock) : IStorefrontInvoiceProcessor
{
    public async Task ProcessAsync(int storefrontOrderId, CancellationToken ct = default)
    {
        if (db.CurrentStoreId is not > 0 || db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Invoice work requires its tenant and an independent post-commit scope.");
        var store = db.CurrentStoreId.Value;
        var work = await db.Set<StorefrontInvoiceWork>().SingleOrDefaultAsync(x => x.StoreId == store && x.StorefrontOrderId == storefrontOrderId, ct);
        if (work is null || work.CompletedAtUtc.HasValue) return;
        if (!await db.Orders.AnyAsync(x => x.StoreId == store && x.Id == work.SaleOrderId && x.Status == OrderStatus.Completed, ct) ||
            !await db.Set<StorefrontSaleBinding>().AnyAsync(x => x.StoreId == store && x.StorefrontOrderId == storefrontOrderId && x.SaleOrderId == work.SaleOrderId, ct))
            throw new ConflictAppException("Nguồn ghi bán chưa hoàn tất; không sinh chứng từ từ dữ liệu dở dang.");
        try
        {
            var goods = await invoices.GenerateInvoicesFromOrderAsync(work.SaleOrderId, ct);
            if (!goods.IsSuccess) { work.Attempts = Math.Min(1000000, work.Attempts + 1); work.NextAttemptAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(1); work.LastErrorCode = "GOODS_INVOICE_NOT_READY"; await db.SaveChangesAsync(ct); return; }
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await StorefrontFulfillmentService.Lock(db, $"GaoApp:Storefront:Invoice:{store}:{storefrontOrderId}", ct);
            await db.Entry(work).ReloadAsync(ct); if (work.CompletedAtUtc.HasValue) { await tx.CommitAsync(ct); return; }
            if (work.ServiceRevenueId is int feeId && !await db.InvoiceHeads.AnyAsync(x => x.StoreId == store && x.StorefrontServiceRevenueId == feeId && x.OriginalInvoiceHeadId == null, ct))
            {
                var fee = await db.Set<StorefrontServiceRevenue>().AsNoTracking().SingleAsync(x => x.StoreId == store && x.Id == feeId && x.StorefrontOrderId == storefrontOrderId, ct);
                var owner = await db.LegalEntities.AsNoTracking().SingleAsync(x => x.StoreId == store && x.Id == fee.LegalEntityId, ct);
                var setting = await db.InvoiceProviderSettings.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == store && x.Id == owner.InvoiceProviderSettingId, ct);
                var error = InvoiceCommandService.ValidateProviderSetting(setting, owner); if (error is not null) throw new ConflictAppException(error.Message);
                var head = new InvoiceHead { StoreId = store, StorefrontServiceRevenueId = fee.Id, NativeServiceInvoiceRoute = fee.InvoiceRoute,
                    NativeServiceRecognizedAtUtc = fee.RecognizedAtUtc, NativeServiceActorUserId = fee.ActorUserId, NativeServiceCashOnly = fee.CashOnlyFunding,
                    LegalEntityId = fee.LegalEntityId, InvoiceProviderSettingId = setting!.Id, InvoiceDate = DateTime.SpecifyKind(fee.RecognizedAtUtc.AddHours(7), DateTimeKind.Unspecified),
                    BuyerType = InvoiceBuyerTypes.NoInvoice, ProviderCode = setting.ProviderCode, ProviderStatus = InvoiceProviderStatus.LocalDraft,
                    TransactionUuid = Guid.NewGuid().ToString("D"), SupplierTaxCode = setting.SupplierTaxCode, InvoiceType = setting.InvoiceType,
                    TemplateCode = setting.TemplateCode, InvoiceSeries = setting.InvoiceSeries,
                    Note = fee.TaxRate > 0 ? "Dịch vụ website; phí gồm thuế đã chốt. Phát hành riêng để bảo toàn làm tròn thuế của nguồn." : "Dịch vụ website có nguồn doanh thu và quyết toán riêng." };
                head.Details.Add(new() { StoreId = store, StorefrontServiceRevenueId = fee.Id, SourceType = InvoiceDetailSourceType.FromStorefrontServiceRevenue,
                    ItemName = fee.Name, UnitName = "lần", Quantity = 1, UnitPrice = fee.NetAmount, Amount = fee.NetAmount,
                    VatRate = fee.TaxRate, VatAmount = fee.TaxAmount, TotalAmount = fee.GrossAmount, Note = "Nguồn dịch vụ website #" + fee.Id });
                InvoiceNativeServiceAmounts.Recalculate(head);
                if (head.GrandTotal != fee.GrossAmount || head.SubTotal != fee.NetAmount || head.VatAmount != fee.TaxAmount)
                    throw new ConflictAppException("Hóa đơn dịch vụ không bảo toàn nguyên nguồn phí đã thu.");
                db.InvoiceHeads.Add(head); await db.SaveChangesAsync(ct);
            }
            work.Attempts++; work.CompletedAtUtc = clock.GetUtcNow().UtcDateTime; work.LastErrorCode = null;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or GaoApp.Application.Common.Exceptions.AppException)
        {
            db.ChangeTracker.Clear();
            var retry = await db.Set<StorefrontInvoiceWork>().SingleAsync(x => x.StoreId == store && x.StorefrontOrderId == storefrontOrderId, ct);
            if (retry.CompletedAtUtc is null) { retry.Attempts = Math.Min(1000000, retry.Attempts + 1); retry.NextAttemptAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(1); retry.LastErrorCode = "NATIVE_INVOICE_RETRY_REQUIRED"; await db.SaveChangesAsync(ct); }
        }
    }
}
