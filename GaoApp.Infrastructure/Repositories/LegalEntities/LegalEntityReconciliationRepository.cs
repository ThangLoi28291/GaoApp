using GaoApp.Application.Common;
using GaoApp.Application.DTOs.LegalEntities;
using GaoApp.Application.Interfaces.Repositories.LegalEntities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.LegalEntities;

public sealed class LegalEntityReconciliationRepository : ILegalEntityReconciliationRepository
{
    private readonly AppDbContext _db;

    public LegalEntityReconciliationRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<LegalEntityOrderBreakdownDto?> GetOrderBreakdownAsync(
        int storeId,
        int orderId,
        CancellationToken ct = default)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Id == orderId && !x.IsDeleted)
            .Select(x => new
            {
                x.Id,
                x.OrderNumber,
                x.Status,
                x.CompletedAtUtc,
                x.GrandTotal
            })
            .SingleOrDefaultAsync(ct);

        if (order == null)
            return null;

        var allocations = await _db.OrderLegalEntityAllocations
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.OrderId == orderId && !x.IsDeleted)
            .OrderBy(x => x.SalePriority)
            .ThenBy(x => x.OrderLineId)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.OrderLineId,
                x.LegalEntityId,
                LegalEntityCode = x.LegalEntity.Code,
                LegalEntityName = x.LegalEntity.Name,
                x.SalePriority,
                x.WarehouseId,
                WarehouseCode = x.Warehouse.Code,
                WarehouseName = x.Warehouse.Name,
                ProductName = x.OrderLine.ItemName,
                x.OrderLine.Sku,
                x.OrderLine.Barcode,
                x.ProductVariant.HasInputInvoice,
                x.Quantity,
                x.BaseQuantity,
                x.NetAmount,
                x.AllocationSource
            })
            .ToListAsync(ct);

        if (allocations.Count == 0)
            return null;

        var reversalByAllocation = await _db.OrderLegalEntityAllocationReversals
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.OrderId == orderId && !x.IsDeleted)
            .GroupBy(x => x.OrderLegalEntityAllocationId)
            .Select(x => new
            {
                AllocationId = x.Key,
                BaseQuantity = x.Sum(y => y.BaseQuantity),
                Amount = x.Sum(y => y.FinancialAmount)
            })
            .ToDictionaryAsync(x => x.AllocationId, ct);

        var invoiceProjections = await _db.InvoiceHeads
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                x.OrderId == orderId &&
                x.OriginalInvoiceHeadId == null &&
                !x.IsDeleted)
            .OrderBy(x => x.LegalEntity != null ? x.LegalEntity.SalePriority : int.MaxValue)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.LegalEntityId,
                LegalEntityCode = x.LegalEntity != null ? x.LegalEntity.Code : "Legacy",
                LegalEntityName = x.LegalEntity != null ? x.LegalEntity.Name : "Hóa đơn legacy",
                x.TotalQuantity,
                x.GrandTotal,
                x.ProviderStatus,
                x.ProviderInvoiceNo,
                x.IssuedAtUtc
            })
            .ToListAsync(ct);

        var lines = allocations.Select(x =>
        {
            reversalByAllocation.TryGetValue(x.Id, out var reversal);
            return new LegalEntityAllocationLineDto
            {
                AllocationId = x.Id,
                OrderLineId = x.OrderLineId,
                LegalEntityId = x.LegalEntityId,
                LegalEntityCode = x.LegalEntityCode,
                LegalEntityName = x.LegalEntityName,
                SalePriority = x.SalePriority,
                WarehouseId = x.WarehouseId,
                WarehouseCode = x.WarehouseCode,
                WarehouseName = x.WarehouseName,
                ProductName = x.ProductName,
                Sku = x.Sku,
                Barcode = x.Barcode,
                HasInputInvoice = x.HasInputInvoice,
                Quantity = x.Quantity,
                BaseQuantity = x.BaseQuantity,
                NetAmount = x.NetAmount,
                ReversedBaseQuantity = reversal?.BaseQuantity ?? 0m,
                ReversedAmount = reversal?.Amount ?? 0m,
                AllocationSource = x.AllocationSource.ToString()
            };
        }).ToList();

        var invoices = invoiceProjections.Select(x => new LegalEntityChildInvoiceDto
        {
            InvoiceHeadId = x.Id,
            LegalEntityId = x.LegalEntityId,
            LegalEntityCode = x.LegalEntityCode,
            LegalEntityName = x.LegalEntityName,
            TotalQuantity = x.TotalQuantity,
            GrandTotal = x.GrandTotal,
            ProviderStatus = x.ProviderStatus.ToString(),
            ProviderInvoiceNo = x.ProviderInvoiceNo,
            IssuedAtUtc = x.IssuedAtUtc
        }).ToList();

        var entitySummaries = lines
            .GroupBy(x => new
            {
                x.LegalEntityId,
                x.LegalEntityCode,
                x.LegalEntityName,
                x.SalePriority
            })
            .OrderBy(x => x.Key.SalePriority)
            .ThenBy(x => x.Key.LegalEntityId)
            .Select(group =>
            {
                var entityInvoices = invoices
                    .Where(x => x.LegalEntityId == group.Key.LegalEntityId)
                    .ToList();
                var gross = group.Sum(x => x.NetAmount);
                var reversed = group.Sum(x => x.ReversedAmount);
                var expectedInvoice = group
                    .Where(x => x.HasInputInvoice)
                    .Sum(x => x.NetAmount - x.ReversedAmount);

                return new LegalEntityOrderEntityBreakdownDto
                {
                    LegalEntityId = group.Key.LegalEntityId,
                    LegalEntityCode = group.Key.LegalEntityCode,
                    LegalEntityName = group.Key.LegalEntityName,
                    SalePriority = group.Key.SalePriority,
                    AllocationGrossTotal = gross,
                    ReversalTotal = reversed,
                    AllocationNetTotal = gross - reversed,
                    ExpectedInvoiceTotal = expectedInvoice,
                    ActualInvoiceTotal = entityInvoices.Sum(x => x.GrandTotal),
                    InvoiceCount = entityInvoices.Count
                };
            })
            .ToList();

        var allocationGross = lines.Sum(x => x.NetAmount);
        var reversalTotal = lines.Sum(x => x.ReversedAmount);

        return new LegalEntityOrderBreakdownDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status.ToString(),
            CompletedAtUtc = order.CompletedAtUtc,
            UnifiedOrderTotal = order.GrandTotal,
            AllocationGrossTotal = allocationGross,
            ReversalTotal = reversalTotal,
            AllocationNetTotal = allocationGross - reversalTotal,
            ExpectedInvoiceTotal = lines
                .Where(x => x.HasInputInvoice)
                .Sum(x => x.NetAmount - x.ReversedAmount),
            ActualInvoiceTotal = invoices.Sum(x => x.GrandTotal),
            EntitySummaries = entitySummaries,
            Allocations = lines,
            Invoices = invoices
        };
    }

    public async Task<LegalEntityReconciliationReportDto> QueryAsync(
        int storeId,
        LegalEntityReconciliationQueryDto query,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        CancellationToken ct = default)
    {
        var storeState = await _db.Stores
            .AsNoTracking()
            .Where(x => x.Id == storeId && !x.IsDeleted)
            .Select(x => new
            {
                x.IsMultiLegalEntityEnabled,
                x.MultiLegalEntityActivatedAtUtc
            })
            .SingleAsync(ct);

        var options = await _db.LegalEntities
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .OrderBy(x => x.SalePriority)
            .ThenBy(x => x.Id)
            .Select(x => new LegalEntityReconciliationOptionDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                SalePriority = x.SalePriority
            })
            .ToListAsync(ct);

        var ordersQuery = _db.Orders
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.CompletedAtUtc.HasValue &&
                x.CompletedAtUtc.Value >= fromUtc &&
                x.CompletedAtUtc.Value < toUtcExclusive &&
                x.LegalEntityAllocations.Any(a => !a.IsDeleted));

        if (query.LegalEntityId.HasValue)
        {
            var legalEntityId = query.LegalEntityId.Value;
            ordersQuery = ordersQuery.Where(x =>
                x.LegalEntityAllocations.Any(a =>
                    !a.IsDeleted && a.LegalEntityId == legalEntityId));
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword;
            ordersQuery = ordersQuery.Where(x =>
                (x.OrderNumber != null && x.OrderNumber.Contains(keyword)) ||
                (x.Customer != null &&
                    ((x.Customer.Name != null && x.Customer.Name.Contains(keyword)) ||
                     (x.Customer.Phone != null && x.Customer.Phone.Contains(keyword)))));
        }

        var orderRows = await ordersQuery
            .OrderByDescending(x => x.CompletedAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.OrderNumber,
                x.Status,
                x.CompletedAtUtc,
                x.GrandTotal
            })
            .ToListAsync(ct);

        if (orderRows.Count == 0)
        {
            return new LegalEntityReconciliationReportDto
            {
                Query = query,
                IsMultiLegalEntityEnabled = storeState.IsMultiLegalEntityEnabled,
                MultiLegalEntityActivatedAtUtc = storeState.MultiLegalEntityActivatedAtUtc,
                LegalEntities = options,
                Orders = new PagedResult<LegalEntityOrderReconciliationRowDto>(
                    query.Page,
                    query.PageSize,
                    0,
                    [])
            };
        }

        var orderIdsQuery = ordersQuery.Select(x => x.Id);

        var allocationByOrder = await _db.OrderLegalEntityAllocations
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                orderIdsQuery.Contains(x.OrderId))
            .GroupBy(x => x.OrderId)
            .Select(x => new
            {
                OrderId = x.Key,
                Gross = x.Sum(y => y.NetAmount),
                Eligible = x.Sum(y => y.ProductVariant.HasInputInvoice ? y.NetAmount : 0m)
            })
            .ToListAsync(ct);

        var reversalByOrder = await _db.OrderLegalEntityAllocationReversals
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                orderIdsQuery.Contains(x.OrderId))
            .GroupBy(x => x.OrderId)
            .Select(x => new
            {
                OrderId = x.Key,
                Amount = x.Sum(y => y.FinancialAmount),
                EligibleAmount = x.Sum(y =>
                    y.OrderLegalEntityAllocation.ProductVariant.HasInputInvoice
                        ? y.FinancialAmount
                        : 0m)
            })
            .ToListAsync(ct);

        var invoiceByOrder = await _db.InvoiceHeads
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.OriginalInvoiceHeadId == null &&
                x.OrderId.HasValue && orderIdsQuery.Contains(x.OrderId.Value))
            .GroupBy(x => x.OrderId!.Value)
            .Select(x => new
            {
                OrderId = x.Key,
                Amount = x.Sum(y => y.GrandTotal),
                Count = x.Count(),
                IssuedCount = x.Count(y =>
                    y.IssuedAtUtc.HasValue || y.ProviderInvoiceNo != null)
            })
            .ToListAsync(ct);

        var allocationByEntity = await _db.OrderLegalEntityAllocations
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                orderIdsQuery.Contains(x.OrderId))
            .GroupBy(x => new
            {
                x.OrderId,
                x.LegalEntityId,
                Code = x.LegalEntity.Code,
                Name = x.LegalEntity.Name,
                x.LegalEntity.SalePriority
            })
            .Select(x => new
            {
                x.Key.OrderId,
                x.Key.LegalEntityId,
                x.Key.Code,
                x.Key.Name,
                x.Key.SalePriority,
                Gross = x.Sum(y => y.NetAmount),
                Eligible = x.Sum(y => y.ProductVariant.HasInputInvoice ? y.NetAmount : 0m)
            })
            .ToListAsync(ct);

        var reversalByEntity = await _db.OrderLegalEntityAllocationReversals
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                orderIdsQuery.Contains(x.OrderId))
            .GroupBy(x => new { x.OrderId, x.LegalEntityId })
            .Select(x => new
            {
                x.Key.OrderId,
                x.Key.LegalEntityId,
                Amount = x.Sum(y => y.FinancialAmount),
                EligibleAmount = x.Sum(y =>
                    y.OrderLegalEntityAllocation.ProductVariant.HasInputInvoice
                        ? y.FinancialAmount
                        : 0m)
            })
            .ToListAsync(ct);

        var invoiceByEntity = await _db.InvoiceHeads
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.OriginalInvoiceHeadId == null &&
                x.LegalEntityId.HasValue &&
                x.OrderId.HasValue && orderIdsQuery.Contains(x.OrderId.Value))
            .GroupBy(x => new { OrderId = x.OrderId!.Value, LegalEntityId = x.LegalEntityId!.Value })
            .Select(x => new
            {
                x.Key.OrderId,
                x.Key.LegalEntityId,
                Amount = x.Sum(y => y.GrandTotal),
                Count = x.Count(),
                IssuedCount = x.Count(y =>
                    y.IssuedAtUtc.HasValue || y.ProviderInvoiceNo != null)
            })
            .ToListAsync(ct);

        var allocationOrderMap = allocationByOrder.ToDictionary(x => x.OrderId);
        var reversalOrderMap = reversalByOrder.ToDictionary(x => x.OrderId);
        var invoiceOrderMap = invoiceByOrder.ToDictionary(x => x.OrderId);
        var entityCodesByOrder = allocationByEntity
            .GroupBy(x => x.OrderId)
            .ToDictionary(
                x => x.Key,
                x => string.Join(" + ", x
                    .OrderBy(y => y.SalePriority)
                    .ThenBy(y => y.LegalEntityId)
                    .Select(y => y.Code)));

        var rows = orderRows.Select(x =>
        {
            allocationOrderMap.TryGetValue(x.Id, out var allocation);
            reversalOrderMap.TryGetValue(x.Id, out var reversal);
            invoiceOrderMap.TryGetValue(x.Id, out var invoice);
            entityCodesByOrder.TryGetValue(x.Id, out var entityCodes);

            var gross = allocation?.Gross ?? 0m;
            var reversed = reversal?.Amount ?? 0m;
            return new LegalEntityOrderReconciliationRowDto
            {
                OrderId = x.Id,
                OrderNumber = x.OrderNumber,
                Status = x.Status.ToString(),
                CompletedAtUtc = x.CompletedAtUtc,
                LegalEntityCodes = entityCodes ?? string.Empty,
                UnifiedOrderTotal = x.GrandTotal,
                AllocationGrossTotal = gross,
                ReversalTotal = reversed,
                AllocationNetTotal = gross - reversed,
                ExpectedInvoiceTotal = (allocation?.Eligible ?? 0m) -
                    (reversal?.EligibleAmount ?? 0m),
                ActualInvoiceTotal = invoice?.Amount ?? 0m,
                InvoiceCount = invoice?.Count ?? 0,
                IssuedInvoiceCount = invoice?.IssuedCount ?? 0
            };
        }).ToList();

        var discrepancyCount = rows.Count(x => !x.IsBalanced);
        if (query.OnlyDiscrepancies)
            rows = rows.Where(x => !x.IsBalanced).ToList();

        var includedOrderIds = rows.Select(x => x.OrderId).ToHashSet();
        var reversalEntityMap = reversalByEntity.ToDictionary(
            x => (x.OrderId, x.LegalEntityId));
        var invoiceEntityMap = invoiceByEntity.ToDictionary(
            x => (x.OrderId, x.LegalEntityId));

        var selectedEntityRows = allocationByEntity
            .Where(x => includedOrderIds.Contains(x.OrderId))
            .Where(x => !query.LegalEntityId.HasValue || x.LegalEntityId == query.LegalEntityId.Value)
            .Select(x =>
            {
                reversalEntityMap.TryGetValue((x.OrderId, x.LegalEntityId), out var reversal);
                invoiceEntityMap.TryGetValue((x.OrderId, x.LegalEntityId), out var invoice);
                return new
                {
                    x.OrderId,
                    x.LegalEntityId,
                    x.Code,
                    x.Name,
                    x.SalePriority,
                    x.Gross,
                    x.Eligible,
                    Reversal = reversal?.Amount ?? 0m,
                    EligibleReversal = reversal?.EligibleAmount ?? 0m,
                    InvoiceAmount = invoice?.Amount ?? 0m,
                    InvoiceCount = invoice?.Count ?? 0,
                    IssuedCount = invoice?.IssuedCount ?? 0
                };
            })
            .ToList();

        var summaries = selectedEntityRows
            .GroupBy(x => new
            {
                x.LegalEntityId,
                x.Code,
                x.Name,
                x.SalePriority
            })
            .OrderBy(x => x.Key.SalePriority)
            .ThenBy(x => x.Key.LegalEntityId)
            .Select(x => new LegalEntityReconciliationSummaryDto
            {
                LegalEntityId = x.Key.LegalEntityId,
                LegalEntityCode = x.Key.Code,
                LegalEntityName = x.Key.Name,
                SalePriority = x.Key.SalePriority,
                OrderCount = x.Select(y => y.OrderId).Distinct().Count(),
                AllocationGrossTotal = x.Sum(y => y.Gross),
                ReversalTotal = x.Sum(y => y.Reversal),
                AllocationNetTotal = x.Sum(y => y.Gross - y.Reversal),
                ExpectedInvoiceTotal = x.Sum(y => y.Eligible - y.EligibleReversal),
                ActualInvoiceTotal = x.Sum(y => y.InvoiceAmount),
                InvoiceCount = x.Sum(y => y.InvoiceCount),
                IssuedInvoiceCount = x.Sum(y => y.IssuedCount)
            })
            .ToList();

        var totalItems = rows.Count;
        var pageItems = rows
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new LegalEntityReconciliationReportDto
        {
            Query = query,
            IsMultiLegalEntityEnabled = storeState.IsMultiLegalEntityEnabled,
            MultiLegalEntityActivatedAtUtc = storeState.MultiLegalEntityActivatedAtUtc,
            LegalEntities = options,
            OrderCount = totalItems,
            DiscrepantOrderCount = query.OnlyDiscrepancies ? totalItems : discrepancyCount,
            UnifiedOrderTotal = rows.Sum(x => x.UnifiedOrderTotal),
            AllocationGrossTotal = selectedEntityRows.Sum(x => x.Gross),
            ReversalTotal = selectedEntityRows.Sum(x => x.Reversal),
            AllocationNetTotal = selectedEntityRows.Sum(x => x.Gross - x.Reversal),
            ExpectedInvoiceTotal = selectedEntityRows.Sum(x => x.Eligible - x.EligibleReversal),
            ActualInvoiceTotal = selectedEntityRows.Sum(x => x.InvoiceAmount),
            EntitySummaries = summaries,
            Orders = new PagedResult<LegalEntityOrderReconciliationRowDto>(
                query.Page,
                query.PageSize,
                totalItems,
                pageItems)
        };
    }
}
