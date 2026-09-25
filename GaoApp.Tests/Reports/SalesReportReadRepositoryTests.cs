using System.Diagnostics;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Services.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Reports;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Reports;

public sealed class SalesReportReadRepositoryTests
{
    [Fact]
    public async Task Discounts_newest_should_use_sale_time_then_id_even_when_older_discount_is_larger()
    {
        var options = CreateOptions();
        int olderId, newerId, tiedNewerId;
        await using (var seed = CreateContext(options))
        {
            var older = NewOrder(1, OrderStatus.Completed, Utc(2026, 9, 7, 1), 1000m, 300m, 700m, null);
            var newer = NewOrder(1, OrderStatus.Completed, Utc(2026, 9, 7, 2), 1000m, 20m, 980m, null);
            var tiedNewer = NewOrder(1, OrderStatus.Completed, Utc(2026, 9, 7, 2), 1000m, 10m, 990m, null);
            seed.Orders.AddRange(older, newer, tiedNewer);
            await seed.SaveChangesAsync();
            olderId = older.Id;
            newerId = newer.Id;
            tiedNewerId = tiedNewer.Id;
        }

        await using var read = CreateContext(options);
        var repository = new SalesReportReadRepository(read);
        var query = new SalesReportingPeriodPolicy().ResolveDetail(new SalesDetailQueryDto
        {
            FromDate = new DateTime(2026, 9, 7),
            ToDate = new DateTime(2026, 9, 7),
            Sort = SalesDetailSorts.Newest
        }, Utc(2026, 9, 7, 5));
        // Separate pages also verify deterministic ordering for equal sale timestamps.
        query.PageSize = 1;
        var ids = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            query.Page = page;
            var result = await repository.GetDiscountsAsync(1, query);
            Assert.Equal(3, result.TotalItems);
            ids.Add(Assert.Single(result.Items).OrderId);
        }
        Assert.Equal(new[] { tiedNewerId, newerId, olderId }, ids);

