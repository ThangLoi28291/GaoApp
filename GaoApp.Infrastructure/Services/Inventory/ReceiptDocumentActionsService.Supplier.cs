using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Inventory;

public sealed partial class ReceiptDocumentActionsService
{
    private const string SupplierSettlementCorrectionMessage =
        "Phiếu đã có xác nhận hoặc lịch sử thanh toán tiền hàng. Không được sửa trực tiếp nhà cung cấp; vui lòng xử lý bằng chứng từ điều chỉnh để giữ nguyên lịch sử chi tiền.";

    private async Task<bool> HasSupplierSettlementAsync(StockDocument document, CancellationToken ct)
    {
        if (document.IsMerchandisePaid) return true;
        var payables = db.PurchasePayables.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.StoreId == StoreId && x.StockDocumentId == document.Id && x.Type == PurchasePayableType.Merchandise);
        return await payables.AnyAsync(x => x.Status == PurchasePayableStatus.Paid || x.PaidAtUtc != null, ct) ||
            await db.TreasuryEntries.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.StoreId == StoreId && x.PurchasePayableId != null &&
                payables.Any(p => p.Id == x.PurchasePayableId), ct);
    }

    public async Task CorrectSupplierAsync(int id, CorrectReceiptSupplierRequest request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 1000)
            throw new BusinessRuleException("Nhập lý do sửa nhà cung cấp từ 1 đến 1.000 ký tự.");
        if (user.UserId is not > 0) throw new BusinessRuleException("Vui lòng đăng nhập lại.");

        // Share the receipt lock with XML linking/unlinking and supplier resolution.
        // The receipt, payable party and audit must change together, without reposting stock.
        await inputInvoices.BeginSupplierResolutionTransactionAsync(ct);
        try
        {
            // Treasury writes take this store lock before accessing a payable. Take it before
            // the receipt/payable locks too, so a payment cannot land between checking and saving.
            if (db.Database.IsSqlServer() && db.Database.CurrentTransaction != null)
            {
                var resource = $"treasury:{StoreId}";
                await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; IF @r<0 THROW 51000,'Treasury is busy.',1;", ct);
            }
            var document = await inputInvoices.LockReceiptForInputInvoiceMutationAsync(StoreId, id, ct);
            if (document == null || document.IsDeleted || document.Type != StockDocumentType.Receipt)
                throw new BusinessRuleException("Không tìm thấy phiếu nhập.");
            Version(document, request.RowVersion);
            if (document.Status != StockDocumentStatus.Confirmed)
                throw new BusinessRuleException("Chức năng này chỉ dành cho phiếu đã duyệt. Phiếu chưa duyệt sửa tại mục Nhà cung cấp.");
            var supplier = await db.Suppliers.SingleOrDefaultAsync(x => x.StoreId == StoreId &&
                x.Id == request.SupplierId && x.IsActive && !x.IsDeleted, ct)
                ?? throw new BusinessRuleException("Nhà cung cấp không tồn tại, đã ngừng hoạt động hoặc không thuộc cửa hàng này.");
            if ((document.PurchaseOrderId.HasValue || document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder) &&
                document.PurchaseOrder?.SupplierId != supplier.Id)
                throw new BusinessRuleException("Phiếu theo đơn mua phải cùng nhà cung cấp với đơn mua. Vui lòng xử lý nhà cung cấp của đơn mua trước.");
            var invoices = await inputInvoices.GetLinkedInvoicesForSupplierResolutionAsync(StoreId, id, ct);
            if (invoices.Any(x => x.ResolvedSupplierId != supplier.Id ||
                x.SupplierResolutionStatus != InputInvoiceSupplierResolutionStatus.Resolved))
                throw new BusinessRuleException("Nhà cung cấp mới không khớp hóa đơn XML đang gắn. Gỡ liên kết XML sai trước, sửa nhà cung cấp rồi chọn lại hóa đơn đúng.");
            if (document.SupplierId == supplier.Id)
                throw new BusinessRuleException("Nhà cung cấp mới đang trùng với nhà cung cấp hiện tại.");

            // Include archived rows in the guard: reversing or hiding a payment must never
            // permit rewriting the supplier on its original financial evidence.
            var allPayables = db.Database.IsSqlServer() && db.Database.CurrentTransaction != null
                ? await db.PurchasePayables.FromSqlInterpolated($"""
                    SELECT * FROM [dbo].[PurchasePayables] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [StoreId] = {StoreId} AND [StockDocumentId] = {id} AND [Type] = {(int)PurchasePayableType.Merchandise}
                    """).IgnoreQueryFilters().ToListAsync(ct)
                : await db.PurchasePayables.IgnoreQueryFilters().Where(x => x.StoreId == StoreId && x.StockDocumentId == id &&
                    x.Type == PurchasePayableType.Merchandise).ToListAsync(ct);
            var payableIds = allPayables.Select(x => x.Id).ToArray();
            if (document.IsMerchandisePaid ||
                allPayables.Any(x => x.Status == PurchasePayableStatus.Paid || x.PaidAtUtc != null) ||
                await db.TreasuryEntries.IgnoreQueryFilters().AnyAsync(x => x.StoreId == StoreId &&
                    x.PurchasePayableId != null && payableIds.Contains(x.PurchasePayableId.Value), ct))
                throw new BusinessRuleException(SupplierSettlementCorrectionMessage);
            var payables = allPayables.Where(x => !x.IsDeleted).ToList();
            object Snapshot() => new
            {
                document.SupplierId, SupplierName = document.Supplier?.Name, SupplierTaxCode = document.Supplier?.TaxCode,
                document.MerchandisePayeeName,
                PurchasePayables = payables.Select(x => new { x.Id, x.SupplierId, x.PayeeName, x.Amount,
                    x.Status, x.RecognizedAtUtc, x.PaidAtUtc, x.SourceKey }).ToArray()
            };
            var before = JsonSerializer.Serialize(Snapshot());
            document.SupplierId = supplier.Id;
            document.Supplier = supplier;
            document.MerchandisePayeeName = supplier.Name;
            foreach (var payable in payables)
            {
                payable.SupplierId = supplier.Id;
                payable.PayeeName = supplier.Name;
            }
            db.PurchaseReceiptAuditEvents.Add(new PurchaseReceiptAuditEvent
            {
                StoreId = StoreId, StockDocumentId = id, EventType = PurchaseReceiptAuditEventType.ReceiptSupplierCorrected,
                ActorUserId = user.UserId.Value, ActorUserName = user.UserName, OccurredAtUtc = DateTime.UtcNow,
                IsSuccess = true, Reason = reason,
                ChangedFieldsJson = "[\"SupplierId\",\"MerchandisePayeeName\",\"PurchasePayables\"]",
                OldValuesJson = before, NewValuesJson = JsonSerializer.Serialize(Snapshot())
            });
            await SaveAsync(document, ct);
            await inputInvoices.CommitSupplierResolutionTransactionAsync(ct);
        }
        catch (Microsoft.Data.SqlClient.SqlException error) when (error.Number == 51000)
        {
            await inputInvoices.RollbackSupplierResolutionTransactionAsync(CancellationToken.None);
            await inputInvoices.ClearFailedTransactionStateAsync(CancellationToken.None);
            throw new BusinessRuleException("Đang xử lý thanh toán trong cửa hàng. Vui lòng thử lại sau khi giao dịch hoàn tất.");
        }
        catch
        {
            await inputInvoices.RollbackSupplierResolutionTransactionAsync(CancellationToken.None);
            await inputInvoices.ClearFailedTransactionStateAsync(CancellationToken.None);
            throw;
        }
    }
}
