using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Repositories.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Reports;

public sealed class SalesReportReadRepository : ISalesReportReadRepository
{
    private readonly AppDbContext _db;

    public SalesReportReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<SalesExecutivePeriodDataDto> GetExecutivePeriodAsync(
        int storeId,
        SalesResolvedExecutiveQueryDto query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Period);

        if (storeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(storeId));

        var salesQuery = BuildSaleQuery(storeId, query);
        var saleLineQuery = BuildSaleLineQuery(storeId, salesQuery);
        var returnQuery = BuildReturnQuery(storeId, query);
        var voidQuery = BuildVoidQuery(storeId, query);

        var salesAggregate = await salesQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                SalesOrders = group.Count(),
                GrossSales = group.Sum(x => x.Subtotal),
                Discounts = group.Sum(x => x.DiscountTotal),
                SalesAfterDiscount = group.Sum(x => x.GrandTotal),
                CustomerLinkedOrders = group.Count(x => x.CustomerId != null),
                Promotion = group.Sum(x => x.PromotionDiscountTotal),
                Combo = group.Sum(x => x.ComboDiscountTotal),
                ManualOrder = group.Sum(x => x.OrderDiscount),
                Voucher = group.Sum(x => x.VoucherDiscountTotal)
            })
            .SingleOrDefaultAsync(ct);

        var returnAggregate = await returnQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Returns = group.Sum(x => x.ReturnSubtotal),
                RefundAmount = group.Sum(x => x.RefundTotal),
                ReturnCount = group.Count(),
                RefundCount = group.Count(x => x.RefundTotal > 0m)
            })
            .SingleOrDefaultAsync(ct);

        var voidAggregate = await voidQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                VoidCount = group.Count(),
                VoidValue = group.Sum(x => x.GrandTotal)
            })
            .SingleOrDefaultAsync(ct);

        var manualLineDiscount = await saleLineQuery
            .Select(line => (decimal?)line.LineDiscount)
            .SumAsync(ct) ?? 0m;

        var trend = await GetTrendAsync(
            salesQuery,
            returnQuery,
            query.Period,
            ct);

        var salesByHour = await GetSalesByHourAsync(
            salesQuery,
            returnQuery,
            query.Period,
            ct);

        var topProducts = await GetTopProductsAsync(
            saleLineQuery,
            ct);

        return new SalesExecutivePeriodDataDto
        {
            Summary = new SalesMetricSummaryDto
            {
                GrossSales = salesAggregate?.GrossSales ?? 0m,
                Discounts = salesAggregate?.Discounts ?? 0m,
                SalesAfterDiscount = salesAggregate?.SalesAfterDiscount ?? 0m,
                Returns = returnAggregate?.Returns ?? 0m,
                RefundAmount = returnAggregate?.RefundAmount ?? 0m,
                SalesOrders = salesAggregate?.SalesOrders ?? 0,
                ReturnCount = returnAggregate?.ReturnCount ?? 0,
                RefundCount = returnAggregate?.RefundCount ?? 0,
                VoidCount = voidAggregate?.VoidCount ?? 0,
                VoidValue = voidAggregate?.VoidValue ?? 0m,
                CustomerLinkedOrders = salesAggregate?.CustomerLinkedOrders ?? 0
            },
            Trend = trend,
            SalesByHour = salesByHour,
            TopProducts = topProducts,
            DiscountBreakdown = new SalesDiscountBreakdownDto
            {
                ManualLine = manualLineDiscount,
                Promotion = salesAggregate?.Promotion ?? 0m,
                Combo = salesAggregate?.Combo ?? 0m,
                ManualOrder = salesAggregate?.ManualOrder ?? 0m,
                Voucher = salesAggregate?.Voucher ?? 0m,
                TotalDiscounts = salesAggregate?.Discounts ?? 0m
            }
        };
    }

    public async Task<SalesPagedResultDto<SalesOrderDetailRowDto>> GetOrdersAsync(
        int storeId,
        SalesResolvedDetailQueryDto query,
        CancellationToken ct = default)
    {
        ValidateDetailQuery(storeId, query);
        var orders = BuildDetailSaleQuery(storeId, query);
        orders = ApplyOrderSearch(orders, query.Search);
        var totalItems = await orders.CountAsync(ct);

        var ordered = query.Sort switch
        {
            SalesDetailSorts.Oldest => orders.OrderBy(x => x.CompletedAtUtc).ThenBy(x => x.Id),
            SalesDetailSorts.ValueDesc => orders.OrderByDescending(x => x.GrandTotal).ThenByDescending(x => x.CompletedAtUtc),
            SalesDetailSorts.ValueAsc => orders.OrderBy(x => x.GrandTotal).ThenByDescending(x => x.CompletedAtUtc),
            _ => orders.OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.Id)
        };

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(order => new SalesOrderDetailRowDto
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber ?? string.Empty,
                CompletedAtUtc = order.CompletedAtUtc!.Value,
                CustomerName = order.Customer != null ? order.Customer.Name : "Khách lẻ",
                Subtotal = order.Subtotal,
                Discounts = order.DiscountTotal,
                SalesAfterDiscount = order.GrandTotal,
                StatusCode = (int)order.Status,
                ReturnCount = _db.SalesReturns.Count(r =>
                    r.StoreId == storeId && !r.IsDeleted && r.OrderId == order.Id && r.Status == SalesReturnStatus.Completed),
                ReturnValue = _db.SalesReturns
                    .Where(r => r.StoreId == storeId && !r.IsDeleted && r.OrderId == order.Id && r.Status == SalesReturnStatus.Completed)
                    .Select(r => (decimal?)r.ReturnSubtotal)
                    .Sum() ?? 0m
            })
            .ToListAsync(ct);

        return Page(items, query, totalItems);
    }

    public async Task<SalesPagedResultDto<SalesProductDetailRowDto>> GetProductsAsync(
        int storeId,
        SalesResolvedDetailQueryDto query,
        CancellationToken ct = default)
    {
        ValidateDetailQuery(storeId, query);
        var sales = BuildDetailSaleQuery(storeId, query);
        var lines = BuildSaleLineQuery(storeId, sales);

        if (query.VariantId is > 0)
            lines = lines.Where(x => x.VariantId == query.VariantId.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search;
            lines = lines.Where(x =>
                x.ItemName.Contains(search) ||
                (x.Sku != null && x.Sku.Contains(search)) ||
                (x.Barcode != null && x.Barcode.Contains(search)));
        }

        var grouped = lines
            .GroupBy(line => line.VariantId)
            .Select(group => new SalesProductDetailRowDto
            {
                VariantId = group.Key,
                ItemName = group.Max(x => x.ItemName) ?? string.Empty,
                Sku = group.Max(x => x.Sku),
                BaseUnitName = group.Max(x => x.BaseUnitName) ?? "Đơn vị gốc",
                BaseQuantity = group.Sum(x => x.BaseQuantity),
                GrossSales = group.Sum(x => x.Quantity * x.UnitPrice),
                SalesOrders = group.Select(x => x.OrderId).Distinct().Count()
            });

        var totalItems = await grouped.CountAsync(ct);
        var ordered = query.Sort switch
        {
            SalesDetailSorts.QuantityDesc => grouped.OrderByDescending(x => x.BaseQuantity).ThenBy(x => x.ItemName),
            SalesDetailSorts.NameAsc => grouped.OrderBy(x => x.ItemName).ThenByDescending(x => x.GrossSales),
            SalesDetailSorts.ValueAsc => grouped.OrderBy(x => x.GrossSales).ThenBy(x => x.ItemName),
            _ => grouped.OrderByDescending(x => x.GrossSales).ThenBy(x => x.ItemName)
        };

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return Page(items, query, totalItems);
    }

    public async Task<SalesPagedResultDto<SalesDiscountOrderRowDto>> GetDiscountsAsync(
        int storeId,
        SalesResolvedDetailQueryDto query,
        CancellationToken ct = default)
    {
        ValidateDetailQuery(storeId, query);
        var orders = ApplyOrderSearch(BuildDetailSaleQuery(storeId, query), query.Search)
            .Where(x => x.DiscountTotal > 0m);
        var totalItems = await orders.CountAsync(ct);

        var ordered = query.Sort switch
        {
            SalesDetailSorts.Newest => orders.OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.Id),
            SalesDetailSorts.Oldest => orders.OrderBy(x => x.CompletedAtUtc).ThenBy(x => x.Id),
            SalesDetailSorts.ValueAsc => orders.OrderBy(x => x.DiscountTotal).ThenByDescending(x => x.CompletedAtUtc),
            _ => orders.OrderByDescending(x => x.DiscountTotal).ThenByDescending(x => x.CompletedAtUtc)
        };

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(order => new SalesDiscountOrderRowDto
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber ?? string.Empty,
                CompletedAtUtc = order.CompletedAtUtc!.Value,
                CustomerName = order.Customer != null ? order.Customer.Name : "Khách lẻ",
                TotalDiscounts = order.DiscountTotal,
                ManualLine = _db.OrderLines
                    .Where(line => line.StoreId == storeId && !line.IsDeleted && line.OrderId == order.Id)
                    .Select(line => (decimal?)line.LineDiscount)
                    .Sum() ?? 0m,
                Promotion = order.PromotionDiscountTotal,
                Combo = order.ComboDiscountTotal,
                ManualOrder = order.OrderDiscount,
                Voucher = order.VoucherDiscountTotal
            })
            .ToListAsync(ct);

        return Page(items, query, totalItems);
    }

    public async Task<SalesPagedResultDto<SalesReturnDetailRowDto>> GetReturnsAsync(
        int storeId,
        SalesResolvedDetailQueryDto query,
        CancellationToken ct = default)
    {
        ValidateDetailQuery(storeId, query);
        var returns = BuildDetailReturnQuery(storeId, query);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search;
            returns = returns.Where(x =>
                x.ReturnNumber.Contains(search) ||
                (x.Order.OrderNumber != null && x.Order.OrderNumber.Contains(search)) ||
                (x.Order.Customer != null && x.Order.Customer.Name.Contains(search)));
        }
        var totalItems = await returns.CountAsync(ct);
        var ordered = query.Sort switch
        {
            SalesDetailSorts.Oldest => returns.OrderBy(x => x.CompletedAtUtc).ThenBy(x => x.Id),
            SalesDetailSorts.ValueDesc => returns.OrderByDescending(x => x.ReturnSubtotal).ThenByDescending(x => x.CompletedAtUtc),
            SalesDetailSorts.ValueAsc => returns.OrderBy(x => x.ReturnSubtotal).ThenByDescending(x => x.CompletedAtUtc),
            _ => returns.OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.Id)
        };

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(row => new SalesReturnDetailRowDto
            {
                ReturnId = row.Id,
                ReturnNumber = row.ReturnNumber,
                CompletedAtUtc = row.CompletedAtUtc!.Value,
                OrderId = row.OrderId,
                OrderNumber = row.Order.OrderNumber ?? string.Empty,
                CustomerName = row.Order.Customer != null ? row.Order.Customer.Name : "Khách lẻ",
                TypeCode = (int)row.Type,
                ReturnSubtotal = row.ReturnSubtotal,
                RefundAmount = row.RefundTotal
            })
            .ToListAsync(ct);

        return Page(items, query, totalItems);
    }

    public async Task<SalesPagedResultDto<SalesVoidDetailRowDto>> GetVoidsAsync(
        int storeId,
        SalesResolvedDetailQueryDto query,
        CancellationToken ct = default)
    {
        ValidateDetailQuery(storeId, query);
        var orders = ApplyOrderSearch(BuildDetailVoidQuery(storeId, query), query.Search);
        var totalItems = await orders.CountAsync(ct);
        var ordered = query.Sort switch
        {
            SalesDetailSorts.Oldest => orders.OrderBy(x => x.CompletedAtUtc).ThenBy(x => x.Id),
            SalesDetailSorts.ValueDesc => orders.OrderByDescending(x => x.GrandTotal).ThenByDescending(x => x.CompletedAtUtc),
            SalesDetailSorts.ValueAsc => orders.OrderBy(x => x.GrandTotal).ThenByDescending(x => x.CompletedAtUtc),
            _ => orders.OrderByDescending(x => x.CompletedAtUtc).ThenByDescending(x => x.Id)
        };

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(order => new SalesVoidDetailRowDto
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber ?? string.Empty,
                CompletedAtUtc = order.CompletedAtUtc!.Value,
                CustomerName = order.Customer != null ? order.Customer.Name : "Khách lẻ",
                Subtotal = order.Subtotal,
                Discounts = order.DiscountTotal,
                VoidValue = order.GrandTotal
            })
            .ToListAsync(ct);

        return Page(items, query, totalItems);
    }

    public Task<List<SalesTerminalOptionDto>> GetTerminalOptionsAsync(
        int storeId,
        CancellationToken ct = default)
    {
        if (storeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(storeId));

        return _db.POSTerminals
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId
                && !x.IsDeleted
                && x.IsActive
                && x.Status == POSTerminalStatus.Active)
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .Select(x => new SalesTerminalOptionDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name
            })
            .ToListAsync(ct);
    }

    private static void ValidateDetailQuery(int storeId, SalesResolvedDetailQueryDto query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Period);
        if (storeId <= 0) throw new ArgumentOutOfRangeException(nameof(storeId));
        if (query.Page <= 0) throw new ArgumentOutOfRangeException(nameof(query.Page));
        if (query.PageSize <= 0 || query.PageSize > 100) throw new ArgumentOutOfRangeException(nameof(query.PageSize));
    }

    private IQueryable<Order> BuildDetailSaleQuery(int storeId, SalesResolvedDetailQueryDto query)
    {
        var result = _db.Orders.AsNoTracking().Where(order =>
            order.StoreId == storeId && !order.IsDeleted && order.CompletedAtUtc.HasValue &&
            order.CompletedAtUtc.Value >= query.EffectiveFromUtc &&
            order.CompletedAtUtc.Value < query.EffectiveToUtcExclusive &&
            (order.Status == OrderStatus.Completed || order.Status == OrderStatus.Refunded));

        if (query.TerminalId is > 0)
        {
            var terminalId = query.TerminalId.Value;
            result = result.Where(order => order.POSShift.StoreId == storeId && order.POSShift.TerminalId == terminalId);
        }
        return ApplyOrderCustomerState(result, query.CustomerState);
    }

    private IQueryable<SalesReturn> BuildDetailReturnQuery(int storeId, SalesResolvedDetailQueryDto query)
    {
        var result = _db.SalesReturns.AsNoTracking().Where(row =>
            row.StoreId == storeId && !row.IsDeleted && row.Status == SalesReturnStatus.Completed &&
            row.CompletedAtUtc.HasValue && row.CompletedAtUtc.Value >= query.EffectiveFromUtc &&
            row.CompletedAtUtc.Value < query.EffectiveToUtcExclusive);
        if (query.TerminalId is > 0)
        {
            var terminalId = query.TerminalId.Value;
            result = result.Where(row => row.POSShift.StoreId == storeId && row.POSShift.TerminalId == terminalId);
        }
        return query.CustomerState switch
        {
            SalesCustomerStates.Linked => result.Where(x => x.Order.StoreId == storeId && x.Order.CustomerId != null),
            SalesCustomerStates.Guest => result.Where(x => x.Order.StoreId == storeId && x.Order.CustomerId == null),
            _ => result
        };
    }

    private IQueryable<Order> BuildDetailVoidQuery(int storeId, SalesResolvedDetailQueryDto query)
    {
        var result = _db.Orders.AsNoTracking().Where(order =>
            order.StoreId == storeId && !order.IsDeleted && order.Status == OrderStatus.Voided &&
            order.CompletedAtUtc.HasValue && order.CompletedAtUtc.Value >= query.EffectiveFromUtc &&
            order.CompletedAtUtc.Value < query.EffectiveToUtcExclusive);
        if (query.TerminalId is > 0)
        {
            var terminalId = query.TerminalId.Value;
            result = result.Where(order => order.POSShift.StoreId == storeId && order.POSShift.TerminalId == terminalId);
        }
        return ApplyOrderCustomerState(result, query.CustomerState);
    }

    private static IQueryable<Order> ApplyOrderSearch(IQueryable<Order> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return query;
        return query.Where(order =>
            (order.OrderNumber != null && order.OrderNumber.Contains(search)) ||
            (order.Customer != null && order.Customer.Name.Contains(search)) ||
            (order.Customer != null && order.Customer.Phone != null && order.Customer.Phone.Contains(search)));
    }

    private static SalesPagedResultDto<T> Page<T>(List<T> items, SalesResolvedDetailQueryDto query, int totalItems)
        => new()
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems,
            TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)query.PageSize))
        };

    private IQueryable<Order> BuildSaleQuery(
        int storeId,
        SalesResolvedExecutiveQueryDto query)
    {
        var period = query.Period;

        var result = _db.Orders
            .AsNoTracking()
            .Where(order =>
                order.StoreId == storeId
                && !order.IsDeleted
                && order.CompletedAtUtc.HasValue
                && order.CompletedAtUtc.Value >= period.FromUtc
                && order.CompletedAtUtc.Value < period.ToUtcExclusive
                && (order.Status == OrderStatus.Completed
                    || order.Status == OrderStatus.Refunded));

        if (query.TerminalId is > 0)
        {
            var terminalId = query.TerminalId.Value;
            result = result.Where(order =>
                order.POSShift.StoreId == storeId
                && order.POSShift.TerminalId == terminalId);
        }

        return ApplyOrderCustomerState(result, query.CustomerState);
    }

    private IQueryable<OrderLine> BuildSaleLineQuery(
        int storeId,
        IQueryable<Order> salesQuery)
    {
        var saleOrderIds = salesQuery.Select(order => order.Id);

        return _db.OrderLines
            .AsNoTracking()
            .Where(line =>
                line.StoreId == storeId
                && !line.IsDeleted
                && saleOrderIds.Contains(line.OrderId));
    }

    private IQueryable<SalesReturn> BuildReturnQuery(
        int storeId,
        SalesResolvedExecutiveQueryDto query)
    {
        var period = query.Period;

        var result = _db.SalesReturns
            .AsNoTracking()
            .Where(salesReturn =>
                salesReturn.StoreId == storeId
                && !salesReturn.IsDeleted
                && salesReturn.Status == SalesReturnStatus.Completed
                && salesReturn.CompletedAtUtc.HasValue
                && salesReturn.CompletedAtUtc.Value >= period.FromUtc
                && salesReturn.CompletedAtUtc.Value < period.ToUtcExclusive);

        if (query.TerminalId is > 0)
        {
            var terminalId = query.TerminalId.Value;
            result = result.Where(salesReturn =>
                salesReturn.POSShift.StoreId == storeId
                && salesReturn.POSShift.TerminalId == terminalId);
        }

        return query.CustomerState switch
        {
            SalesCustomerStates.Linked => result.Where(x =>
                x.Order.StoreId == storeId && x.Order.CustomerId != null),
            SalesCustomerStates.Guest => result.Where(x =>
                x.Order.StoreId == storeId && x.Order.CustomerId == null),
            _ => result
        };
    }

    private IQueryable<Order> BuildVoidQuery(
        int storeId,
        SalesResolvedExecutiveQueryDto query)
    {
        var period = query.Period;

        var result = _db.Orders
            .AsNoTracking()
            .Where(order =>
                order.StoreId == storeId
                && !order.IsDeleted
                && order.Status == OrderStatus.Voided
                && order.CompletedAtUtc.HasValue
                && order.CompletedAtUtc.Value >= period.FromUtc
                && order.CompletedAtUtc.Value < period.ToUtcExclusive);

        if (query.TerminalId is > 0)
        {
            var terminalId = query.TerminalId.Value;
            result = result.Where(order =>
                order.POSShift.StoreId == storeId
                && order.POSShift.TerminalId == terminalId);
        }

        return ApplyOrderCustomerState(result, query.CustomerState);
    }

    private static IQueryable<Order> ApplyOrderCustomerState(
        IQueryable<Order> query,
        string customerState)
        => customerState switch
        {
            SalesCustomerStates.Linked => query.Where(x => x.CustomerId != null),
            SalesCustomerStates.Guest => query.Where(x => x.CustomerId == null),
            _ => query
        };

    private async Task<List<SalesTrendPointDto>> GetTrendAsync(
        IQueryable<Order> salesQuery,
        IQueryable<SalesReturn> returnQuery,
        SalesReportPeriodDto period,
        CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            return await GetRelationalTrendAsync(
                salesQuery,
                returnQuery,
                period,
                ct);
        }

        return await GetInMemoryTrendAsync(
            salesQuery,
            returnQuery,
            period,
            ct);
    }

    private async Task<List<SalesByHourPointDto>> GetSalesByHourAsync(
        IQueryable<Order> salesQuery,
        IQueryable<SalesReturn> returnQuery,
        SalesReportPeriodDto period,
        CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            return await GetRelationalSalesByHourAsync(
                salesQuery,
                returnQuery,
                period,
                ct);
        }

        return await GetInMemorySalesByHourAsync(
            salesQuery,
            returnQuery,
            period,
            ct);
    }

    private static Task<List<SalesTopProductDto>> GetTopProductsAsync(
        IQueryable<OrderLine> saleLineQuery,
        CancellationToken ct)
        => saleLineQuery
            .GroupBy(line => line.VariantId)
            .Select(group => new SalesTopProductDto
            {
                VariantId = group.Key,
                ItemName = group.Max(x => x.ItemName) ?? string.Empty,
                Sku = group.Max(x => x.Sku),
                BaseUnitName = group.Max(x => x.BaseUnitName) ?? string.Empty,
                BaseQuantity = group.Sum(x => x.BaseQuantity),
                GrossSales = group.Sum(x => x.Quantity * x.UnitPrice)
            })
            .OrderByDescending(x => x.GrossSales)
            .ThenBy(x => x.ItemName)
            .Take(10)
            .ToListAsync(ct);

    private static async Task<List<SalesTrendPointDto>> GetRelationalTrendAsync(
        IQueryable<Order> salesQuery,
        IQueryable<SalesReturn> returnQuery,
        SalesReportPeriodDto period,
        CancellationToken ct)
    {
        var salesBuckets = await salesQuery
            .GroupBy(order =>
                EF.Functions.DateDiffHour(
                    period.FromUtc,
                    order.CompletedAtUtc!.Value) / period.BucketHours)
            .Select(group => new
            {
                BucketIndex = group.Key,
                GrossSales = group.Sum(x => x.Subtotal),
                Discounts = group.Sum(x => x.DiscountTotal),
                SalesAfterDiscount = group.Sum(x => x.GrandTotal),
                SalesOrders = group.Count()
            })
            .OrderBy(x => x.BucketIndex)
            .ToListAsync(ct);

        var returnBuckets = await returnQuery
            .GroupBy(salesReturn =>
                EF.Functions.DateDiffHour(
                    period.FromUtc,
                    salesReturn.CompletedAtUtc!.Value) / period.BucketHours)
            .Select(group => new
            {
                BucketIndex = group.Key,
                Returns = group.Sum(x => x.ReturnSubtotal),
                RefundAmount = group.Sum(x => x.RefundTotal),
                ReturnCount = group.Count(),
                RefundCount = group.Count(x => x.RefundTotal > 0m)
            })
            .OrderBy(x => x.BucketIndex)
            .ToListAsync(ct);

        var result = new Dictionary<int, SalesTrendPointDto>();

        foreach (var bucket in salesBuckets)
        {
            result[bucket.BucketIndex] = new SalesTrendPointDto
            {
                BucketIndex = bucket.BucketIndex,
                GrossSales = bucket.GrossSales,
                Discounts = bucket.Discounts,
                SalesAfterDiscount = bucket.SalesAfterDiscount,
                SalesOrders = bucket.SalesOrders
            };
        }

        foreach (var bucket in returnBuckets)
        {
            if (!result.TryGetValue(bucket.BucketIndex, out var point))
            {
                point = new SalesTrendPointDto
                {
                    BucketIndex = bucket.BucketIndex
                };
                result[bucket.BucketIndex] = point;
            }

            point.Returns = bucket.Returns;
            point.RefundAmount = bucket.RefundAmount;
            point.ReturnCount = bucket.ReturnCount;
            point.RefundCount = bucket.RefundCount;
        }

        return result.Values
            .Where(x => x.BucketIndex >= 0 && x.BucketIndex < period.BucketCount)
            .OrderBy(x => x.BucketIndex)
            .ToList();
    }

    private static async Task<List<SalesTrendPointDto>> GetInMemoryTrendAsync(
        IQueryable<Order> salesQuery,
        IQueryable<SalesReturn> returnQuery,
        SalesReportPeriodDto period,
        CancellationToken ct)
    {
        var salesRows = await salesQuery
            .Select(order => new
            {
                CompletedAtUtc = order.CompletedAtUtc!.Value,
                order.Subtotal,
                order.DiscountTotal,
                order.GrandTotal
            })
            .ToListAsync(ct);

        var returnRows = await returnQuery
            .Select(salesReturn => new
            {
                CompletedAtUtc = salesReturn.CompletedAtUtc!.Value,
                salesReturn.ReturnSubtotal,
                salesReturn.RefundTotal
            })
            .ToListAsync(ct);

        var result = salesRows
            .GroupBy(row => GetBucketIndex(
                period,
                row.CompletedAtUtc))
            .ToDictionary(
                group => group.Key,
                group => new SalesTrendPointDto
                {
                    BucketIndex = group.Key,
                    GrossSales = group.Sum(x => x.Subtotal),
                    Discounts = group.Sum(x => x.DiscountTotal),
                    SalesAfterDiscount = group.Sum(x => x.GrandTotal),
                    SalesOrders = group.Count()
                });

        foreach (var group in returnRows.GroupBy(row => GetBucketIndex(
                     period,
                     row.CompletedAtUtc)))
        {
            if (!result.TryGetValue(group.Key, out var point))
            {
                point = new SalesTrendPointDto
                {
                    BucketIndex = group.Key
                };
                result[group.Key] = point;
            }

            point.Returns = group.Sum(x => x.ReturnSubtotal);
            point.RefundAmount = group.Sum(x => x.RefundTotal);
            point.ReturnCount = group.Count();
            point.RefundCount = group.Count(x => x.RefundTotal > 0m);
        }

        return result.Values
            .Where(x => x.BucketIndex >= 0 && x.BucketIndex < period.BucketCount)
            .OrderBy(x => x.BucketIndex)
            .ToList();
    }

    private static async Task<List<SalesByHourPointDto>> GetRelationalSalesByHourAsync(
        IQueryable<Order> salesQuery,
        IQueryable<SalesReturn> returnQuery,
        SalesReportPeriodDto period,
        CancellationToken ct)
    {
        var salesBuckets = await salesQuery
            .GroupBy(order =>
                EF.Functions.DateDiffHour(
                    period.FromUtc,
                    order.CompletedAtUtc!.Value) % 24)
            .Select(group => new
            {
                Hour = group.Key,
                SalesAfterDiscount = group.Sum(x => x.GrandTotal),
                SalesOrders = group.Count()
            })
            .OrderBy(x => x.Hour)
            .ToListAsync(ct);

        var returnBuckets = await returnQuery
            .GroupBy(salesReturn =>
                EF.Functions.DateDiffHour(
                    period.FromUtc,
                    salesReturn.CompletedAtUtc!.Value) % 24)
            .Select(group => new
            {
                Hour = group.Key,
                Returns = group.Sum(x => x.ReturnSubtotal)
            })
            .OrderBy(x => x.Hour)
            .ToListAsync(ct);

        var result = new Dictionary<int, SalesByHourPointDto>();

        foreach (var bucket in salesBuckets)
        {
            result[bucket.Hour] = new SalesByHourPointDto
            {
                Hour = bucket.Hour,
                SalesAfterDiscount = bucket.SalesAfterDiscount,
                SalesOrders = bucket.SalesOrders
            };
        }

        foreach (var bucket in returnBuckets)
        {
            if (!result.TryGetValue(bucket.Hour, out var point))
            {
                point = new SalesByHourPointDto { Hour = bucket.Hour };
                result[bucket.Hour] = point;
            }

            point.Returns = bucket.Returns;
        }

        return result.Values
            .Where(x => x.Hour >= 0 && x.Hour <= 23)
            .OrderBy(x => x.Hour)
            .ToList();
    }

    private static async Task<List<SalesByHourPointDto>> GetInMemorySalesByHourAsync(
        IQueryable<Order> salesQuery,
        IQueryable<SalesReturn> returnQuery,
        SalesReportPeriodDto period,
        CancellationToken ct)
    {
        var salesRows = await salesQuery
            .Select(order => new
            {
                CompletedAtUtc = order.CompletedAtUtc!.Value,
                order.GrandTotal
            })
            .ToListAsync(ct);

        var returnRows = await returnQuery
            .Select(salesReturn => new
            {
                CompletedAtUtc = salesReturn.CompletedAtUtc!.Value,
                salesReturn.ReturnSubtotal
            })
            .ToListAsync(ct);

        var result = salesRows
            .GroupBy(row => GetHourOfBusinessDay(period, row.CompletedAtUtc))
            .ToDictionary(
                group => group.Key,
                group => new SalesByHourPointDto
                {
                    Hour = group.Key,
                    SalesAfterDiscount = group.Sum(x => x.GrandTotal),
                    SalesOrders = group.Count()
                });

        foreach (var group in returnRows.GroupBy(row =>
                     GetHourOfBusinessDay(period, row.CompletedAtUtc)))
        {
            if (!result.TryGetValue(group.Key, out var point))
            {
                point = new SalesByHourPointDto { Hour = group.Key };
                result[group.Key] = point;
            }

            point.Returns = group.Sum(x => x.ReturnSubtotal);
        }

        return result.Values
            .Where(x => x.Hour >= 0 && x.Hour <= 23)
            .OrderBy(x => x.Hour)
            .ToList();
    }

    private static int GetBucketIndex(
        SalesReportPeriodDto period,
        DateTime completedAtUtc)
    {
        var elapsedHours = (completedAtUtc - period.FromUtc).TotalHours;
        return (int)Math.Floor(elapsedHours / period.BucketHours);
    }

    private static int GetHourOfBusinessDay(
        SalesReportPeriodDto period,
        DateTime completedAtUtc)
    {
        var elapsedHours = (int)Math.Floor(
            (completedAtUtc - period.FromUtc).TotalHours);
        return elapsedHours % 24;
    }
}