        query.Page = 1;
        query.Sort = SalesDetailSorts.DiscountDesc;
        Assert.Equal(olderId, Assert.Single((await repository.GetDiscountsAsync(1, query)).Items).OrderId);
    }

    [Fact]
    public async Task GetExecutivePeriodAsync_should_use_explicit_store_sale_return_void_and_insight_cohorts()
    {
        var options = CreateOptions();
        int completedOrderId;

        await using (var seed = CreateContext(options))
        {
            var completed = NewOrder(
                storeId: 1,
                status: OrderStatus.Completed,
                completedAtUtc: Utc(2026, 9, 7, 1),
                subtotal: 1000m,
                discounts: 100m,
                grandTotal: 900m,
                customerId: 10);

            completed.PromotionDiscountTotal = 30m;
            completed.ComboDiscountTotal = 20m;
            completed.OrderDiscount = 20m;
            completed.VoucherDiscountTotal = 10m;

            var refunded = NewOrder(
                storeId: 1,
                status: OrderStatus.Refunded,
                completedAtUtc: Utc(2026, 9, 7, 3),
                subtotal: 500m,
                discounts: 50m,
                grandTotal: 450m,
                customerId: null);

            refunded.PromotionDiscountTotal = 10m;
            refunded.ComboDiscountTotal = 10m;
            refunded.OrderDiscount = 10m;
            refunded.VoucherDiscountTotal = 10m;

            var voided = NewOrder(
                storeId: 1,
                status: OrderStatus.Voided,
                completedAtUtc: Utc(2026, 9, 7, 4),
                subtotal: 250m,
                discounts: 0m,
                grandTotal: 250m,
                customerId: null);

            var otherStore = NewOrder(
                storeId: 2,
                status: OrderStatus.Completed,
                completedAtUtc: Utc(2026, 9, 7, 5),
                subtotal: 9999m,
                discounts: 999m,
                grandTotal: 9000m,
                customerId: 20);

            seed.Orders.AddRange(completed, refunded, voided, otherStore);
            await seed.SaveChangesAsync();
            completedOrderId = completed.Id;

            seed.OrderLines.AddRange(
                NewLine(completed, variantId: 101, "Nước A", "A-101", "chai", quantity: 2m, baseQuantity: 12m, unitPrice: 300m, lineDiscount: 20m),
                NewLine(completed, variantId: 102, "Bánh B", "B-102", "gói", quantity: 1m, baseQuantity: 1m, unitPrice: 400m, lineDiscount: 0m),
                NewLine(refunded, variantId: 101, "Nước A", "A-101", "chai", quantity: 1m, baseQuantity: 1m, unitPrice: 200m, lineDiscount: 10m),
                NewLine(refunded, variantId: 103, "Kẹo C", "C-103", "gói", quantity: 3m, baseQuantity: 3m, unitPrice: 100m, lineDiscount: 0m));

            seed.SalesReturns.AddRange(
                NewReturn(
                    storeId: 1,
                    orderId: completedOrderId,
                    status: SalesReturnStatus.Completed,
                    completedAtUtc: Utc(2026, 9, 7, 6),
                    returnSubtotal: 150m,
                    refundTotal: 100m,
                    returnNumber: "RET-001"),
                NewReturn(
                    storeId: 1,
                    orderId: completedOrderId,
                    status: SalesReturnStatus.Cancelled,
                    completedAtUtc: Utc(2026, 9, 7, 7),
                    returnSubtotal: 777m,
                    refundTotal: 777m,
                    returnNumber: "RET-CANCELLED"),
                NewReturn(
                    storeId: 2,
                    orderId: otherStore.Id,
                    status: SalesReturnStatus.Completed,
                    completedAtUtc: Utc(2026, 9, 7, 8),
                    returnSubtotal: 888m,
                    refundTotal: 888m,
                    returnNumber: "RET-STORE-2"));

            seed.POSTerminals.AddRange(
                new POSTerminal
                {
                    StoreId = 1,
                    Code = "POS01",
                    Name = "Quầy 1",
                    IsActive = true,
                    Status = POSTerminalStatus.Active,
                    RowVersion = new byte[8]
                },
                new POSTerminal
                {
                    StoreId = 2,
                    Code = "POS02",
                    Name = "Quầy store 2",
                    IsActive = true,
                    Status = POSTerminalStatus.Active,
                    RowVersion = new byte[8]
                });

            await seed.SaveChangesAsync();
        }

        await using var read = CreateContext(options);
        var repository = new SalesReportReadRepository(read);
        var resolved = new SalesReportingPeriodPolicy().Resolve(
            new SalesExecutiveDashboardQueryDto
            {
                FromDate = new DateTime(2026, 9, 7),
                ToDate = new DateTime(2026, 9, 7),
                Compare = SalesComparisonModes.None
            },
            Utc(2026, 9, 7, 10));

        var result = await repository.GetExecutivePeriodAsync(
            storeId: 1,
            resolved.Current);

        Assert.Equal(2, result.Summary.SalesOrders);
        Assert.Equal(1500m, result.Summary.GrossSales);
        Assert.Equal(150m, result.Summary.Discounts);
        Assert.Equal(1350m, result.Summary.SalesAfterDiscount);
        Assert.Equal(1, result.Summary.CustomerLinkedOrders);

        Assert.Equal(150m, result.Summary.Returns);
        Assert.Equal(100m, result.Summary.RefundAmount);
        Assert.Equal(1, result.Summary.ReturnCount);
        Assert.Equal(1, result.Summary.RefundCount);

        Assert.Equal(1, result.Summary.VoidCount);
        Assert.Equal(250m, result.Summary.VoidValue);

        Assert.Equal(1350m, result.Trend.Sum(x => x.SalesAfterDiscount));
        Assert.Equal(150m, result.Trend.Sum(x => x.Returns));
        Assert.Equal(100m, result.Trend.Sum(x => x.RefundAmount));

        Assert.Equal(30m, result.DiscountBreakdown.ManualLine);
        Assert.Equal(40m, result.DiscountBreakdown.Promotion);
        Assert.Equal(30m, result.DiscountBreakdown.Combo);
        Assert.Equal(30m, result.DiscountBreakdown.ManualOrder);
        Assert.Equal(20m, result.DiscountBreakdown.Voucher);
        Assert.Equal(150m, result.DiscountBreakdown.TotalDiscounts);

        var top = Assert.Single(result.TopProducts.Where(x => x.VariantId == 101));
        Assert.Equal(13m, top.BaseQuantity);
        Assert.Equal(800m, top.GrossSales);
        Assert.Equal("chai", top.BaseUnitName);

        var hour8 = Assert.Single(result.SalesByHour.Where(x => x.Hour == 8));
        Assert.Equal(900m, hour8.SalesAfterDiscount);
        Assert.Equal(1, hour8.SalesOrders);

        var hour13 = Assert.Single(result.SalesByHour.Where(x => x.Hour == 13));
        Assert.Equal(150m, hour13.Returns);

        var terminals = await repository.GetTerminalOptionsAsync(1);
        Assert.Single(terminals);
        Assert.Equal("POS01", terminals[0].Code);
    }

    [Fact]
    public async Task GetExecutivePeriodAsync_should_apply_customer_state_without_changing_header_semantics()
    {
        var options = CreateOptions();

        await using (var seed = CreateContext(options))
        {
            seed.Orders.AddRange(
                NewOrder(
                    1,
                    OrderStatus.Completed,
                    Utc(2026, 9, 7, 1),
                    1000m,
                    100m,
                    900m,
                    customerId: 10),
                NewOrder(
                    1,
                    OrderStatus.Completed,
                    Utc(2026, 9, 7, 2),
                    500m,
                    50m,
                    450m,
                    customerId: null));

            await seed.SaveChangesAsync();
        }

        await using var read = CreateContext(options);
        var repository = new SalesReportReadRepository(read);
        var policy = new SalesReportingPeriodPolicy();
        var resolved = policy.Resolve(
            new SalesExecutiveDashboardQueryDto
            {
                FromDate = new DateTime(2026, 9, 7),
                ToDate = new DateTime(2026, 9, 7),
                Compare = SalesComparisonModes.None,
                CustomerState = SalesCustomerStates.Linked
            },
            Utc(2026, 9, 7, 10));

        var result = await repository.GetExecutivePeriodAsync(1, resolved.Current);

        Assert.Equal(1, result.Summary.SalesOrders);
        Assert.Equal(1000m, result.Summary.GrossSales);
        Assert.Equal(100m, result.Summary.Discounts);
        Assert.Equal(900m, result.Summary.SalesAfterDiscount);
        Assert.Equal(1, result.Summary.CustomerLinkedOrders);
    }

    [Fact]
    public async Task Detail_queries_should_be_store_scoped_paginated_and_keep_return_refund_separate()
    {
        var options = CreateOptions();
        await using (var seed = CreateContext(options))
        {
            var order = NewOrder(1, OrderStatus.Completed, Utc(2026, 9, 7, 2), 1000m, 100m, 900m, 10);
            order.OrderNumber = "SO-001";
            order.PromotionDiscountTotal = 30m;
            order.ComboDiscountTotal = 20m;
            order.OrderDiscount = 20m;
            order.VoucherDiscountTotal = 10m;
            seed.Orders.Add(order);
            await seed.SaveChangesAsync();
            seed.OrderLines.Add(NewLine(order, 101, "Nước A", "A-101", "chai", 2m, 12m, 500m, 20m));
            seed.SalesReturns.Add(NewReturn(1, order.Id, SalesReturnStatus.Completed, Utc(2026,9,7,5), 150m, 100m, "RET-001"));
            seed.Orders.Add(NewOrder(2, OrderStatus.Completed, Utc(2026,9,7,2), 9999m, 0m, 9999m, null));
            await seed.SaveChangesAsync();
        }

        await using var read = CreateContext(options);
        var repository = new SalesReportReadRepository(read);
        var policy = new SalesReportingPeriodPolicy();
        var resolved = policy.ResolveDetail(new SalesDetailQueryDto { FromDate = new DateTime(2026,9,7), ToDate = new DateTime(2026,9,7), Page=1, PageSize=25 }, Utc(2026,9,7,10));

        var orders = await repository.GetOrdersAsync(1, resolved);
        var products = await repository.GetProductsAsync(1, resolved);
        var discounts = await repository.GetDiscountsAsync(1, resolved);
        var returns = await repository.GetReturnsAsync(1, resolved);

        Assert.Single(orders.Items);
        Assert.Equal("SO-001", orders.Items[0].OrderNumber);
        Assert.Equal(1, orders.Items[0].ReturnCount);
        Assert.Equal(150m, orders.Items[0].ReturnValue);
        Assert.Single(products.Items);
        Assert.Equal(12m, products.Items[0].BaseQuantity);
        Assert.Single(discounts.Items);
        Assert.Equal(100m, discounts.Items[0].TotalDiscounts);
        Assert.Single(returns.Items);
        Assert.Equal(150m, returns.Items[0].ReturnSubtotal);
        Assert.Equal(100m, returns.Items[0].RefundAmount);
    }

    [Fact]
    public async Task SqlServer_relational_reporting_should_translate_store_date_return_buckets_paging_sort_and_representative_30_day_query()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();

        var storeOne = await database.SeedInventoryCatalogAsync();
        var storeTwo = await database.SeedInventoryCatalogAsync();

        var shiftOneId = await CreateRelationalShiftAsync(
            database,
            storeOne,
            terminalCode: "RPT-POS-01");
        var shiftTwoId = await CreateRelationalShiftAsync(
            database,
            storeTwo,
            terminalCode: "RPT-POS-02");

        int priorOrderId;

        await using (var seed = database.CreateHostContext())
        {
            var currentHigh = NewOrder(
                storeOne.StoreId,
                OrderStatus.Completed,
                Utc(2026, 9, 7, 1),
                1000m,
                100m,
                900m,
                customerId: null);
            currentHigh.POSShiftId = shiftOneId;
            currentHigh.OrderNumber = "RPT-SQL-001";
            currentHigh.PromotionDiscountTotal = 30m;
            currentHigh.ComboDiscountTotal = 20m;
            currentHigh.OrderDiscount = 20m;
            currentHigh.VoucherDiscountTotal = 10m;

            var currentLow = NewOrder(
                storeOne.StoreId,
                OrderStatus.Refunded,
                Utc(2026, 9, 7, 3),
                500m,
                50m,
                450m,
                customerId: null);
            currentLow.POSShiftId = shiftOneId;
            currentLow.OrderNumber = "RPT-SQL-002";

            var priorOrder = NewOrder(
                storeOne.StoreId,
                OrderStatus.Completed,
                Utc(2026, 8, 8, 12),
                700m,
                70m,
                630m,
                customerId: null);
            priorOrder.POSShiftId = shiftOneId;
            priorOrder.OrderNumber = "RPT-SQL-PRIOR";

            var outsideLowerBound = NewOrder(
                storeOne.StoreId,
                OrderStatus.Completed,
                Utc(2026, 8, 8, 16),
                800m,
                80m,
                720m,
                customerId: null);
            outsideLowerBound.POSShiftId = shiftOneId;
            outsideLowerBound.OrderNumber = "RPT-SQL-OUTSIDE";

            var otherStore = NewOrder(
                storeTwo.StoreId,
                OrderStatus.Completed,
                Utc(2026, 9, 7, 2),
                9999m,
                999m,
                9000m,
                customerId: null);
            otherStore.POSShiftId = shiftTwoId;
            otherStore.OrderNumber = "RPT-SQL-STORE-2";

            seed.Orders.AddRange(
                currentHigh,
                currentLow,
                priorOrder,
                outsideLowerBound,
                otherStore);
            await seed.SaveChangesAsync();
            priorOrderId = priorOrder.Id;

            seed.OrderLines.AddRange(
                NewLine(
                    currentHigh,
                    storeOne.ProductVariantId,
                    "Sản phẩm SQL A",
                    "RPT-SQL-A",
                    "chai",
                    quantity: 2m,
                    baseQuantity: 12m,
                    unitPrice: 300m,
                    lineDiscount: 20m),
                NewLine(
                    currentLow,
                    storeOne.ProductVariantId,
                    "Sản phẩm SQL A",
                    "RPT-SQL-A",
                    "chai",
                    quantity: 1m,
                    baseQuantity: 1m,
                    unitPrice: 200m,
                    lineDiscount: 10m));

            var currentReturnForPriorSale = NewReturn(
                storeOne.StoreId,
                priorOrderId,
                SalesReturnStatus.Completed,
                Utc(2026, 9, 7, 6),
                returnSubtotal: 150m,
                refundTotal: 100m,
                returnNumber: "RPT-SQL-RET-001");
            currentReturnForPriorSale.POSShiftId = shiftOneId;

            var otherStoreReturn = NewReturn(
                storeTwo.StoreId,
                otherStore.Id,
                SalesReturnStatus.Completed,
                Utc(2026, 9, 7, 6),
                returnSubtotal: 888m,
                refundTotal: 888m,
                returnNumber: "RPT-SQL-RET-STORE-2");
            otherStoreReturn.POSShiftId = shiftTwoId;

            seed.SalesReturns.AddRange(
                currentReturnForPriorSale,
                otherStoreReturn);

            var representativeStartUtc = Utc(2026, 8, 9, 5);
            for (var day = 0; day < 29; day++)
            {
                for (var sequence = 0; sequence < 2; sequence++)
                {
                    var historical = NewOrder(
                        storeOne.StoreId,
                        OrderStatus.Completed,
                        representativeStartUtc.AddDays(day).AddHours(sequence),
                        subtotal: 100m + sequence,
                        discounts: 10m,
                        grandTotal: 90m + sequence,
                        customerId: null);
                    historical.POSShiftId = shiftOneId;
                    historical.OrderNumber = $"RPT-SQL-H-{day:00}-{sequence}";
                    seed.Orders.Add(historical);
                }
            }

            await seed.SaveChangesAsync();
        }

        await using var read = database.CreateHostContext();
        var repository = new SalesReportReadRepository(read);
        var policy = new SalesReportingPeriodPolicy();

        var oneDay = policy.Resolve(
            new SalesExecutiveDashboardQueryDto
            {
                FromDate = new DateTime(2026, 9, 7),
                ToDate = new DateTime(2026, 9, 7),
                Compare = SalesComparisonModes.None
            },
            Utc(2026, 9, 7, 10));

        var executive = await repository.GetExecutivePeriodAsync(
            storeOne.StoreId,
            oneDay.Current);

        Assert.Equal(2, executive.Summary.SalesOrders);
        Assert.Equal(1500m, executive.Summary.GrossSales);
        Assert.Equal(150m, executive.Summary.Discounts);
        Assert.Equal(1350m, executive.Summary.SalesAfterDiscount);
        Assert.Equal(150m, executive.Summary.Returns);
        Assert.Equal(100m, executive.Summary.RefundAmount);
        Assert.Equal(1, executive.Summary.ReturnCount);
        Assert.Equal(1, executive.Summary.RefundCount);

        Assert.Equal(1350m, executive.Trend.Sum(x => x.SalesAfterDiscount));
        Assert.Equal(150m, executive.Trend.Sum(x => x.Returns));
        Assert.Equal(100m, executive.Trend.Sum(x => x.RefundAmount));

        var hour8 = Assert.Single(executive.SalesByHour.Where(x => x.Hour == 8));
        Assert.Equal(900m, hour8.SalesAfterDiscount);
        Assert.Equal(1, hour8.SalesOrders);

        var hour13 = Assert.Single(executive.SalesByHour.Where(x => x.Hour == 13));
        Assert.Equal(150m, hour13.Returns);

        var topProduct = Assert.Single(executive.TopProducts);
        Assert.Equal(storeOne.ProductVariantId, topProduct.VariantId);
        Assert.Equal(13m, topProduct.BaseQuantity);
        Assert.Equal(800m, topProduct.GrossSales);

        var detail = new SalesResolvedDetailQueryDto
        {
            Period = oneDay.Current.Period,
            EffectiveFromUtc = oneDay.Current.Period.FromUtc,
            EffectiveToUtcExclusive = oneDay.Current.Period.ToUtcExclusive,
            CustomerState = SalesCustomerStates.All,
            Page = 1,
            PageSize = 1,
            Sort = SalesDetailSorts.ValueDesc
        };

        var firstOrderPage = await repository.GetOrdersAsync(
            storeOne.StoreId,
            detail);

        Assert.Equal(2, firstOrderPage.TotalItems);
        Assert.Single(firstOrderPage.Items);
        Assert.Equal("RPT-SQL-001", firstOrderPage.Items[0].OrderNumber);
        Assert.Equal(900m, firstOrderPage.Items[0].SalesAfterDiscount);

        detail.Page = 2;
        var secondOrderPage = await repository.GetOrdersAsync(
            storeOne.StoreId,
            detail);

        Assert.Equal(2, secondOrderPage.TotalItems);
        Assert.Single(secondOrderPage.Items);
        Assert.Equal("RPT-SQL-002", secondOrderPage.Items[0].OrderNumber);
        Assert.Equal(450m, secondOrderPage.Items[0].SalesAfterDiscount);

        detail.Page = 1;
        detail.PageSize = 25;
        detail.Sort = SalesDetailSorts.Newest;
        var returns = await repository.GetReturnsAsync(
            storeOne.StoreId,
            detail);

        var currentReturn = Assert.Single(returns.Items);
        Assert.Equal("RPT-SQL-RET-001", currentReturn.ReturnNumber);
        Assert.Equal("RPT-SQL-PRIOR", currentReturn.OrderNumber);
        Assert.Equal(150m, currentReturn.ReturnSubtotal);
        Assert.Equal(100m, currentReturn.RefundAmount);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var thirtyDays = policy.Resolve(
            new SalesExecutiveDashboardQueryDto
            {
                FromDate = new DateTime(2026, 8, 9),
                ToDate = new DateTime(2026, 9, 7),
                Compare = SalesComparisonModes.None
            },
            Utc(2026, 9, 7, 10));

        var stopwatch = Stopwatch.StartNew();
        var representative = await repository.GetExecutivePeriodAsync(
            storeOne.StoreId,
            thirtyDays.Current,
            timeout.Token);
        stopwatch.Stop();

        Console.WriteLine($"RPT1_SQL_30_DAY_MS={stopwatch.ElapsedMilliseconds}");
        Assert.Equal(60, representative.Summary.SalesOrders);
        Assert.NotEmpty(representative.Trend);
    }

    private static OrderLine NewLine(
        Order order,
        int variantId,
        string itemName,
        string sku,
        string baseUnitName,
        decimal quantity,
        decimal baseQuantity,
        decimal unitPrice,
        decimal lineDiscount)
        => new()
        {
            StoreId = order.StoreId,
            OrderId = order.Id,
            Order = order,
            ProductId = variantId,
            VariantId = variantId,
            ItemName = itemName,
            Sku = sku,
            UnitName = baseUnitName,
            SellingUnitName = baseUnitName,
            BaseUnitName = baseUnitName,
            Quantity = quantity,
            BaseQuantity = baseQuantity,
            Multiplier = baseQuantity / quantity,
            UnitPrice = unitPrice,
            OriginalUnitPrice = unitPrice,
            LineDiscount = lineDiscount,
            LineTotal = quantity * unitPrice - lineDiscount,
            RowVersion = new byte[8]
        };

    private static Order NewOrder(
        int storeId,
        OrderStatus status,
        DateTime completedAtUtc,
        decimal subtotal,
        decimal discounts,
        decimal grandTotal,
        int? customerId)
        => new()
        {
            StoreId = storeId,
            Status = status,
            PaymentStatus = PaymentStatus.Paid,
            POSShiftId = 1,
            CompletedAtUtc = completedAtUtc,
            Subtotal = subtotal,
            DiscountTotal = discounts,
            GrandTotal = grandTotal,
            CustomerId = customerId,
            RowVersion = new byte[8]
        };

    private static SalesReturn NewReturn(
        int storeId,
        int orderId,
        SalesReturnStatus status,
        DateTime completedAtUtc,
        decimal returnSubtotal,
        decimal refundTotal,
        string returnNumber)
        => new()
        {
            StoreId = storeId,
            OrderId = orderId,
            POSShiftId = 1,
            ReturnNumber = returnNumber,
            Type = SalesReturnType.ReturnAndRefund,
            Status = status,
            Reason = "Synthetic report test",
            ReturnSubtotal = returnSubtotal,
            RefundTotal = refundTotal,
            CreatedByUserId = 99,
            CompletedAtUtc = completedAtUtc,
            RowVersion = new byte[8]
        };

    private static async Task<int> CreateRelationalShiftAsync(
        InventoryPostingLocalDb database,
        InventoryPostingSeed seed,
        string terminalCode)
    {
        await using var context = database.CreateTenantContext(seed.StoreId);

        var terminal = new POSTerminal
        {
            StoreId = seed.StoreId,
            Code = terminalCode,
            Name = $"Terminal {terminalCode}",
            IsActive = true,
            Status = POSTerminalStatus.Active
        };

        context.POSTerminals.Add(terminal);
        await context.SaveChangesAsync();

        var shift = new POSShift
        {
            StoreId = seed.StoreId,
            TerminalId = terminal.Id,
            WarehouseId = seed.WarehouseId,
            OpenedByUserId = 99,
            OpenedAtUtc = Utc(2026, 8, 1, 0),
            Status = POSShiftStatus.Open,
            OpeningCash = 0m,
            ClosingCashExpected = 0m
        };

        context.POSShifts.Add(shift);
        await context.SaveChangesAsync();

        return shift.Id;
    }

    private static DateTime Utc(
        int year,
        int month,
        int day,
        int hour)
        => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<InMemoryAppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static InMemoryAppDbContext CreateContext(
        DbContextOptions<InMemoryAppDbContext> options)
    {
        var context = new InMemoryAppDbContext(
            options,
            new TenantContext(),
            new TestCurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "sales-report-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
