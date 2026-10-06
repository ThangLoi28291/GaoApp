using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;
using GaoApp.Application.Services.Inventory;
using GaoApp.Tests.Inventory;

namespace GaoApp.Tests.Reports;

[Collection("R1FinalDatabasePreflight")]
public sealed class OperationsReportSqlServerTests
{
    private static async Task<object> SeedBrowserAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, FullApplicationFixture.Client client)
    {
        var day = DateTime.UtcNow.AddHours(7).Date;
        var customers = new List<int>();
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            foreach (var name in new[] { "Đại lý Minh An", "Cửa hàng Lan Phương", "Nguyễn Thị Hoa" }) {
                var customer = new Customer { StoreId = store.StoreId, Name = name, HaveDebt = true }; db.Customers.Add(customer); await db.SaveChangesAsync(); customers.Add(customer.Id);
            }
            var parent = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
            parent.Product.Name = "Gạo ST25"; parent.ProductVariantName = "Túi 5kg"; parent.Sku = "G-ST25-5KG"; parent.Price = 28000;
            (await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId)).Name = "Kho chính";
            (await db.Categories.SingleAsync(x => x.Id == parent.Product.CategoryId)).Name = "Gạo đặc sản";
            (await db.Units.SingleAsync(x => x.Id == parent.Product.BaseUnitId)).Name = "kg";
            var movements = InventoryPosPostingContractTests.CreateRealMovementService(db);
            foreach (var item in new[] { (Name: "Gạo thơm", Qty: 4m, Cost: 15000m), (Name: "Gạo nếp", Qty: 45m, Cost: 21000m) }) {
                var variant = new ProductVariant { StoreId = store.StoreId, ProductId = parent.ProductId, Sku = "DEMO-" + item.Qty, ProductVariantName = item.Name, Price = 28000, CostPrice = item.Cost };
                db.ProductVariants.Add(variant); await db.SaveChangesAsync();
                await movements.CreateAsync(new InventoryMovementFactory().CreatePurchaseReceipt(store.WarehouseId, variant.Id, item.Qty, item.Cost, "DEMO-" + variant.Id, 1, "DEMO-" + variant.Id, 1, day.AddDays(-30).AddHours(5)));
            }
            var document = new StockDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId, DocumentNo = "NK-00128", Type = StockDocumentType.Receipt }; db.StockDocuments.Add(document); await db.SaveChangesAsync();
            db.AddRange(new PurchasePayable { StoreId = store.StoreId, StockDocumentId = document.Id, SourceKey = "DEMO-1", PayeeName = "Nhà máy gạo Đồng Tháp", Amount = 18_500_000, RecognizedAtUtc = day.AddDays(-40).AddHours(5), DueDate = day.AddDays(-12) },
                new PurchasePayable { StoreId = store.StoreId, StockDocumentId = document.Id, SourceKey = "DEMO-2", PayeeName = "Vận tải Thanh Bình", Amount = 2_400_000, RecognizedAtUtc = day.AddDays(-15).AddHours(5), DueDate = day.AddDays(5), Type = PurchasePayableType.Freight });
            await db.SaveChangesAsync();
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        for (var i = 0; i < 3; i++) {
            var draft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft"); var orderId = draft.GetProperty("orderId").GetInt32();
            await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty={i + 2}");
            await client.JsonAsync(HttpMethod.Post, $"/admin/pos/cart/current/customer/{customers[i]}", new { repriceExistingLines = false });
            await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize-credit", new { clientRequestId = Guid.NewGuid(), expectedCustomerId = customers[i], expectedBalance = (i + 2) * 28000, dueDate = day.AddDays(10) });
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            var order = await db.Orders.SingleAsync(x => x.Id == orderId); order.CreditDueDate = i == 0 ? day.AddDays(-35) : i == 1 ? day.AddDays(-8) : day.AddDays(10); await db.SaveChangesAsync();
        }
        foreach (var fund in new[] { "cash", "bank" }) await client.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/opening", new TreasuryOpeningDto { Fund = fund, Date = day.AddDays(-14), Amount = fund == "cash" ? 15_000_000 : 48_000_000, Note = "Số dư demo đã đối chiếu" });
        for (var i = 0; i < 12; i++) {
            await client.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", new TreasuryWriteDto { ClientRequestId = Guid.NewGuid(), Date = day.AddDays(-i), Fund = i % 2 == 0 ? "cash" : "bank", Amount = i % 3 == 0 ? -(280_000 + i * 170_000) : 1_250_000 + i * 380_000, Name = i % 3 == 0 ? "Chi dịch vụ vận chuyển" : "Thu bán hàng đối chiếu chứng từ cũ", Reference = "PTC-" + (128 + i) });
        }
        return new { Day = day.ToString("yyyy-MM-dd") };
    }
    [Fact]
    public async Task Actual_money_is_idempotent_scoped_reversible_and_separate_from_recognition()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var owner = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var reader = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Report.Profit.View));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var day = DateTime.UtcNow.AddHours(7).Date; var query = $"?fromDate={day:yyyy-MM-dd}&toDate={day:yyyy-MM-dd}";
        foreach (var route in new[] { "cashflow", "inventory", "debts" }) Assert.Contains("data-operations-report", await owner.Http.GetStringAsync("/admin/reports/" + route));
        await owner.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/opening", new TreasuryOpeningDto { Fund = "cash", Date = day, Amount = 1000, Note = "Biên bản kiểm quỹ" });
        var input = new TreasuryWriteDto { ClientRequestId = Guid.NewGuid(), Date = day, Fund = "cash", Amount = -100, Name = "=SUM(1,2)" };
        var first = await owner.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", input);
        Assert.Equal(first.GetProperty("id").GetInt32(), (await owner.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", input)).GetProperty("id").GetInt32());
        input.Amount = -101;
        using (var mismatch = await owner.Http.PostAsJsonAsync("/admin/reports/treasury/entries", input)) Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        var report = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!;
        Assert.Equal(900, report.Summary.Single(x => x.Fund == "cash").Closing); Assert.Equal(100, report.Summary.Single(x => x.Fund == "cash").Outflow);
        Assert.Null(report.Summary.Single(x => x.Fund == "bank").Closing);
        Assert.Equal(0, (await foreign.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!.Count);
        using (var denied = await reader.Http.PostAsJsonAsync("/admin/reports/treasury/entries", input)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await reader.Http.GetAsync("/admin/reports/operations/cashflow/export" + query)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var csv = await owner.Http.GetStringAsync("/admin/reports/operations/cashflow/export" + query);
        Assert.Contains("'=SUM(1,2)", csv); Assert.Matches("\"-100(?:\\.0+)?\"", csv); Assert.DoesNotContain("\"'-100", csv);
        var row = Assert.Single(report.Items);
        var reverse = new TreasuryReverseDto { ClientRequestId = Guid.NewGuid(), Date = day, RowVersion = row.RowVersion!, Reason = "Hoàn trả khoản đã chi" };
        using (var denied = await foreign.Http.PostAsJsonAsync($"/admin/reports/treasury/entries/{row.ManualId}/reverse", reverse)) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        await owner.JsonAsync(HttpMethod.Post, $"/admin/reports/treasury/entries/{row.ManualId}/reverse", reverse);
        await owner.JsonAsync(HttpMethod.Post, $"/admin/reports/treasury/entries/{row.ManualId}/reverse", reverse);
        report = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!;
        Assert.Equal(2, report.Count); Assert.Equal(1000, report.Summary.Single(x => x.Fund == "cash").Closing);
        await owner.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", new TreasuryWriteDto { ClientRequestId = Guid.NewGuid(), Date = day, Fund = "cash", TargetFund = "bank", Amount = -200, Name = "Nộp ngân hàng" });
        report = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!;
        Assert.Equal(0, report.Items.Sum(x => x.Amount)); Assert.Equal(800, report.Summary.Single(x => x.Fund == "cash").Closing);
        Assert.Equal(200, report.Summary.Single(x => x.Fund == "bank").Inflow); Assert.Equal(2, report.Items.Count(x => x.IsTransfer));
        var exp = await owner.JsonAsync(HttpMethod.Post, "/admin/reports/expenses", new ExpenseWriteDto { ClientRequestId = Guid.NewGuid(), Name = "Tiền thuê", Amount = 90, RecognitionFrom = day, RecognitionTo = day.AddDays(2) });
        var confirmed = await owner.JsonAsync(HttpMethod.Post, $"/admin/reports/expenses/{exp.GetProperty("id")}/confirm", new { rowVersion = exp.GetProperty("rowVersion").GetString() });
        await owner.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", new TreasuryWriteDto { ClientRequestId = Guid.NewGuid(), Date = day, Fund = "cash", Amount = -90, Name = "Thanh toán thuê", OperatingExpenseId = exp.GetProperty("id").GetInt32(), SourceRowVersion = confirmed.GetProperty("rowVersion").GetString() });
        report = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!;
        Assert.Equal(710, report.Summary.Single(x => x.Fund == "cash").Closing);
        var management = (await owner.Http.GetFromJsonAsync<ManagementReportDto>("/admin/reports/management/data" + query + "&compare=none"))!;
        Assert.Equal(30, management.Current.OperatingExpenses);
        var legacy = await owner.JsonAsync(HttpMethod.Post, "/admin/reports/expenses", new ExpenseWriteDto { ClientRequestId = Guid.NewGuid(), Name = "Chi phí cũ đã trả", Amount = 15, IsPaid = true, RecognitionFrom = day, RecognitionTo = day });
        var legacyConfirmed = await owner.JsonAsync(HttpMethod.Post, $"/admin/reports/expenses/{legacy.GetProperty("id")}/confirm", new { rowVersion = legacy.GetProperty("rowVersion").GetString() });
        var sources = (await owner.Http.GetFromJsonAsync<TreasurySourcesDto>("/admin/reports/treasury/sources?expenseId=" + legacy.GetProperty("id")))!;
        Assert.True(Assert.Single(sources.Expenses).RequiresEvidence);
        await owner.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", new TreasuryWriteDto { ClientRequestId = Guid.NewGuid(), Date = day, Fund = "cash", Amount = -15, Name = "Bổ sung thanh toán cũ", Reference = "PC-CU-01", ReconcileLegacyPayment = true, OperatingExpenseId = legacy.GetProperty("id").GetInt32(), SourceRowVersion = legacyConfirmed.GetProperty("rowVersion").GetString() });
        var paidExpenses = (await owner.Http.GetFromJsonAsync<ExpenseListDto>("/admin/reports/expenses/data" + query))!;
        await owner.JsonAsync(HttpMethod.Post, $"/admin/reports/expenses/{exp.GetProperty("id")}/void", new { rowVersion = paidExpenses.Items.Single(x => x.Id == exp.GetProperty("id").GetInt32()).RowVersion, reason = "Hủy kỳ ghi nhận; tiền đã chi vẫn giữ" });
        report = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!;
        Assert.Equal(695, report.Summary.Single(x => x.Fund == "cash").Closing);
        Assert.Equal(15, (await owner.Http.GetFromJsonAsync<ManagementReportDto>("/admin/reports/management/data" + query + "&compare=none"))!.Current.OperatingExpenses);
        var token = owner.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single(); owner.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var denied = await owner.Http.PostAsJsonAsync("/admin/reports/treasury/entries", input)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        owner.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
    }

    [Fact]
    public async Task Credit_collection_and_deposits_are_counted_once_and_stock_reconciles()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        int customerId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            var role = await db.Roles.SingleAsync(x => x.Code == "ADMIN");
            (await db.UserInStores.SingleAsync(x => x.UserId == account.UserId)).RoleId = role.Id;
            var customer = new Customer { StoreId = store.StoreId, Name = "Khách công nợ báo cáo", HaveDebt = true }; db.Customers.Add(customer);
            await db.SaveChangesAsync(); customerId = customer.Id;
        }
        using var owner = await app.LoginAsync(account);
        using var nonAdmin = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Report.Profit.View));
        var day = DateTime.UtcNow.AddHours(7).Date; var query = $"?fromDate={day:yyyy-MM-dd}&toDate={day:yyyy-MM-dd}";
        await owner.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 500, warehouseId = store.WarehouseId });
        var draft = await owner.JsonAsync(HttpMethod.Post, "/admin/pos/draft"); var orderId = draft.GetProperty("orderId").GetInt32();
        await owner.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=3");
        await owner.JsonAsync(HttpMethod.Post, $"/admin/pos/cart/current/customer/{customerId}", new { repriceExistingLines = false });
        await owner.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize-credit", new { clientRequestId = Guid.NewGuid(), expectedCustomerId = customerId, expectedBalance = 60, dueDate = day.AddDays(7) });
        await owner.JsonAsync(HttpMethod.Post, "/admin/customer-debt/collect", new { clientRequestId = Guid.NewGuid(), customerId, orderId, amount = 20, method = 0 });
        var flow = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!;
        Assert.Equal(20, flow.Summary.Single(x => x.Fund == "cash").Inflow); Assert.Single(flow.Items);
        var debt = (await owner.Http.GetFromJsonAsync<DebtReportDto>("/admin/reports/operations/debts/data" + query))!;
        var row = Assert.Single(debt.Items); Assert.Equal(0, row.Opening); Assert.Equal(60, row.Increase); Assert.Equal(20, row.Decrease); Assert.Equal(40, row.Closing); Assert.Equal("current", row.Aging);
        var stock = (await owner.Http.GetFromJsonAsync<StockReportDto>("/admin/reports/operations/inventory/data" + query))!;
        Assert.True(stock.CanViewCost); var sku = Assert.Single(stock.Items); Assert.Equal(sku.Closing, sku.Opening + sku.Inbound - sku.Outbound); Assert.Equal(97, sku.Closing); Assert.Equal(970, sku.ClosingValue);
        var noCost = (await nonAdmin.Http.GetFromJsonAsync<StockReportDto>("/admin/reports/operations/inventory/data" + query))!;
        Assert.False(noCost.CanViewCost); Assert.Null(noCost.InventoryValue); Assert.All(noCost.Items, x => { Assert.Null(x.OpeningValue); Assert.Null(x.ClosingValue); });
        // Legacy debt mirror matching remains exact and safe when the new FK was not stored.
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) { (await db.POSShiftCashTransactions.SingleAsync()).CustomerDebtReceiptId = null; await db.SaveChangesAsync(); }
        flow = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!; Assert.Single(flow.Items); Assert.Equal(20, flow.Items.Sum(x => x.Amount));
        var deposit = await owner.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/receive", new { clientRequestId = Guid.NewGuid(), customerId, amount = 30, method = 0, purpose = "Đặt hàng báo cáo", expectedDeliveryDate = day.AddDays(3) });
        await owner.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/refund", new { clientRequestId = Guid.NewGuid(), depositId = deposit.GetProperty("depositId").GetInt32(), amount = 10, method = 0, note = "Hoàn một phần cọc" });
        flow = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!;
        Assert.Equal(3, flow.Count); Assert.Equal(40, flow.Items.Sum(x => x.Amount));
    }

    [Fact]
    public async Task Linking_a_cash_voucher_and_cancelling_its_classification_never_moves_money_twice()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*"); using var owner = await app.LoginAsync(account);
        var day = DateTime.UtcNow.AddHours(7).Date; var query = $"?fromDate={day:yyyy-MM-dd}&toDate={day:yyyy-MM-dd}";
        await owner.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 500, warehouseId = store.WarehouseId });
        int voucherId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            var shift = await db.POSShifts.SingleAsync();
            var voucher = new POSShiftCashTransaction { StoreId = store.StoreId, POSShiftId = shift.Id, Type = POSShiftCashTransactionType.CashOut, Amount = 25, Reason = "Chi vật tư", CreatedByUserId = account.UserId };
            db.POSShiftCashTransactions.Add(voucher); await db.SaveChangesAsync(); voucherId = voucher.Id;
        }
        var input = new TreasuryWriteDto { ClientRequestId = Guid.NewGuid(), Date = day, Fund = "cash", Amount = -25, Name = "Chi vật tư đã đối chiếu", POSShiftCashTransactionId = voucherId };
        await owner.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", input);
        var flow = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!;
        var row = Assert.Single(flow.Items); Assert.Equal(-25, row.Amount); Assert.Equal("Liên kết phiếu ca", row.Source); Assert.True(row.CanReverse);
        await owner.JsonAsync(HttpMethod.Post, $"/admin/reports/treasury/entries/{row.ManualId}/reverse", new TreasuryReverseDto { ClientRequestId = Guid.NewGuid(), Date = day, RowVersion = row.RowVersion!, Reason = "Hủy phân loại, giữ tiền mặt gốc" });
        flow = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!;
        Assert.Equal(-25, Assert.Single(flow.Items).Amount);
        input.ClientRequestId = Guid.NewGuid(); await owner.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", input);
        flow = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!; Assert.Equal(-25, Assert.Single(flow.Items).Amount);
        row = Assert.Single(flow.Items);
        await owner.JsonAsync(HttpMethod.Post, $"/admin/reports/treasury/entries/{row.ManualId}/reverse", new TreasuryReverseDto { ClientRequestId = Guid.NewGuid(), Date = day, RowVersion = row.RowVersion!, Reason = "Đối chiếu lại là luân chuyển nội bộ" });
        input.ClientRequestId = Guid.NewGuid(); input.TargetFund = "cash"; input.Name = "Rút tiền ca nộp két lớn";
        await owner.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", input);
        flow = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!;
        Assert.Equal(2, flow.Count); Assert.Equal(0, flow.Items.Sum(x => x.Amount)); Assert.All(flow.Items, x => Assert.True(x.IsTransfer)); Assert.Equal(0, flow.Trend.Sum(x => x.Net));
    }

    [Fact]
    public async Task Supplier_partial_payments_aging_and_reversals_reconcile_at_historical_cutoff()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var owner = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var day = DateTime.UtcNow.AddHours(7).Date; var query = $"?fromDate={day:yyyy-MM-dd}&toDate={day:yyyy-MM-dd}";
        int id; string version;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            var document = new StockDocument { StoreId = store.StoreId, WarehouseId = store.WarehouseId, DocumentNo = "REPORT-NCC", Type = StockDocumentType.Receipt };
            db.StockDocuments.Add(document); await db.SaveChangesAsync();
            var payable = new PurchasePayable { StoreId = store.StoreId, StockDocumentId = document.Id, SourceKey = "REPORT-NCC", Amount = 300, PayeeName = "NCC báo cáo", RecognizedAtUtc = day.AddDays(-40).AddHours(5), DueDate = day.AddDays(-31) };
            db.Add(payable); await db.SaveChangesAsync(); id = payable.Id; version = Convert.ToBase64String(payable.RowVersion);
        }
        var before = (await owner.Http.GetFromJsonAsync<DebtReportDto>("/admin/reports/operations/debts/data" + query))!; Assert.Equal(300, before.Payable); Assert.Equal("31-60", Assert.Single(before.Items).Aging);
        await owner.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", new TreasuryWriteDto { ClientRequestId = Guid.NewGuid(), Date = day, Fund = "bank", Amount = -100, Name = "Trả một phần NCC", PurchasePayableId = id, SourceRowVersion = version });
        var after = (await owner.Http.GetFromJsonAsync<DebtReportDto>("/admin/reports/operations/debts/data" + query))!;
        var row = Assert.Single(after.Items); Assert.Equal(300, row.Opening); Assert.Equal(100, row.Decrease); Assert.Equal(200, row.Closing);
        var flow = (await owner.Http.GetFromJsonAsync<CashFlowReportDto>("/admin/reports/operations/cashflow/data" + query))!;
        var payment = Assert.Single(flow.Items);
        await owner.JsonAsync(HttpMethod.Post, $"/admin/reports/treasury/entries/{payment.ManualId}/reverse", new TreasuryReverseDto { ClientRequestId = Guid.NewGuid(), Date = day, RowVersion = payment.RowVersion!, Reason = "NCC hoàn lại tiền" });
        after = (await owner.Http.GetFromJsonAsync<DebtReportDto>("/admin/reports/operations/debts/data" + query))!; Assert.Equal(300, after.Payable);
        var yesterday = $"?fromDate={day.AddDays(-1):yyyy-MM-dd}&toDate={day.AddDays(-1):yyyy-MM-dd}";
        Assert.Equal(300, (await owner.Http.GetFromJsonAsync<DebtReportDto>("/admin/reports/operations/debts/data" + yesterday))!.Payable);
        int legacyId; string legacyVersion;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            var documentId = (await db.Set<PurchasePayable>().SingleAsync(x => x.Id == id)).StockDocumentId;
            var legacy = new PurchasePayable { StoreId = store.StoreId, StockDocumentId = documentId, SourceKey = "REPORT-OLD-PAID", Amount = 12, PayeeName = "NCC đã trả trước", RecognizedAtUtc = day.AddDays(-40).AddHours(5), Status = PurchasePayableStatus.Paid, PaidAtUtc = day.AddDays(-2).AddHours(5) };
            db.Add(legacy); await db.SaveChangesAsync(); legacyId = legacy.Id; legacyVersion = Convert.ToBase64String(legacy.RowVersion);
        }
        var sources = (await owner.Http.GetFromJsonAsync<TreasurySourcesDto>("/admin/reports/treasury/sources?payableId=" + legacyId))!; Assert.True(Assert.Single(sources.Payables).RequiresEvidence);
        await owner.JsonAsync(HttpMethod.Post, "/admin/reports/treasury/entries", new TreasuryWriteDto { ClientRequestId = Guid.NewGuid(), Date = day.AddDays(-2), Fund = "bank", Amount = -12, Name = "Bổ sung chứng từ NCC", Reference = "UNC-CU-001", PurchasePayableId = legacyId, SourceRowVersion = legacyVersion, ReconcileLegacyPayment = true });
        Assert.Equal(300, (await owner.Http.GetFromJsonAsync<DebtReportDto>("/admin/reports/operations/debts/data" + yesterday))!.Payable);
        using(var missing = await owner.Http.GetAsync("/admin/reports/operations/inventory/data" + query + "&warehouseId=" + app.Stores[1].WarehouseId)) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using(var invalid = await owner.Http.GetAsync("/admin/reports/operations/cashflow/data?fromDate=2020-01-01&toDate=2026-01-01")) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
