using System.Collections;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Tests.Security;
using GaoApp.Tests.Reports;
using Microsoft.EntityFrameworkCore;

// This standalone probe uses the existing disposable fixture, never a configured application database.
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
object Read(object target, string name) => target.GetType().GetProperty(name, flags)!.GetValue(target)!;
async Task<object> Call(object? target, Type type, string method, params object[] parameters)
{
    var task = (Task)type.GetMethod(method, flags)!.Invoke(target, parameters)!;
    await task; return task.GetType().GetProperty("Result")!.GetValue(task)!;
}
var type = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.FullApplicationFixture")!;
var fixture = await Call(null, type, args.Contains("--pos-session-mobile") || args.Contains("--pos-order-acceptance") || args.Contains("--pos-order-detail") || args.Contains("--delivery-picking") || args.Contains("--delivery-inventory") || args.Contains("--delivery-trips") ? "StartDeliveryAsync" : args.Contains("--receipt-supplier") || args.Contains("--input-invoice-library") ? "StartWithInvoiceLibraryAsync" : "StartAsync");
await using var cleanup = (IAsyncDisposable)fixture;
var store = ((IEnumerable)Read(fixture, "Stores")).Cast<object>().First();
var account = args.Contains("--receipt-templates") || args.Contains("--kiosk") || args.Contains("--operations-reports") || args.Contains("--role-permissions") || args.Contains("--shift-admin")
    || args.Contains("--pos-shift-records-mobile")
    || args.Contains("--pos-shift-review-mobile")
    ? await Call(null, typeof(PosShiftAdministrationSqlServerTests), "AddAdminAsync", fixture, store)
    : await Call(fixture, type, "AddAccountAsync", store, new[] { "*" });
var client = await Call(fixture, type, "LoginAsync", account);
using var clientCleanup = (IDisposable)client;
var http = (HttpClient)Read(client, "Http");
if (args.Contains("--receiving-list"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.ReceivingListBrowserFixture")!;
    var seed = await Call(null, seam, "PrepareAsync", fixture, store);
    var before = (string)await Call(null, seam, "FingerprintAsync", fixture, store);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    object AccountData(object value) => new { user = Read(value, "Name"), password = Read(value, "Password") };
    var receivingListStart = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    receivingListStart.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    receivingListStart.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "receiving-list.browser.cjs"));
    using var probe = Process.Start(receivingListStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        manager = AccountData(account), employee = AccountData(Read(seed, "Employee")), denied = AccountData(Read(seed, "Denied")),
        terminalId = Read(store, "TerminalId"), otherLegalEntityId = Read(seed, "OtherLegalEntityId"),
        otherWarehouseId = Read(seed, "OtherWarehouseId"), activeIds = Read(seed, "ActiveIds")
    }));
    probe.StandardInput.Close();
    using var receivingListTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
    try { await probe.WaitForExitAsync(receivingListTimeout.Token); }
    catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("Receiving list browser failed.");
    if (before != (string)await Call(null, seam, "FingerprintAsync", fixture, store))
        throw new InvalidOperationException("Read-only list browser changed documents, stock, payables or store menus.");
    Console.WriteLine("RECEIVING LIST PASS — real Razor/HTTP/SQL, business data and store menus unchanged; disposable SQL only.");
    return;
}
if (args.Contains("--receipt-approval-workspace"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.ReceiptApprovalWorkspaceBrowserFixture")
        ?? throw new InvalidOperationException("Receipt approval workspace browser fixture is missing.");
    var seed = await Call(null, seam, "PrepareAsync", fixture, store);
    var receiptId = (int)Read(seed, "ReceiptId");
    var workspaceDatabase = Read(fixture, "Database");
    AppDbContext ReceiptContext() => (AppDbContext)workspaceDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(workspaceDatabase, new object?[] { Read(store, "StoreId"), null })!;
    Dictionary<int, decimal> beforeQuantities;
    Dictionary<int, decimal> receivedQuantities;
    await using (var before = ReceiptContext())
    {
        beforeQuantities = (await before.InventoryBalances.AsNoTracking().ToListAsync())
            .GroupBy(x => x.ProductVariantId).ToDictionary(x => x.Key, x => x.Sum(v => v.OnHandQty));
        receivedQuantities = (await before.StockDocumentLines.AsNoTracking()
            .Where(x => x.StockDocumentId == receiptId && !x.IsDeleted).ToListAsync())
            .GroupBy(x => x.ProductVariantId).ToDictionary(x => x.Key, x => x.Sum(v => v.BaseQuantity));
    }
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var evidence = Environment.GetEnvironmentVariable("GAO_RECEIPT_BROWSER_OUTPUT")
        ?? Path.Combine(source, "TestResults", "receipt-approval-workspace");
    Directory.CreateDirectory(evidence);
    var browserStart = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    browserStart.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    browserStart.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "receipt-approval-workspace.browser.cjs"));
    using var probe = Process.Start(browserStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"),
        receiptId, changedLineId = Read(seed, "ChangedLineId"), secondChangedLineId = Read(seed, "SecondChangedLineId"),
        addedLineId = Read(seed, "AddedLineId"), unchangedLineId = Read(seed, "UnchangedLineId"),
        savedPrice = Read(seed, "SavedPrice"), taxId = Read(seed, "TaxId"), evidenceDirectory = evidence
    }));
    probe.StandardInput.Close();
    using var workspaceTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
    try { await probe.WaitForExitAsync(workspaceTimeout.Token); }
    catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("Receipt approval workspace browser failed.");
    await using var verify = ReceiptContext();
    var receipt = await verify.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == receiptId);
    var lines = receipt.Lines.Where(x => !x.IsDeleted).ToArray();
    var merchandise = lines.Sum(x => decimal.Round(x.Quantity * x.UnitPriceBeforeVat, 2, MidpointRounding.AwayFromZero) + x.VatAmount);
    if (receipt.Status != StockDocumentStatus.Confirmed || !receipt.HasVat || !receipt.IncludeVatInInventoryCost ||
        !receipt.HasFreight || !receipt.CapitalizeFreightInInventoryCost || receipt.FreightTotal != 12345m ||
        receipt.IsMerchandisePaid || receipt.IsFreightPaid || receipt.TotalAmount != merchandise)
        throw new InvalidOperationException("Browser approval did not preserve the reviewed VAT, freight and settlement choices.");
    var payables = await verify.PurchasePayables.Where(x => x.StockDocumentId == receiptId).ToListAsync();
    if (payables.Count != 2 || !payables.Any(x => x.Type == PurchasePayableType.Merchandise && x.Status == PurchasePayableStatus.Outstanding && x.Amount == merchandise) ||
        !payables.Any(x => x.Type == PurchasePayableType.Freight && x.Status == PurchasePayableStatus.Outstanding && x.Amount == 12345m))
        throw new InvalidOperationException("Approval must record merchandise and freight payables separately and once.");
    var transactions = await verify.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.StockDocument && x.ReferenceId == receiptId.ToString()).ToListAsync();
    var approvalAudits = await verify.PurchaseReceiptAuditEvents.Where(x => x.StockDocumentId == receiptId)
        .Select(x => new { x.Id, x.EventType, x.IsSuccess }).ToListAsync();
    var postingEvidence = new
    {
        receiptId, lineCount = lines.Length, transactionCount = transactions.Count,
        commercialInventoryValue = merchandise + 12345m, postedInventoryValue = transactions.Sum(x => x.TotalCost),
        lines = lines.Select(x => new { x.Id, x.LineNo, x.BaseQuantity, x.LineTotal, x.VatAmount, x.FreightAllocation }),
        postings = transactions.Select(x => new { x.Id, x.ReferenceLineId, x.QuantityChange, x.UnitCostSnapshot, x.TotalCost, x.TransactionType }),
        approvalAudits
    };
    var postingEvidenceJson = JsonSerializer.Serialize(postingEvidence);
    File.WriteAllText(Path.Combine(evidence, "sql-posting-checks.json"), postingEvidenceJson);
    if (transactions.Count != lines.Length)
        throw new InvalidOperationException("Approval must post every active line exactly once. " + postingEvidenceJson);
    foreach (var line in lines)
    {
        var matching = transactions.Where(x => x.ReferenceLineId == line.Id).ToArray();
        if (matching.Length != 1 || matching[0].QuantityChange != line.BaseQuantity)
            throw new InvalidOperationException("Each active line must have exactly one posting for its reviewed base quantity. " + postingEvidenceJson);
        // Receipt pricing supplies a six-decimal base cost; the existing legacy
        // valuation engine then rounds unit cost/value to four decimals. Verify
        // that exact policy rather than equating its result with commercial cents.
        var commercialLineValue = line.LineTotal + line.FreightAllocation;
        var receiptBaseCost = decimal.Round(commercialLineValue / line.BaseQuantity, 6, MidpointRounding.AwayFromZero);
        var valuationUnitCost = decimal.Round(receiptBaseCost, 4, MidpointRounding.AwayFromZero);
        var valuationValue = decimal.Round(line.BaseQuantity * valuationUnitCost, 4, MidpointRounding.AwayFromZero);
        if (matching[0].UnitCostSnapshot != valuationUnitCost || matching[0].TotalCost != valuationValue)
            throw new InvalidOperationException("Posted unit/value cost must match the existing valuation precision policy including selected VAT and freight. " + postingEvidenceJson);
    }
    if (approvalAudits.Count(x => x.EventType == PurchaseReceiptAuditEventType.CommercialApprovalConfirmed && x.IsSuccess) != 1)
        throw new InvalidOperationException("Approval must produce one successful commercial approval audit event. " + postingEvidenceJson);
    var afterQuantities = (await verify.InventoryBalances.AsNoTracking().ToListAsync()).GroupBy(x => x.ProductVariantId)
        .ToDictionary(x => x.Key, x => x.Sum(v => v.OnHandQty));
    foreach (var pair in receivedQuantities)
        if (afterQuantities.GetValueOrDefault(pair.Key) != beforeQuantities.GetValueOrDefault(pair.Key) + pair.Value)
            throw new InvalidOperationException("Browser approval stock quantities were posted incorrectly or more than once.");
    Console.WriteLine("RECEIPT APPROVAL WORKSPACE PASS — actual Razor/HTTP, exact stock and cost posting, separate payables, one approval audit; disposable SQL only.");
    return;
}
if (args.Contains("--receipt-revision-ux"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.ReceiptRevisionBrowserFixture")
        ?? throw new InvalidOperationException("Receipt revision browser fixture is missing.");
    var seed = await Call(null, seam, "PrepareAsync", fixture, store);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var evidence = Environment.GetEnvironmentVariable("GAO_RECEIPT_BROWSER_OUTPUT")
        ?? Path.Combine(source, "TestResults", "receipt-revision-ux");
    Directory.CreateDirectory(evidence);
    var revisionStart = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    revisionStart.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    revisionStart.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "receipt-revision-ux.browser.cjs"));
    using var probe = Process.Start(revisionStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"),
        receiptId = Read(seed, "ReceiptId"), changedLineId = Read(seed, "ChangedLineId"),
        addedLineId = Read(seed, "AddedLineId"), unchangedLineId = Read(seed, "UnchangedLineId"),
        savedPrice = Read(seed, "SavedPrice"), evidenceDirectory = evidence
    }));
    probe.StandardInput.Close();
    using var revisionTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
    try { await probe.WaitForExitAsync(revisionTimeout.Token); }
    catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("Receipt revision UX browser failed.");
    var revisionDatabase = Read(fixture, "Database");
    await using var verify = (AppDbContext)revisionDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(revisionDatabase, new object?[] { Read(store, "StoreId"), null })!;
    var receiptId = (int)Read(seed, "ReceiptId");
    if ((await verify.StockDocuments.SingleAsync(x => x.Id == receiptId)).Status != StockDocumentStatus.Rejected ||
        (await verify.InventoryBalances.SingleAsync()).OnHandQty != 100 || await verify.PurchasePayables.AnyAsync())
        throw new InvalidOperationException("Review/return browser must preserve inventory and payables.");
    Console.WriteLine("RECEIPT REVISION UX PASS — actual Razor, actual HTTP, disposable SQL only.");
    return;
}
if (args.Contains("--delivery-trips"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Delivery.DeliveryD06BrowserFixture")
        ?? throw new InvalidOperationException("D06 browser fixture seam is missing.");
    var seed = await Call(null, seam, "PrepareAsync", fixture);
    var tripStoreId = (int)Read(seed, "StoreId");
    var tripDatabase = Read(fixture, "Database");
    AppDbContext TripContext() => (AppDbContext)tripDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(tripDatabase, new object?[] { tripStoreId, null })!;
    object AccountData(object value) => new { name = Read(value, "Name"), password = Read(value, "Password"), userId = Read(value, "UserId") };
    object OrderData(object value) => new { id = Read(value, "Id"), lookupToken = Read(value, "LookupToken"), version = Read(value, "Version"),
        code = Read(value, "Code"), sourceWarehouseId = Read(value, "SourceWarehouseId"), sourceLegalEntityId = Read(value, "SourceLegalEntityId"),
        sourceWarehouseName = Read(value, "SourceWarehouseName"), sourceLegalEntityName = Read(value, "SourceLegalEntityName"), lines = Read(value, "Lines") };
    object TripData(object value) => new { id = Read(value, "Id"), version = Read(value, "Version"), orderIds = Read(value, "OrderIds") };
    var seedOrders = ((IEnumerable)Read(seed, "Orders")).Cast<object>().ToArray();
    var primaryIds = seedOrders.Select(x => (int)Read(x, "Id")).ToArray();
    var spareId = (int)Read(Read(seed, "Spare"), "Id");
    int beforeTripReceipts, beforeTripHistory, beforeTripOutbox, beforePayments, beforeCompleted, beforeMoves, beforeOrderReceipts, beforeOrderHistory, beforeOrderOutbox;
    Dictionary<int, (decimal Quantity, decimal Value)> beforeBalances;
    await using (var before = TripContext())
    {
        beforeTripReceipts = await before.DeliveryTripCommandReceipts.CountAsync(); beforeTripHistory = await before.DeliveryTripRevisions.CountAsync(); beforeTripOutbox = await before.DeliveryTripOutboxMessages.CountAsync();
        beforePayments = await before.OrderPayments.CountAsync(); beforeCompleted = await before.Orders.CountAsync(x => x.Status == OrderStatus.Completed); beforeMoves = await before.InventoryTransactions.CountAsync();
        beforeOrderReceipts = await before.DeliveryCommandReceipts.CountAsync(x => primaryIds.Contains(x.DeliveryOrderId));
        beforeOrderHistory = await before.DeliveryRevisions.CountAsync(x => primaryIds.Contains(x.DeliveryOrderId)); beforeOrderOutbox = await before.DeliveryOutboxMessages.CountAsync(x => primaryIds.Contains(x.DeliveryOrderId));
        beforeBalances = (await before.InventoryBalances.AsNoTracking().ToArrayAsync()).ToDictionary(x => x.Id, x => (x.OnHandQty, x.InventoryValue));
        if (await before.DeliveryJournalEntries.AnyAsync(x => primaryIds.Contains(x.DeliveryOrderId))) throw new InvalidOperationException("D06 primary orders must start without stock or fabricated custody.");
    }
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var evidence = Environment.GetEnvironmentVariable("GAO_DELIVERY_TRIP_TEST_OUTPUT") ?? Path.Combine(source, "TestResults", "delivery-trips"); Directory.CreateDirectory(evidence);
    var tripStart = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    tripStart.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules"); tripStart.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "delivery-trip.browser.cjs"));
    using var probe = Process.Start(tripStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        terminalId = Read(seed, "TerminalId"), storeId = tripStoreId, orders = seedOrders.Select(OrderData), spare = OrderData(Read(seed, "Spare")),
        creator = AccountData(Read(seed, "Creator")), viewer = AccountData(Read(seed, "ViewOnly")), depart = AccountData(Read(seed, "DepartOnly")),
        reassign = AccountData(Read(seed, "Reassign")), cancel = AccountData(Read(seed, "Cancel")), courier = AccountData(Read(seed, "Courier")), otherCourier = AccountData(Read(seed, "OtherCourier")),
        handedOver = TripData(Read(seed, "HandedOver")), departed = TripData(Read(seed, "Departed")), expected = Read(seed, "Expected"), evidenceDirectory = evidence }));
    probe.StandardInput.Close(); using var tripTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
    try { await probe.WaitForExitAsync(tripTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("D06 trip browser failed.");
    using var result = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "browser-checks.json")));
    if (result.RootElement.GetProperty("checks").GetArrayLength() != 6) throw new InvalidOperationException("D06 six browser groups are incomplete.");
    var commands = result.RootElement.GetProperty("commandIds").EnumerateArray().Select(x => Guid.Parse(x.GetString()!)).Distinct().ToArray();
    if (commands.Length != 9) throw new InvalidOperationException("D06 expected exactly9 committed root commands.");
    var mainTripId = result.RootElement.GetProperty("tripId").GetInt32();
    await using var verify = TripContext();
    if (await verify.DeliveryTripCommandReceipts.CountAsync(x => commands.Contains(x.ClientRequestId)) != 9 ||
        await verify.DeliveryTripCommandReceipts.CountAsync() != beforeTripReceipts + 9 || await verify.DeliveryTripRevisions.CountAsync() != beforeTripHistory + 9 || await verify.DeliveryTripOutboxMessages.CountAsync() != beforeTripOutbox + 9)
        throw new InvalidOperationException("D06 root receipts/history/outbox do not match exact commands/replay.");
    var journals = await verify.DeliveryJournalEntries.AsNoTracking().Where(x => primaryIds.Contains(x.DeliveryOrderId)).ToArrayAsync();
    if (journals.Length != 2 || journals.Any(x => x.Kind != GaoApp.Domain.Delivery.DeliveryJournalKind.Dispatch) ||
        journals.Single(x => x.DeliveryOrderId == primaryIds[0]).BaseQuantity != 2 || journals.Single(x => x.DeliveryOrderId == primaryIds[0]).CostAmount != 18000 ||
        journals.Single(x => x.DeliveryOrderId == primaryIds[1]).BaseQuantity != 3 || journals.Single(x => x.DeliveryOrderId == primaryIds[1]).CostAmount != 33000 ||
        await verify.InventoryTransactions.CountAsync() != beforeMoves + 2 || await verify.OrderPayments.CountAsync() != beforePayments || await verify.Orders.CountAsync(x => x.Status == OrderStatus.Completed) != beforeCompleted ||
        await verify.DeliveryCommandReceipts.CountAsync(x => primaryIds.Contains(x.DeliveryOrderId)) != beforeOrderReceipts + 2 ||
        await verify.DeliveryRevisions.CountAsync(x => primaryIds.Contains(x.DeliveryOrderId)) != beforeOrderHistory + 9 || await verify.DeliveryOutboxMessages.CountAsync(x => primaryIds.Contains(x.DeliveryOrderId)) != beforeOrderOutbox + 9)
        throw new InvalidOperationException("D06 handover must export each source once and all later commands preserve stock/finance.");
    var balances = await verify.InventoryBalances.AsNoTracking().ToArrayAsync();
    if (balances.Length != beforeBalances.Count || balances.Any(x => !beforeBalances.ContainsKey(x.Id)) ||
        balances.Sum(x => x.OnHandQty - beforeBalances[x.Id].Quantity) != -5 || balances.Sum(x => x.InventoryValue - beforeBalances[x.Id].Value) != -51000)
        throw new InvalidOperationException("D06 exact source balance quantities/values differ.");
    var mainTrip = await verify.DeliveryTrips.AsNoTracking().SingleAsync(x => x.Id == mainTripId);
    var active = await verify.DeliveryTripOrders.AsNoTracking().Where(x => x.TripId == mainTripId && x.IsActive).ToArrayAsync();
    var currentEventIds = active.Where(x => x.CurrentCustodyEventId.HasValue).Select(x => x.CurrentCustodyEventId!.Value).ToArray();
    if (mainTrip.State != GaoApp.Domain.Delivery.DeliveryTripState.Departed || mainTrip.DepartedAtUtc is null || active.Length != 2 ||
        await verify.DeliveryCustodyEvents.CountAsync(x => x.TripId == mainTripId) != 4 || await verify.DeliveryCustodyLines.CountAsync(x => currentEventIds.Contains(x.CustodyEventId)) != 2 ||
        await verify.DeliveryJournalEntries.AnyAsync(x => x.DeliveryOrderId == spareId) ||
        (await verify.DeliveryOrders.SingleAsync(x => x.Id == primaryIds[0])).State != GaoApp.Domain.Delivery.DeliveryState.CancellationPending ||
        (await verify.DeliveryOrders.SingleAsync(x => x.Id == primaryIds[1])).State != GaoApp.Domain.Delivery.DeliveryState.Delivering)
        throw new InvalidOperationException("D06 departure/one-order recall must preserve genuine courier chains and the other order.");
    await File.WriteAllTextAsync(Path.Combine(evidence, "sql-effects.json"), JsonSerializer.Serialize(new { committedRootCommands = 9, stockChildReceipts = 2, tripHistory = 9, orderHistory = 9,
        dispatchJournals = journals, balanceDeltaQuantity = -5, balanceDeltaValue = -51000, mainTripId, currentCourier = mainTrip.CourierUserId,
        physicalCustodyEvents = 4, currentCustodyLines = 2, noReturnOrFinance = true, fixtureOnly = true }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("D06 TRIP BROWSER 6 GROUPS + EXACT SQL EFFECTS PASS — disposable fixture only."); return;
}
if (args.Contains("--delivery-inventory"))
{
    var inventoryFixtureType = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Delivery.DeliveryD05BrowserFixture")
        ?? throw new InvalidOperationException("D05 browser fixture seam is missing.");
    var inventorySeed = await Call(null, inventoryFixtureType, "PrepareAsync", fixture);
    var inventoryStoreId = (int)Read(inventorySeed, "StoreId");
    var inventoryDatabase = Read(fixture, "Database");
    AppDbContext InventoryContext() => (AppDbContext)inventoryDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(inventoryDatabase, new object?[] { inventoryStoreId, null })!;
    object InventoryOrder(object value) => new { id = Read(value, "Id"), lookupToken = Read(value, "LookupToken") };
    object InventoryAccount(object value) => new { name = Read(value, "Name"), password = Read(value, "Password"), userId = Read(value, "UserId") };
    var inventoryIds = new[] { (int)Read(Read(inventorySeed, "Created"), "Id"), (int)Read(Read(inventorySeed, "Held"), "Id"), (int)Read(Read(inventorySeed, "Posted"), "Id") };
    int initialReceipts, initialRevisions, initialOutbox, initialTransactions, initialJournals, initialPayments, initialCompleted;
    Dictionary<int, decimal> initialOnHand;
    await using (var before = InventoryContext())
    {
        initialReceipts = await before.DeliveryCommandReceipts.CountAsync(x => inventoryIds.Contains(x.DeliveryOrderId));
        initialRevisions = await before.DeliveryRevisions.CountAsync(x => inventoryIds.Contains(x.DeliveryOrderId));
        initialOutbox = await before.DeliveryOutboxMessages.CountAsync(x => inventoryIds.Contains(x.DeliveryOrderId));
        initialTransactions = await before.InventoryTransactions.CountAsync(); initialJournals = await before.DeliveryJournalEntries.CountAsync();
        initialPayments = await before.OrderPayments.CountAsync(); initialCompleted = await before.Orders.CountAsync(x => x.Status == OrderStatus.Completed);
        initialOnHand = await before.InventoryBalances.ToDictionaryAsync(x => x.Id, x => x.OnHandQty);
    }
    var inventorySource = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var inventoryEvidence = Environment.GetEnvironmentVariable("GAO_DELIVERY_INVENTORY_TEST_OUTPUT") ?? Path.Combine(inventorySource, "TestResults", "delivery-inventory");
    Directory.CreateDirectory(inventoryEvidence);
    var inventoryStart = new ProcessStartInfo("node") { WorkingDirectory = inventorySource, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    inventoryStart.Environment["NODE_PATH"] = Path.Combine(inventorySource, "Logs", "pos-offline-browser-deps", "node_modules");
    inventoryStart.ArgumentList.Add(Path.Combine(inventorySource, "GaoApp.Tests.Browser", "delivery-inventory.browser.cjs"));
    using var inventoryProbe = Process.Start(inventoryStart)!;
    await inventoryProbe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}", terminalId = Read(inventorySeed, "TerminalId"),
        created = InventoryOrder(Read(inventorySeed, "Created")), held = InventoryOrder(Read(inventorySeed, "Held")), posted = InventoryOrder(Read(inventorySeed, "Posted")),
        creator = InventoryAccount(Read(inventorySeed, "Creator")), viewer = InventoryAccount(Read(inventorySeed, "Viewer")),
        costViewer = InventoryAccount(Read(inventorySeed, "CostViewer")), cancel = InventoryAccount(Read(inventorySeed, "Cancel")),
        storeId = inventoryStoreId, sourceWarehouseId = Read(inventorySeed, "SourceWarehouseId"), sourceWarehouseName = Read(inventorySeed, "SourceWarehouseName"),
        sourceLegalEntityId = Read(inventorySeed, "SourceLegalEntityId"), sourceLegalEntityName = Read(inventorySeed, "SourceLegalEntityName"),
        expected = Read(inventorySeed, "Expected"), evidenceDirectory = inventoryEvidence
    }));
    inventoryProbe.StandardInput.Close(); using var inventoryTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
    try { await inventoryProbe.WaitForExitAsync(inventoryTimeout.Token); }
    catch { if (!inventoryProbe.HasExited) inventoryProbe.Kill(true); throw; }
    if (inventoryProbe.ExitCode != 0) throw new InvalidOperationException("D05 inventory browser failed.");
    using var inventoryResult = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(inventoryEvidence, "browser-checks.json")));
    if (inventoryResult.RootElement.GetProperty("checks").GetArrayLength() != 6) throw new InvalidOperationException("D05 browser groups are incomplete.");
    var inventoryCommandIds = inventoryResult.RootElement.GetProperty("commandIds").EnumerateArray().Select(x => Guid.Parse(x.GetString()!)).Distinct().ToArray();
    await using var inventoryVerify = InventoryContext();
    if (inventoryCommandIds.Length != 4 || await inventoryVerify.DeliveryCommandReceipts.CountAsync(x => inventoryCommandIds.Contains(x.ClientRequestId)) != inventoryCommandIds.Length ||
        await inventoryVerify.DeliveryCommandReceipts.CountAsync(x => inventoryIds.Contains(x.DeliveryOrderId)) != initialReceipts + inventoryCommandIds.Length ||
        await inventoryVerify.DeliveryRevisions.CountAsync(x => inventoryIds.Contains(x.DeliveryOrderId)) != initialRevisions + inventoryCommandIds.Length ||
        await inventoryVerify.DeliveryOutboxMessages.CountAsync(x => inventoryIds.Contains(x.DeliveryOrderId)) != initialOutbox + inventoryCommandIds.Length)
        throw new InvalidOperationException("D05 browser distinct commands/replay did not match receipts/history/outbox.");
    var finalOnHand = await inventoryVerify.InventoryBalances.ToDictionaryAsync(x => x.Id, x => x.OnHandQty);
    if (initialOnHand.Count != finalOnHand.Count || initialOnHand.Any(x => !finalOnHand.TryGetValue(x.Key, out var quantity) || quantity != x.Value) ||
        await inventoryVerify.InventoryTransactions.CountAsync() != initialTransactions || await inventoryVerify.DeliveryJournalEntries.CountAsync() != initialJournals ||
        await inventoryVerify.OrderPayments.CountAsync() != initialPayments || await inventoryVerify.Orders.CountAsync(x => x.Status == OrderStatus.Completed) != initialCompleted)
        throw new InvalidOperationException("D05 report/cancel browser changed physical stock or finance.");
    var finalInventoryFacts = new List<JsonElement>();
    foreach (var id in inventoryIds)
    {
        using var response = await http.GetAsync($"/admin/api/deliveries/{id}/inventory"); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var fact = document.RootElement;
        if (fact.GetProperty("storeId").GetInt32() != inventoryStoreId || fact.GetProperty("sourceWarehouseId").GetInt32() != (int)Read(inventorySeed, "SourceWarehouseId") ||
            fact.GetProperty("sourceLegalEntityId").GetInt32() != (int)Read(inventorySeed, "SourceLegalEntityId") || fact.GetProperty("inCustodyBaseQuantityText").ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("D05 browser source/custody projection changed.");
        finalInventoryFacts.Add(fact.Clone());
    }
    await File.WriteAllTextAsync(Path.Combine(inventoryEvidence, "sql-effects.json"), JsonSerializer.Serialize(new {
        distinctCommittedCommands = inventoryCommandIds.Length, initialReceipts, initialRevisions, initialOutbox,
        physicalStockUnchangedByUi = true, stockTransactionsUnchanged = initialTransactions, stockJournalsUnchanged = initialJournals,
        paymentsUnchanged = initialPayments, completedSalesUnchanged = initialCompleted, finalInventoryFacts,
        seedUsesInternalStockExecutorOnly = true, noCourierOrOutcomeFixture = true, fixtureOnly = true
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("D05 INVENTORY BROWSER 6 GROUPS + EXACT SQL EFFECTS PASS — disposable fixture only."); return;
}
if (args.Contains("--delivery-picking"))
{
    var pickingDatabase = Read(fixture, "Database");
    var pickingStoreId = (int)Read(store, "StoreId");
    var pickingWarehouseId = (int)Read(store, "WarehouseId");
    AppDbContext PickingContext() => (AppDbContext)pickingDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(pickingDatabase, new object?[] { pickingStoreId, null })!;
    var picker = await Call(fixture, type, "AddAccountAsync", store, new[] { PermissionCodes.Delivery.View, PermissionCodes.Delivery.Pick });
    int originalVariantId = (int)Read(store, "VariantId"), replacementVariantId, missingVariantId, exactVariantId;
    await using (var seed = PickingContext())
    {
        var original = await seed.ProductVariants.SingleAsync(x => x.Id == originalVariantId);
        original.Price = 80; original.ProductVariantName = "Gạo gốc <script>alert(1)</script>";
        var replacement = new ProductVariant { StoreId = pickingStoreId, ProductId = original.ProductId,
            Sku = "D04-REPLACEMENT", ProductVariantName = "Gạo thay thế", Price = 120, IsActive = true };
        var missing = new ProductVariant { StoreId = pickingStoreId, ProductId = original.ProductId,
            Sku = "D04-MISSING", ProductVariantName = "Hàng hết", Price = 30, IsActive = true };
        var exactVariant = new ProductVariant { StoreId = pickingStoreId, ProductId = original.ProductId,
            Sku = "D04-EXACT", ProductVariantName = "Hàng kiểm tra độ chính xác", Price = .01m, IsActive = true };
        seed.AddRange(replacement, missing, exactVariant); await seed.SaveChangesAsync();
        replacementVariantId = replacement.Id; missingVariantId = missing.Id; exactVariantId = exactVariant.Id;
        var baseUnitId = await seed.Products.Where(x => x.Id == original.ProductId)
            .Select(x => x.BaseUnitId).SingleAsync();
        seed.Add(new ProductUnitConversion { StoreId = pickingStoreId, ProductVariantId = exactVariantId,
            UnitId = baseUnitId, Factor = 1, IsBaseUnit = true });
        await seed.SaveChangesAsync();
    }
    async Task<JsonElement> PickingPost(string path, object? body = null)
    {
        using var response = body is null ? await http.PostAsync(path, null) : await http.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(path + ": " + (int)response.StatusCode + " " + text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    (await http.PostAsJsonAsync("/admin/pos/shift/open", new { openingCash = 0, warehouseId = Read(store, "WarehouseId") })).EnsureSuccessStatusCode();
    async Task<int> PickingDraft(int variantId, string quantity)
    {
        var draft = await PickingPost("/admin/pos/draft"); var id = draft.GetProperty("orderId").GetInt32();
        await PickingPost($"/admin/pos/{id}/items?variantId={variantId}&qty={quantity}"); return id;
    }
    async Task<JsonElement> PickingTransfer()
    {
        var snapshot = await http.GetFromJsonAsync<JsonElement>("/admin/api/deliveries/current-cart");
        return await PickingPost("/admin/api/deliveries/from-current-cart", new {
            clientRequestId = Guid.NewGuid(), sourceCartId = snapshot.GetProperty("sourceCartId").GetInt32(),
            expectedVersion = snapshot.GetProperty("version").GetString(), expectedFingerprint = snapshot.GetProperty("fingerprint").GetString(),
            recipientName = "Khách D04 <img src=x onerror=alert(1)>", recipientPhone = "0901234567",
            recipientAddress = "Địa chỉ thử nghiệm", note = "Chỉ dữ liệu fixture"
        });
    }
    var mainCart = await PickingDraft(originalVariantId, "10");
    await PickingPost($"/admin/pos/{mainCart}/items?variantId={missingVariantId}&qty=1");
    var main = (await PickingTransfer()).GetProperty("delivery").Clone();
    // The maximum legal quantity needs its own SKU: other pending holds on the
    // ordinary SKU would correctly overflow the aggregate inventory quantity limit.
    var exactCart = await PickingDraft(exactVariantId, "1");
    await using (var seed = PickingContext())
    {
        var cart = await seed.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == exactCart);
        var line = cart.Lines.Single(); line.Quantity = 99999999999999.9999m; line.BaseQuantity = line.Quantity;
        line.Multiplier = 1; line.UnitPrice = .01m; line.OriginalUnitPrice = .01m;
        line.LineTotal = GaoApp.Domain.Delivery.DeliveryValues.PricedAmount(line.Quantity, line.UnitPrice);
        cart.Subtotal = line.LineTotal; cart.GrandTotal = line.LineTotal; cart.BalanceDue = line.LineTotal;
        await seed.SaveChangesAsync();
    }
    var exact = (await PickingTransfer()).GetProperty("delivery").Clone();
    var handedCart = await PickingDraft(originalVariantId, "1");
    var handed = (await PickingTransfer()).GetProperty("delivery").Clone();
    await using (var seed = PickingContext())
    {
        var delivery = await seed.DeliveryOrders.SingleAsync(x => x.Id == handed.GetProperty("id").GetInt32());
        // Fixture-only downstream state to prove D04 guards; no operational handover or inventory executor is invoked.
        delivery.State = GaoApp.Domain.Delivery.DeliveryState.HandedOver; await seed.SaveChangesAsync();
    }
    var legacyCartId = await PickingDraft(originalVariantId, "1");
    int legacyId;
    await using (var seed = PickingContext())
    {
        var source = await seed.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == legacyCartId);
        var sourceLine = source.Lines.Single();
        var owner = await seed.Warehouses.Where(x => x.Id == pickingWarehouseId).Select(x => x.LegalEntityId).SingleAsync();
        var legacy = new GaoApp.Domain.Delivery.DeliveryOrder {
            StoreId = pickingStoreId, Code = "GH-D04-LEGACY", LookupToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            SourceCartId = source.Id, SourceWarehouseId = (int)Read(store, "WarehouseId"), SourceLegalEntityId = owner,
            CreatedTerminalId = (int)Read(store, "TerminalId"), CreatedShiftId = source.POSShiftId, CreatedByUserId = (int)Read(account, "UserId"),
            RecipientName = "Hồ sơ Draft lịch sử", RecipientPhone = "0901234567", RecipientAddress = "Fixture", QuotedTotal = source.GrandTotal
        };
        legacy.Lines.Add(new GaoApp.Domain.Delivery.DeliveryOrderLine {
            StoreId = pickingStoreId, SourceCartId = source.Id, SourceOrderLineId = sourceLine.Id, VariantId = sourceLine.VariantId,
            ItemName = sourceLine.ItemName, UnitName = sourceLine.SellingUnitName ?? sourceLine.UnitName ?? "", BaseUnitName = sourceLine.BaseUnitName ?? "",
            OrderedQuantity = sourceLine.Quantity, BaseMultiplier = sourceLine.Multiplier, UnitPrice = sourceLine.UnitPrice,
            Gross = sourceLine.LineTotal, Net = sourceLine.LineTotal
        });
        seed.Add(legacy); await seed.SaveChangesAsync(); legacyId = legacy.Id;
    }
    Dictionary<(int WarehouseId, int VariantId), decimal> pickingInitialPhysicalStock;
    await using (var baseline = PickingContext())
        pickingInitialPhysicalStock = (await baseline.InventoryBalances.AsNoTracking().ToListAsync())
            .ToDictionary(x => (x.WarehouseId, x.ProductVariantId), x => x.OnHandQty);
    var pickingSource = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var pickingEvidence = Environment.GetEnvironmentVariable("GAO_DELIVERY_PICKING_TEST_OUTPUT") ??
        Path.Combine(pickingSource, "TestResults", "delivery-picking");
    Directory.CreateDirectory(pickingEvidence);
    var pickingStart = new ProcessStartInfo("node") { WorkingDirectory = pickingSource, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    pickingStart.Environment["NODE_PATH"] = Path.Combine(pickingSource, "Logs", "pos-offline-browser-deps", "node_modules");
    pickingStart.ArgumentList.Add(Path.Combine(pickingSource, "GaoApp.Tests.Browser", "delivery-picking.browser.cjs"));
    using var pickingProbe = Process.Start(pickingStart)!;
    await pickingProbe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), actorId = Read(account, "UserId"),
        pickerUser = Read(picker, "Name"), pickerPassword = Read(picker, "Password"), pickerId = Read(picker, "UserId"),
        terminalId = Read(store, "TerminalId"), main, exact, handed, legacyId, replacementVariantId,
        evidenceDirectory = pickingEvidence
    }));
    pickingProbe.StandardInput.Close(); using var pickingTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
    try { await pickingProbe.WaitForExitAsync(pickingTimeout.Token); } catch { if (!pickingProbe.HasExited) pickingProbe.Kill(true); throw; }
    if (pickingProbe.ExitCode != 0) throw new InvalidOperationException("D04 picking browser failed.");
    var probeResult = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(pickingEvidence, "browser-checks.json")));
    var ackIds = probeResult.RootElement.GetProperty("commandIds").EnumerateArray().Select(x => Guid.Parse(x.GetString()!)).Distinct().ToArray();
    await using var verify = PickingContext();
    var operationalIds = new[] { main.GetProperty("id").GetInt32(), exact.GetProperty("id").GetInt32(), handed.GetProperty("id").GetInt32() };
    var orders = await verify.DeliveryOrders.Where(x => operationalIds.Contains(x.Id)).ToListAsync();
    var expectedEvents = orders.Sum(x => x.Revision);
    if (await verify.DeliveryRevisions.CountAsync(x => operationalIds.Contains(x.DeliveryOrderId)) != expectedEvents ||
        await verify.DeliveryOutboxMessages.CountAsync(x => operationalIds.Contains(x.DeliveryOrderId)) != expectedEvents ||
        await verify.DeliveryCommandReceipts.CountAsync(x => operationalIds.Contains(x.DeliveryOrderId)) != 3 + ackIds.Length ||
        await verify.DeliveryCommandReceipts.CountAsync(x => ackIds.Contains(x.ClientRequestId)) != ackIds.Length)
        throw new InvalidOperationException("D04 revisions/receipts/outbox did not match actual distinct committed intents.");
    var holds = await verify.InventoryReservations.AsNoTracking().Where(x => x.Status == InventoryReservationStatus.Active).ToListAsync();
    var pickedLines = await verify.DeliveryPickingLines.AsNoTracking().Include(x => x.Quote)
        .Where(x => operationalIds.Contains(x.DeliveryOrderId)).ToListAsync();
    var expectedHeldLines = pickedLines.Where(x => x.IsActive && x.ReportedQuantity is > 0).ToArray();
    if (holds.Count != expectedHeldLines.Length || holds.Any(x => !operationalIds.Contains(x.DeliveryOrderId ?? 0)))
        throw new InvalidOperationException("D04 active planned holds do not match reported picking lines.");
    foreach (var line in expectedHeldLines)
    {
        var hold = holds.SingleOrDefault(x => x.DeliveryOrderId == line.DeliveryOrderId && x.DeliveryOrderLineId == line.DeliveryOrderLineId);
        var owner = orders.Single(x => x.Id == line.DeliveryOrderId);
        if (hold is null || hold.ProductVariantId != line.Quote.VariantId || hold.WarehouseId != owner.SourceWarehouseId ||
            hold.SourceLegalEntityId != owner.SourceLegalEntityId || hold.ReservedQty != line.ReportedQuantity!.Value * line.Quote.BaseMultiplier ||
            hold.ReportedByUserId != line.ReporterUserId || hold.ReportedAtUtc != line.ReportedAtUtc)
            throw new InvalidOperationException("D04 planned hold quantity, source or reporter differs from saved picking evidence.");
    }
    var balances = await verify.InventoryBalances.AsNoTracking().ToListAsync();
    if (balances.Any(x => x.OnHandQty != pickingInitialPhysicalStock.GetValueOrDefault((x.WarehouseId, x.ProductVariantId)) ||
        x.ReservedQty != holds.Where(h => h.WarehouseId == x.WarehouseId && h.ProductVariantId == x.ProductVariantId).Sum(h => h.ReservedQty)) ||
        pickingInitialPhysicalStock.Keys.Any(key => !balances.Any(x => x.WarehouseId == key.WarehouseId && x.ProductVariantId == key.VariantId)) ||
        await verify.InventoryTransactions.CountAsync() != 1 ||
        await verify.DeliveryJournalEntries.AnyAsync() || await verify.DeliveryDispatchCostFragments.AnyAsync() ||
        await verify.OrderPayments.AnyAsync() || await verify.Orders.AnyAsync(x => x.Status == OrderStatus.Completed))
        throw new InvalidOperationException("D04 browser changed stock or finance.");
    await File.WriteAllTextAsync(Path.Combine(pickingEvidence, "sql-effects.json"), JsonSerializer.Serialize(new {
        distinctPickingIntents = ackIds.Length, expectedEvents, revisions = expectedEvents, receipts = 3 + ackIds.Length,
        openingStockOnly = true, physicalStockUnchanged = true, plannedHoldsMatchReportedEvidence = true,
        activePlannedHolds = holds.Count, noPayments = true, noCompletedSale = true, fixtureOnly = true
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("D04 PICKING BROWSER + EXACT SQL EFFECTS PASS — disposable fixture only."); return;
}
if (args.Contains("--delivery"))
{
    var deliveryDatabase = Read(fixture, "Database");
    var deliveryVariantIds = new List<int>();
    await using (var seedDb = (AppDbContext)deliveryDatabase.GetType().GetMethod("CreateTenantContext", flags)!.Invoke(deliveryDatabase, new object?[]{Read(store,"StoreId"),null})!)
    {
        var templateId=(int)Read(store,"VariantId");
        var template = await seedDb.ProductVariants.SingleAsync(x=>x.Id==templateId);
        for(var i=1;i<=28;i++) {
            var variant = new ProductVariant { StoreId=(int)Read(store,"StoreId"), ProductId=template.ProductId, Sku="D03-LONG-"+i, ProductVariantName="Hàng thử A5 số "+i+" — mô tả dài để kiểm tra xuống dòng", Price=20, CostPrice=10, IsActive=true };
            seedDb.Add(variant); await seedDb.SaveChangesAsync(); deliveryVariantIds.Add(variant.Id);
            seedDb.InventoryBalances.Add(new(){StoreId=variant.StoreId,WarehouseId=(int)Read(store,"WarehouseId"),ProductVariantId=variant.Id,OnHandQty=100});
        }
        await seedDb.SaveChangesAsync();
    }
    var deliveryRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var deliveryStart = new ProcessStartInfo("node") { WorkingDirectory = deliveryRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    deliveryStart.Environment["NODE_PATH"] = Path.Combine(deliveryRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    deliveryStart.ArgumentList.Add(Path.Combine(deliveryRoot, "GaoApp.Tests.Browser", "delivery.browser.cjs"));
    using var probe = Process.Start(deliveryStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"),
        terminalId = Read(store, "TerminalId"), warehouseId = Read(store, "WarehouseId"), variantId = Read(store, "VariantId"),
        longVariantIds = deliveryVariantIds,
        evidenceDirectory = Environment.GetEnvironmentVariable("GAO_DELIVERY_TEST_OUTPUT")
    }));
    probe.StandardInput.Close(); using var deliveryTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try { await probe.WaitForExitAsync(deliveryTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Delivery browser probe failed.");
    await using var deliveryDb = (AppDbContext)deliveryDatabase.GetType().GetMethod("CreateTenantContext", flags)!.Invoke(deliveryDatabase, new object?[]{Read(store,"StoreId"),null})!;
    if (await deliveryDb.DeliveryOrders.CountAsync() != 2 || await deliveryDb.DeliveryRevisions.CountAsync() != 2 ||
        await deliveryDb.POSAuditLogs.CountAsync(x=>x.Action=="DELIVERY_CREATED") != 2 || await deliveryDb.OrderPayments.AnyAsync() ||
        await deliveryDb.Orders.CountAsync(x=>x.Status==OrderStatus.Completed) != 0 || await deliveryDb.InventoryTransactions.CountAsync()!=1)
        throw new Exception("Delivery browser SQL effects were not exactly two transfers and unchanged opening stock.");
    Console.WriteLine("DELIVERY BROWSER + SQL EFFECTS PASS — disposable fixture only."); return;
}
if (args.Contains("--store-monitor"))
{
    var monitorTerminalIds = new List<int> { (int)Read(store, "TerminalId") };
    var monitorDatabase = Read(fixture, "Database");
    var monitorStaff = new List<object> { account };
    for (var staffIndex = 0; staffIndex < 2; staffIndex++)
        monitorStaff.Add(await Call(fixture, type, "AddAccountAsync", store, new[] { "*" }));
    int monitorLegalEntityId; var monitorLabelTaskIds = new List<int>();
    var monitorWarehouseId = (int)Read(store, "WarehouseId");
    await using (var monitorDb = (AppDbContext)monitorDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(monitorDatabase, new object?[] { Read(store, "StoreId"), null })!)
    {
        monitorLegalEntityId = await monitorDb.Warehouses.Where(x => x.Id == monitorWarehouseId).Select(x => x.LegalEntityId).SingleAsync();
        for (var staffIndex = 0; staffIndex < monitorStaff.Count; staffIndex++)
        {
            var staffId = (int)Read(monitorStaff[staffIndex], "UserId");
            (await monitorDb.Users.SingleAsync(x => x.Id == staffId)).FullName = new[] { "Lan Anh", "Hoàng Nam", "Mai Hương" }[staffIndex];
        }
        for (var labelIndex = 1; labelIndex <= 2; labelIndex++)
        {
            var receipt = new StockDocument { StoreId=(int)Read(store, "StoreId"), WarehouseId=(int)Read(store, "WarehouseId"), DocumentNo=$"MONITOR-LABEL-{labelIndex}" };
            monitorDb.StockDocuments.Add(receipt); await monitorDb.SaveChangesAsync();
            var labelTask = new ProductLabelTask { StoreId=receipt.StoreId, StockDocumentId=receipt.Id, DocumentNo=receipt.DocumentNo };
            monitorDb.Add(labelTask); await monitorDb.SaveChangesAsync(); monitorLabelTaskIds.Add(labelTask.Id);
        }
        for (var counter = 2; counter <= 3; counter++)
        {
            var terminal = new POSTerminal { StoreId = (int)Read(store, "StoreId"), Code = $"E2E-0{counter}", Name = $"Quầy 0{counter}" };
            monitorDb.POSTerminals.Add(terminal); await monitorDb.SaveChangesAsync(); monitorTerminalIds.Add(terminal.Id);
        }
    }
    var monitorAccount = await Call(fixture, type, "AddAccountAsync", store, new[] { PermissionCodes.Admin.StoreMonitorView });
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var monitorStart = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    monitorStart.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    monitorStart.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "store-monitor.browser.cjs"));
    using var probe = Process.Start(monitorStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
    {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"),
        monitorUser = Read(monitorAccount, "Name"), monitorPassword = Read(monitorAccount, "Password"),
        terminalId = Read(store, "TerminalId"), terminalIds = monitorTerminalIds,
        warehouseId = Read(store, "WarehouseId"), variantId = Read(store, "VariantId"),
        legalEntityId = monitorLegalEntityId, labelTaskIds = monitorLabelTaskIds,
        staffAccounts = monitorStaff.Select(staff => new { user=Read(staff,"Name"), password=Read(staff,"Password") }),
        evidenceDirectory = Environment.GetEnvironmentVariable("GAO_MONITOR_TEST_OUTPUT")
    }));
    probe.StandardInput.Close(); using var monitorTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try { await probe.WaitForExitAsync(monitorTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Store monitor browser failed.");
    Console.WriteLine("STORE MONITOR BROWSER PASS — three counters on one account, real operations and separate UI fixtures, disposable SQL only."); return;
}
if (args.Contains("--operations-reports"))
{
    var seed = await Call(null, typeof(OperationsReportSqlServerTests), "SeedBrowserAsync", fixture, store, client);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var operationsStart = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    operationsStart.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    operationsStart.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "operations-reports.browser.cjs"));
    using var probe = Process.Start(operationsStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{Read(store,"Host")}:{((Uri)Read(fixture,"Address")).Port}", user = Read(account,"Name"), password = Read(account,"Password"), terminalId = Read(store,"TerminalId"), day = Read(seed,"Day") }));
    probe.StandardInput.Close(); using var operationsTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await probe.WaitForExitAsync(operationsTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Operations reports browser failed.");
    Console.WriteLine("OPERATIONS REPORT BROWSER PASS — disposable SQL only."); return;
}
if (args.Contains("--management-reports"))
{
    var reportDatabase=Read(fixture,"Database"); var storeId=(int)Read(store,"StoreId");
    int wholesaleId,retailId;
    await using(var reportDb=(AppDbContext)reportDatabase.GetType().GetMethod("CreateTenantContext",flags)!.Invoke(reportDatabase,new object?[]{storeId,null})!)
    {
        var wholesale=new Customer{StoreId=storeId,Name="Đại lý Minh An",PriceTier="WHOLESALE"};
        var retail=new Customer{StoreId=storeId,Name="Nguyễn Thị Lan",PriceTier="RETAIL"};
        reportDb.Customers.AddRange(wholesale,retail);await reportDb.SaveChangesAsync();wholesaleId=wholesale.Id;retailId=retail.Id;
        var product=await reportDb.Products.SingleAsync();product.Name="Gạo ST25 · túi 5 kg";await reportDb.SaveChangesAsync();
    }
    (await http.PostAsJsonAsync("/admin/pos/shift/open",new{openingCash=0,warehouseId=Read(store,"WarehouseId")})).EnsureSuccessStatusCode();
    for(var i=0;i<8;i++)
    {
        var customer=i%3==0?wholesaleId:retailId;
        var draft=await (await http.PostAsync("/admin/pos/draft?customerId="+customer,null)).Content.ReadFromJsonAsync<JsonElement>();var id=draft.GetProperty("orderId").GetInt32();
        var qty=i+1;
        (await http.PostAsync($"/admin/pos/{id}/items?variantId={Read(store,"VariantId")}&qty={qty}",null)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync($"/admin/pos/{id}/payments",new{clientRequestId=Guid.NewGuid(),amount=qty*20,method=0})).EnsureSuccessStatusCode();
        using var finalize=await http.PostAsync($"/admin/pos/{id}/finalize",null);if(!finalize.IsSuccessStatusCode)throw new Exception(await finalize.Content.ReadAsStringAsync());
    }
    var day=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")).Date;
    foreach(var expense in new[]{new{Name="Mặt bằng tháng này",Category="rent",Amount=100m},new{Name="Giao hàng",Category="transport",Amount=45m},new{Name="Điện & nước",Category="utilities",Amount=35m}})
    {
        using var created=await http.PostAsJsonAsync("/admin/reports/expenses",new{clientRequestId=Guid.NewGuid(),expense.Name,expense.Category,expense.Amount,recognitionFrom=day,recognitionTo=day,isPaid=true});created.EnsureSuccessStatusCode();
        var row=await created.Content.ReadFromJsonAsync<JsonElement>();(await http.PostAsJsonAsync($"/admin/reports/expenses/{row.GetProperty("id").GetInt32()}/confirm",new{rowVersion=row.GetProperty("rowVersion").GetString()})).EnsureSuccessStatusCode();
    }
    var source=(string)type.GetMethod("SourceRoot",flags)!.Invoke(null,null)!;
    var reportStart=new ProcessStartInfo("node"){WorkingDirectory=source,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true};
    reportStart.Environment["NODE_PATH"]=Path.Combine(source,"Logs","pos-offline-browser-deps","node_modules");
    reportStart.ArgumentList.Add(Path.Combine(source,"GaoApp.Tests.Browser","management-reports.browser.cjs"));
    using var probe=Process.Start(reportStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{baseUrl=$"http://{Read(store,"Host")}:{((Uri)Read(fixture,"Address")).Port}",user=Read(account,"Name"),password=Read(account,"Password"),terminalId=Read(store,"TerminalId"),day=day.ToString("yyyy-MM-dd")}));
    probe.StandardInput.Close();using var reportTimeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try{await probe.WaitForExitAsync(reportTimeout.Token);}catch{if(!probe.HasExited)probe.Kill(true);throw;}
    if(probe.ExitCode!=0)throw new Exception("Management report browser failed.");
    Console.WriteLine("MANAGEMENT REPORT BROWSER PASS — disposable SQL only.");return;
}
if (args.Contains("--input-invoice-library"))
{
    var seedTask = (Task)typeof(InputInvoiceCatalogSqlServerTests).GetMethod("SeedAsync", flags)!.Invoke(null, new[] { fixture, store, (object)8 })!;
    await seedTask;
    var sourceMismatchPath = Path.Combine((string)Read(fixture, "InvoiceLibraryRoot"), "2026", "0312770607", "10", "invoice-8.xml");
    await File.WriteAllTextAsync(sourceMismatchPath, (await File.ReadAllTextAsync(sourceMismatchPath)).Replace("0312770607", "0100520429"));
    using var sync = await http.PostAsync("/admin/input-invoices/sync?year=2026&month=10", null);
    if (!sync.IsSuccessStatusCode) throw new Exception(await sync.Content.ReadAsStringAsync());
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var catalogStart = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    catalogStart.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    catalogStart.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "input-invoice-library.browser.cjs"));
    using var probe = Process.Start(catalogStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{Read(store,"Host")}:{((Uri)Read(fixture,"Address")).Port}", user = Read(account,"Name"), password = Read(account,"Password"), terminalId = Read(store,"TerminalId") }));
    probe.StandardInput.Close(); using var catalogTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try { await probe.WaitForExitAsync(catalogTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Input invoice library browser failed.");
    Console.WriteLine("INPUT INVOICE LIBRARY BROWSER PASS — disposable SQL only."); return;
}
if (args.Contains("--menu-visibility"))
{
    var menuDatabase = Read(fixture, "Database");
    int menuId, employeeId;
    await using (var menuDb = (AppDbContext)menuDatabase.GetType().GetMethod("CreateTenantContext", flags)!.Invoke(menuDatabase, new object?[] { Read(store, "StoreId"), null })!)
    {
        var roleId = (int)Read(account, "RoleId");
        var role = await menuDb.Roles.SingleAsync(x => x.Id == roleId); role.Name = "Nhóm quản lý cửa hàng";
        var menuUserId = (int)Read(account, "UserId");
        var member = await menuDb.UserInStores.Include(x => x.User).SingleAsync(x => x.UserId == menuUserId);
        member.User.FullName = "Nguyễn Minh Anh"; employeeId = member.Id;
        var folder = new AdminMenuItem { StoreId = role.StoreId, Title = "Danh mục sản phẩm", Icon = "bx bx-package", SortOrder = 1 };
        menuDb.AdminMenuItems.Add(folder); await menuDb.SaveChangesAsync();
        var menu = new AdminMenuItem { StoreId = role.StoreId, ParentId = folder.Id, Title = "Sản phẩm", Controller = "Product", PermissionCode = PermissionCodes.Catalog.Product.View, Icon = "bx bx-purchase-tag" };
        menuDb.AdminMenuItems.AddRange(menu,
            new AdminMenuItem { StoreId = role.StoreId, ParentId = folder.Id, Title = "Nhà cung cấp", Controller = "Supplier", Icon = "bx bx-store" },
            new AdminMenuItem { StoreId = role.StoreId, ParentId = folder.Id, Title = "Đơn vị tính", Controller = "Unit", Icon = "bx bx-cube" });
        await menuDb.SaveChangesAsync(); menuId = menu.Id;
    }
    var probeRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var menuStart = new ProcessStartInfo("node") { WorkingDirectory = probeRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    menuStart.Environment["NODE_PATH"] = Path.Combine(probeRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    menuStart.ArgumentList.Add(Path.Combine(probeRoot, "GaoApp.Tests.Browser", "menu-visibility.browser.cjs"));
    using var probe = Process.Start(menuStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{Read(store,"Host")}:{((Uri)Read(fixture,"Address")).Port}", user = Read(account,"Name"), password = Read(account,"Password"), terminalId = Read(store,"TerminalId"), roleId = Read(account,"RoleId"), employeeId, menuId }));
    probe.StandardInput.Close(); using var menuTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try { await probe.WaitForExitAsync(menuTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Menu visibility browser failed.");
    Console.WriteLine("MENU VISIBILITY BROWSER PASS — disposable SQL only."); return;
}
if (args.Contains("--kiosk"))
{
    var seed = await Call(null, typeof(KioskSqlServerTests), "SeedAsync", fixture, store, Read(account, "UserId"));
    var kioskRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var kioskStart = new ProcessStartInfo("node") { WorkingDirectory = kioskRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    kioskStart.Environment["NODE_PATH"] = Path.Combine(kioskRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    kioskStart.ArgumentList.Add(Path.Combine(kioskRoot, "GaoApp.Tests.Browser", "kiosk.browser.cjs"));
    using var probe = Process.Start(kioskStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store,"Host")}:{((Uri)Read(fixture,"Address")).Port}",
        user = Read(account,"Name"), password = Read(account,"Password"), terminalId = Read(store,"TerminalId"),
        key = Read(seed,"Key"), barcode = Read(seed,"Barcode"), customerPhone = "0901234567", stationId = Read(seed,"StationId") }));
    probe.StandardInput.Close(); using var kioskTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await probe.WaitForExitAsync(kioskTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Kiosk browser failed.");
    Console.WriteLine("KIOSK BROWSER PASS — disposable SQL only; payment UI simulated, no bank contact."); return;
}
if (args.Contains("--customer-profile"))
{
    var seed = await Call(null, typeof(CustomerProfileSqlServerTests), "SeedAsync", fixture, store, Read(account,"UserId"));
    var profileRoot=(string)type.GetMethod("SourceRoot",flags)!.Invoke(null,null)!;
    var profileStart=new ProcessStartInfo("node") {WorkingDirectory=profileRoot,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true};
    profileStart.Environment["NODE_PATH"]=Path.Combine(profileRoot,"Logs","pos-offline-browser-deps","node_modules");
    profileStart.ArgumentList.Add(Path.Combine(profileRoot,"GaoApp.Tests.Browser","customer-profile.browser.cjs"));
    using var probe=Process.Start(profileStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {baseUrl=$"http://{Read(store,"Host")}:{((Uri)Read(fixture,"Address")).Port}",user=Read(account,"Name"),password=Read(account,"Password"),terminalId=Read(store,"TerminalId"),customerId=Read(seed,"CustomerId"),orderId=Read(seed,"OrderId"),voucherId=Read(seed,"VoucherId")}));
    probe.StandardInput.Close();using var profileTimeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try {await probe.WaitForExitAsync(profileTimeout.Token);}catch{if(!probe.HasExited)probe.Kill(true);throw;}
    if(probe.ExitCode!=0)throw new Exception("Customer profile browser failed.");
    Console.WriteLine("CUSTOMER PROFILE BROWSER PASS — disposable SQL only.");return;
}
if (args.Contains("--ledger-search"))
{
    await Call(null, typeof(InventoryLedgerSearchSqlServerTests), "SeedAsync", fixture, store, 2000);
    var ledgerRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var ledgerStart = new ProcessStartInfo("node") { WorkingDirectory = ledgerRoot, UseShellExecute = false,
        CreateNoWindow = true, RedirectStandardInput = true };
    ledgerStart.Environment["NODE_PATH"] = Path.Combine(ledgerRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    ledgerStart.ArgumentList.Add(Path.Combine(ledgerRoot, "GaoApp.Tests.Browser", "ledger-search.browser.cjs"));
    using var ledgerProbe = Process.Start(ledgerStart)!;
    await ledgerProbe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId") }));
    ledgerProbe.StandardInput.Close();
    using var ledgerTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try { await ledgerProbe.WaitForExitAsync(ledgerTimeout.Token); } catch { if (!ledgerProbe.HasExited) ledgerProbe.Kill(true); throw; }
    if (ledgerProbe.ExitCode != 0) throw new Exception("Ledger search browser assertions failed.");
    Console.WriteLine("LEDGER SEARCH BROWSER PASS — disposable SQL only.");
    return;
}
if (args.Contains("--receipt-entry"))
{
    var entryRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var entryDatabase = Read(fixture, "Database");
    await using var entryDb = (AppDbContext)entryDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(entryDatabase, new object?[] { Read(store, "StoreId"), null })!;
    var entryWarehouse = await entryDb.Warehouses.SingleAsync();
    var entryTerminal = await entryDb.POSTerminals.SingleAsync();
    entryDb.Add(new StockDocument { StoreId = (int)Read(store, "StoreId"), WarehouseId = entryWarehouse.Id,
        DocumentNo = "NK-CU-001", DocumentTitle = "Phiếu cũ chưa ghi nhận quầy" });
    await entryDb.SaveChangesAsync();
    using var entryResponse = await http.PostAsJsonAsync("/admin/api/stock-documents/receipts", new {
        legalEntityId = entryWarehouse.LegalEntityId, warehouseId = entryWarehouse.Id,
        documentTitle = "Nhập hàng buổi sáng", directReceiptReason = "Nhà phân phối giao" });
    entryResponse.EnsureSuccessStatusCode();
    var entryCreated = await entryResponse.Content.ReadFromJsonAsync<JsonElement>();
    var entryStart = new ProcessStartInfo("node") { WorkingDirectory = entryRoot, UseShellExecute = false,
        CreateNoWindow = true, RedirectStandardInput = true };
    entryStart.Environment["NODE_PATH"] = Path.Combine(entryRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    entryStart.ArgumentList.Add(Path.Combine(entryRoot, "GaoApp.Tests.Browser", "receipt-entry.browser.cjs"));
    using var entryProbe = Process.Start(entryStart)!;
    await entryProbe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"),
        terminalId = entryTerminal.Id, terminalName = entryTerminal.Name, receiptId = entryCreated.GetProperty("id").GetInt32() }));
    entryProbe.StandardInput.Close();
    using var entryTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    try { await entryProbe.WaitForExitAsync(entryTimeout.Token); } catch { if (!entryProbe.HasExited) entryProbe.Kill(true); throw; }
    if (entryProbe.ExitCode != 0) throw new Exception("Receipt entry browser assertions failed.");
    Console.WriteLine("RECEIPT ENTRY BROWSER PASS — disposable SQL only.");
    return;
}
if (args.Contains("--cash-adjustments"))
{
    var adjustmentAdmin = await Call(null, typeof(PosShiftAdministrationSqlServerTests), "AddAdminAsync", fixture, store);
    async Task<JsonElement> PostCash(string path, object body)
    {
        using var response = await http.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    var adjustmentShift = await PostCash("/admin/pos/shift/open", new { openingCash = 0, warehouseId = Read(store, "WarehouseId") });
    var adjustmentVoucher = await PostCash("/admin/pos/shift/cash-transaction", new { type = 1, amount = 30000, reason = "Thu tiền giao hàng", note = "Phiếu nhập nhầm loại" });
    await PostCash("/admin/pos/shift/close", new { closingCashActual = 30000 });
    var adjustmentRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var adjustmentStart = new ProcessStartInfo("node") { WorkingDirectory = adjustmentRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    adjustmentStart.Environment["NODE_PATH"] = Path.Combine(adjustmentRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    adjustmentStart.ArgumentList.Add(Path.Combine(adjustmentRoot, "GaoApp.Tests.Browser", "cash-adjustments.browser.cjs"));
    using var probe = Process.Start(adjustmentStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"),
        admin = Read(adjustmentAdmin, "Name"), adminPassword = Read(adjustmentAdmin, "Password"),
        voucherId = adjustmentVoucher.GetProperty("id").GetInt32(), shiftId = adjustmentShift.GetProperty("id").GetInt32() }));
    probe.StandardInput.Close();
    using var adjustmentTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try { await probe.WaitForExitAsync(adjustmentTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Cash adjustments browser assertions failed.");
    Console.WriteLine("CASH ADJUSTMENTS BROWSER PASS — disposable SQL only.");
    return;
}
if (args.Contains("--receipt-invoice-follow-up"))
{
    var seed = await Call(null, typeof(ReceiptBarcodeProposalSqlServerTests), "SeedAsync", fixture, store);
    var invoiceDatabase = Read(fixture, "Database");
    var receiptId = (int)Read(seed, "ReceiptId");
    int secondId;
    await using (var invoiceDb = (AppDbContext)invoiceDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(invoiceDatabase, new object?[] { Read(store, "StoreId"), null })!)
    {
        var first = await invoiceDb.StockDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == receiptId);
        first.Status = StockDocumentStatus.PendingApproval;
        var second = new StockDocument { StoreId = first.StoreId, DocumentNo = "FOLLOW-UP-SECOND", WarehouseId = first.WarehouseId,
            SupplierId = first.SupplierId, ReceiptSource = first.ReceiptSource, DirectReceiptReason = first.DirectReceiptReason,
            Status = StockDocumentStatus.PendingApproval, IsMerchandisePaid = true,
            Lines = first.Lines.Select(x => new StockDocumentLine { ProductVariantId = x.ProductVariantId,
                ProductUnitConversionId = x.ProductUnitConversionId, UnitId = x.UnitId, UnitNameSnapshot = x.UnitNameSnapshot,
                ProductNameSnapshot = x.ProductNameSnapshot, Factor = x.Factor, Quantity = x.Quantity, BaseQuantity = x.BaseQuantity,
                UnitCost = x.UnitCost, UnitPriceBeforeVat = x.UnitPriceBeforeVat, LineTotal = x.LineTotal, LineNo = x.LineNo }).ToList() };
        var imageVariantId = (int)Read(store, "VariantId");
        var variant = await invoiceDb.ProductVariants.SingleAsync(x => x.Id == imageVariantId);
        var image = new ProductImage { StoreId = first.StoreId, ProductId = variant.ProductId,
            MediaAsset = new MediaAsset { StoreId = first.StoreId, StoragePath = "uploads/products/receipt-follow-up.png", ContentType = "image/png" } };
        invoiceDb.AddRange(second, image); await invoiceDb.SaveChangesAsync();
        variant.PrimaryProductImageId = image.Id; await invoiceDb.SaveChangesAsync(); secondId = second.Id;
    }
    var invoiceRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var invoiceStart = new ProcessStartInfo("node") { WorkingDirectory = invoiceRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    invoiceStart.ArgumentList.Add(Path.Combine(invoiceRoot, "GaoApp.Tests.Browser", "receipt-invoice-follow-up.browser.cjs"));
    using var probe = Process.Start(invoiceStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"), receiptId, secondId }));
    probe.StandardInput.Close();
    using var invoiceTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try { await probe.WaitForExitAsync(invoiceTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Receipt invoice follow-up browser failed.");
    await using var check = (AppDbContext)invoiceDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(invoiceDatabase, new object?[] { Read(store, "StoreId"), null })!;
    if (await check.StockDocuments.CountAsync(x => (x.Id == receiptId || x.Id == secondId) && x.Status == StockDocumentStatus.Confirmed && x.WaitForInputInvoice == false) != 2)
        throw new Exception("Receipt approval/end-waiting did not persist.");
    Console.WriteLine("RECEIPT INVOICE FOLLOW-UP BROWSER PASS — disposable database only.");
    return;
}
if (args.Contains("--receipt-line-removal"))
{
    var removalSeed = await Call(null, typeof(ReceiptLineRemovalSqlServerTests), "SeedAsync", fixture, store);
    var removalRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var removalStart = new ProcessStartInfo("node") { WorkingDirectory = removalRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    removalStart.Environment["NODE_PATH"] = Path.Combine(removalRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    removalStart.ArgumentList.Add(Path.Combine(removalRoot, "GaoApp.Tests.Browser", "receipt-line-removal.browser.cjs"));
    using var removalProbe = Process.Start(removalStart)!;
    await removalProbe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"),
        receiptId = Read(removalSeed, "ReceiptId"), knownLineId = Read(removalSeed, "KnownLineId"), resolvedLineId = Read(removalSeed, "ResolvedLineId") }));
    removalProbe.StandardInput.Close();
    using var removalTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try { await removalProbe.WaitForExitAsync(removalTimeout.Token); } catch { if (!removalProbe.HasExited) removalProbe.Kill(true); throw; }
    if (removalProbe.ExitCode != 0) throw new Exception("Receipt line removal browser assertions failed.");
    var removalDatabase = Read(fixture, "Database");
    await using var check = (AppDbContext)removalDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(removalDatabase, new object?[] { Read(store, "StoreId"), null })!;
    var receiptId = (int)Read(removalSeed, "ReceiptId"); var itemId = (int)Read(removalSeed, "ItemId");
    if (await check.StockDocumentLines.AnyAsync(x => x.StockDocumentId == receiptId) ||
        (await check.StockDocumentProvisionalItems.SingleAsync(x => x.Id == itemId)).ResolvedStockDocumentLineId != (int)Read(removalSeed, "ResolvedLineId") ||
        await check.PurchaseReceiptAuditEvents.CountAsync(x => x.StockDocumentId == receiptId && x.EventType == PurchaseReceiptAuditEventType.PhysicalLineDeleted) != 2 ||
        (await check.InventoryBalances.SingleAsync()).OnHandQty != 100)
        throw new Exception("Removed lines, audit evidence or stock assertion failed.");
    Console.WriteLine("RECEIPT LINE REMOVAL BROWSER PASS — disposable SQL only.");
    return;
}
if (args.Contains("--intake-supplier") || args.Contains("--intake-completion"))
{
    var intakeSeed = await Call(null, typeof(ReceiptIntakeSupplierSqlServerTests), "SeedAsync", fixture, store);
    var intakeRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var intakeStart = new ProcessStartInfo("node") { WorkingDirectory = intakeRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    intakeStart.Environment["NODE_PATH"] = Path.Combine(intakeRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    intakeStart.ArgumentList.Add(Path.Combine(intakeRoot, "GaoApp.Tests.Browser", args.Contains("--intake-completion") ? "intake-completion.browser.cjs" : "intake-supplier.browser.cjs"));
    using var intakeProbe = Process.Start(intakeStart)!;
    await intakeProbe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"),
        receiptId = Read(intakeSeed, "ReceiptId"), supplierId = Read(intakeSeed, "SupplierId"),
        supplierName = Read(intakeSeed, "SupplierName"), itemIds = Read(intakeSeed, "ItemIds") }));
    intakeProbe.StandardInput.Close();
    using var intakeTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try { await intakeProbe.WaitForExitAsync(intakeTimeout.Token); } catch { if (!intakeProbe.HasExited) intakeProbe.Kill(true); throw; }
    if (intakeProbe.ExitCode != 0) throw new Exception("Intake supplier browser assertions failed.");
    var intakeDatabase = Read(fixture, "Database");
    await using var check = (AppDbContext)intakeDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(intakeDatabase, new object?[] { Read(store, "StoreId"), null })!;
    var receiptId = (int)Read(intakeSeed, "ReceiptId"); var supplierId = (int)Read(intakeSeed, "SupplierId");
    var saved = await check.StockDocuments.SingleAsync(x => x.Id == receiptId);
    var itemIds = (int[])Read(intakeSeed, "ItemIds");
    var products = await check.StockDocumentProvisionalItems.Where(x => itemIds.Contains(x.Id) && x.ResolvedProductVariantId != null)
        .Select(x => x.ResolvedProductVariant!.Product).ToListAsync();
    if (saved.SupplierId != supplierId || saved.Status != StockDocumentStatus.PendingApproval || products.Count != 2 ||
        products.Any(x => x.SupplierId != supplierId) || (await check.InventoryBalances.SingleAsync()).OnHandQty != 100)
        throw new Exception("Intake supplier persistence, catalog or inventory assertion failed.");
    if (args.Contains("--intake-completion")) {
        var productIds=products.Select(x=>x.Id).ToArray();
        var images=await check.ProductImages.Where(x=>productIds.Contains(x.ProductId)).ToListAsync();
        if(images.Count!=1 || !images[0].IsPrimary)throw new Exception("Review photo was not committed as the primary product image.");
        var pricedVariant=await check.ProductVariants.SingleAsync(x=>x.ProductId==images[0].ProductId);
        if(pricedVariant.CostPrice!=12000)throw new Exception("New product approval did not save the entered cost to the variant.");
    }
    Console.WriteLine("INTAKE SUPPLIER BROWSER PASS — disposable SQL only.");
    return;
}
if (args.Contains("--product-prices") || args.Contains("--product-filters"))
{
    var priceSeed = await Call(null, args.Contains("--product-filters") ? typeof(ProductFiltersSqlServerTests) : typeof(ProductPriceQuickViewSqlServerTests), "SeedAsync", fixture, store);
    var priceEmployee = await Call(fixture, type, "AddAccountAsync", store, new[] { PermissionCodes.Catalog.Product.View });
    var priceRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var priceStart = new ProcessStartInfo("node") { WorkingDirectory = priceRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    priceStart.Environment["NODE_PATH"] = Path.Combine(priceRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    priceStart.ArgumentList.Add(Path.Combine(priceRoot, "GaoApp.Tests.Browser", args.Contains("--product-filters") ? "product-filters.browser.cjs" : "product-prices.browser.cjs"));
    using var priceProbe = Process.Start(priceStart)!;
    await priceProbe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"),
        employee = Read(priceEmployee, "Name"), employeePassword = Read(priceEmployee, "Password"),
        terminalId = Read(store, "TerminalId"), productId = Read(priceSeed, "ProductId") }));
    priceProbe.StandardInput.Close();
    using var priceTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try { await priceProbe.WaitForExitAsync(priceTimeout.Token); } catch { if (!priceProbe.HasExited) priceProbe.Kill(true); throw; }
    if (priceProbe.ExitCode != 0) throw new Exception("Product price browser assertions failed.");
    Console.WriteLine("PRODUCT UNIT PRICES BROWSER PASS — disposable SQL only.");
    return;
}
if (args.Contains("--receipt-document-actions"))
{
    var seed = await Call(null, typeof(ReceiptBarcodeProposalSqlServerTests), "SeedAsync", fixture, store);
    var employee = await Call(fixture, type, "AddAccountAsync", store, new[] {
        PermissionCodes.Inventory.StockDocument.Create, PermissionCodes.Inventory.StockDocument.Update,
        PermissionCodes.Inventory.StockDocument.Delete, PermissionCodes.Inventory.StockDocument.View });
    var receiptActionsDatabase = Read(fixture, "Database");
    int deleteId;
    await using (var testDb = (AppDbContext)receiptActionsDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(receiptActionsDatabase, new object?[] { Read(store, "StoreId"), null })!)
    {
        var draft = new StockDocument { StoreId = (int)Read(store, "StoreId"), WarehouseId = (int)Read(store, "WarehouseId"),
            DocumentNo = "DELETE-UI-TEST", DocumentTitle = "Phiếu tạo nhầm", ReceiptSource = PurchaseReceiptSource.Direct };
        testDb.Add(draft); await testDb.SaveChangesAsync(); deleteId = draft.Id;
    }
    var testRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var testStart = new ProcessStartInfo("node") { WorkingDirectory = testRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    testStart.Environment["NODE_PATH"] = Path.Combine(testRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    testStart.ArgumentList.Add(Path.Combine(testRoot, "GaoApp.Tests.Browser", "receipt-document-actions.browser.cjs"));
    using var probe = Process.Start(testStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"),
        employee = Read(employee, "Name"), employeePassword = Read(employee, "Password"),
        terminalId = Read(store, "TerminalId"), receiptId = Read(seed, "ReceiptId"), deleteId }));
    probe.StandardInput.Close();
    using var testTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await probe.WaitForExitAsync(testTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Receipt document action browser assertions failed.");
    await using var check = (AppDbContext)receiptActionsDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(receiptActionsDatabase, new object?[] { Read(store, "StoreId"), null })!;
    if (!await check.StockDocuments.IgnoreQueryFilters().AnyAsync(x => x.Id == deleteId && x.IsDeleted)) throw new Exception("Draft was not soft deleted.");
    if ((await check.InventoryBalances.SingleAsync()).OnHandQty != 100) throw new Exception("Title actions moved stock.");
    Console.WriteLine("PASS: actual staff/manager UI, title approvals, draft deletion and retail comparison; disposable SQL only.");
    return;
}
if (args.Contains("--receipt-supplier") || args.Contains("--receipt-prices") || args.Contains("--receipt-price-drafts"))
{
    var seed = await Call(null, typeof(ReceiptBarcodeProposalSqlServerTests), "SeedAsync", fixture, store);
    var supplierDatabase = Read(fixture, "Database");
    var receiptId = (int)Read(seed, "ReceiptId");
    int supplierId, noTaxSupplierId;
    await using (var supplierDb = (AppDbContext)supplierDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(supplierDatabase, new object?[] { Read(store, "StoreId"), null })!)
    {
        var receipt = await supplierDb.StockDocuments.SingleAsync(x => x.Id == receiptId);
        receipt.SupplierId = null;
        receipt.Status = StockDocumentStatus.PendingApproval;
        if (args.Contains("--receipt-price-drafts"))
        {
            var original = await supplierDb.StockDocumentLines.FirstAsync(x => x.StockDocumentId == receiptId);
            for (var n = 3; n <= 10; n++) supplierDb.StockDocumentLines.Add(new StockDocumentLine
            {
                StockDocumentId = receiptId, LineNo = n, ProductVariantId = original.ProductVariantId,
                UnitId = original.UnitId, ProductUnitConversionId = original.ProductUnitConversionId, Factor = original.Factor,
                Quantity = 2, BaseQuantity = 2 * original.Factor, UnitCost = 10, UnitPriceBeforeVat = 10, LineTotal = 20,
                ProductNameSnapshot = "Sản phẩm thử nháp " + n, UnitNameSnapshot = original.UnitNameSnapshot
            });
        }
        var supplier = await supplierDb.Suppliers.SingleAsync();
        supplier.Name = "Nhà cung cấp thử lưu XML";
        supplier.TaxCode = "0123456789";
        supplierId = supplier.Id;
        var noTax = new Supplier { StoreId = (int)Read(store, "StoreId"), Name = "Nhà cung cấp thiếu MST", Code = "NO-TAX-TEST" };
        supplierDb.Suppliers.Add(noTax);
        await supplierDb.SaveChangesAsync();
        noTaxSupplierId = noTax.Id;
        var imageVariantId = (int)Read(store, "VariantId");
        var imageVariant = await supplierDb.ProductVariants.SingleAsync(x => x.Id == imageVariantId);
        var previewImage = new ProductImage { StoreId = imageVariant.StoreId, ProductId = imageVariant.ProductId,
            MediaAsset = new MediaAsset { StoreId = imageVariant.StoreId, StoragePath = "uploads/products/receipt-preview.png", ContentType = "image/png" } };
        supplierDb.ProductImages.Add(previewImage); await supplierDb.SaveChangesAsync();
        imageVariant.PrimaryProductImageId = previewImage.Id; await supplierDb.SaveChangesAsync();
        supplierDb.Taxes.Add(new Tax { StoreId = imageVariant.StoreId, Code = "VAT-TEST-8", Name = "VAT 8%", Rate = 8, IsActive = true });
        await supplierDb.SaveChangesAsync();
    }
    var supplierRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    if (args.Contains("--receipt-supplier"))
    {
        var invoiceDate = DateTime.Today;
        var xmlFolder = Path.Combine((string)Read(fixture, "InvoiceLibraryRoot"), invoiceDate.Year.ToString(), "0123456789", invoiceDate.ToString("MM"));
        Directory.CreateDirectory(xmlFolder);
        await File.WriteAllTextAsync(Path.Combine(xmlFolder, "supplier-test.xml"), $"""
            <HDon><DLHDon><TTChung><KHMSHDon>1</KHMSHDon><KHHDon>C26TEST</KHHDon><SHDon>1001</SHDon><NLap>{invoiceDate:yyyy-MM-dd}</NLap></TTChung><NDHDon><NBan><Ten>Nhà cung cấp thử lưu XML</Ten><MST>0123456789</MST></NBan><NMua><Ten>Buyer test</Ten></NMua><DSHHDVu><HHDVu><STT>1</STT><THHDVu>Hàng kiểm thử XML</THHDVu><DVTinh>Cái</DVTinh><SLuong>1</SLuong><DGia>100</DGia><ThTien>100</ThTien><TSuat>8%</TSuat></HHDVu></DSHHDVu><TToan><TgTCThue>100</TgTCThue><TgTThue>8</TgTThue><TgTTTBSo>108</TgTTTBSo></TToan></NDHDon></DLHDon></HDon>
            """);
    }
    var supplierStart = new ProcessStartInfo("node") { WorkingDirectory = supplierRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    supplierStart.Environment["NODE_PATH"] = Path.Combine(supplierRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    supplierStart.ArgumentList.Add(Path.Combine(supplierRoot, "GaoApp.Tests.Browser",
        args.Contains("--receipt-price-drafts") ? "receipt-price-drafts.browser.cjs" : args.Contains("--receipt-prices") ? "receipt-prices.browser.cjs" : "receipt-supplier.browser.cjs"));
    using var probe = Process.Start(supplierStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"),
        receiptId, supplierId, noTaxSupplierId, packId = Read(seed, "PackId"), cartonId = Read(seed, "CartonId")
    }));
    probe.StandardInput.Close(); await probe.WaitForExitAsync();
    if (probe.ExitCode != 0) throw new Exception("Receipt supplier browser assertions failed.");
    await using var check = (AppDbContext)supplierDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(supplierDatabase, new object?[] { Read(store, "StoreId"), null })!;
    var saved = await check.StockDocuments.SingleAsync(x => x.Id == receiptId);
    if (args.Contains("--receipt-prices"))
    {
        var packId = (int)Read(seed, "PackId");
        var cartonId = (int)Read(seed, "CartonId");
        if ((await check.ProductUnitConversions.SingleAsync(x => x.Id == packId)).Price != 60000 ||
            (await check.ProductUnitConversions.SingleAsync(x => x.Id == packId)).WholesalePrice != 55000 ||
            (await check.ProductUnitConversions.SingleAsync(x => x.Id == cartonId)).Price != 280000 ||
            saved.Status != StockDocumentStatus.PendingApproval ||
            (await check.InventoryBalances.SingleAsync()).OnHandQty != 100)
            throw new Exception("Price update did not preserve unselected prices or receipt/inventory state.");
        var pricedVariantId = (int)Read(store, "VariantId");
        if ((await check.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == pricedVariantId)).Product.BasePrice != 15000)
            throw new Exception("Product price must equal the pack retail price divided by its factor.");
        Console.WriteLine("RECEIPT SELLING PRICES BROWSER PASS — disposable database only.");
        return;
    }
    if (args.Contains("--receipt-price-drafts"))
    {
        if (saved.Status != StockDocumentStatus.PendingApproval || (await check.InventoryBalances.SingleAsync()).OnHandQty != 100 || await check.PurchasePayables.AnyAsync())
            throw new Exception("Draft save must not post inventory or payables.");
        Console.WriteLine("RECEIPT PRICE DRAFT BROWSER PASS — disposable database only.");
        return;
    }
    if (saved.SupplierId != supplierId || saved.Status != StockDocumentStatus.PendingApproval)
        throw new Exception("Supplier was not saved independently of commercial approval.");
    if ((await check.InventoryBalances.SingleAsync()).OnHandQty != 100)
        throw new Exception("Saving supplier must not post inventory.");
    Console.WriteLine("RECEIPT SUPPLIER BROWSER PASS — disposable database only.");
    return;
}
if (args.Contains("--customer-debt") || args.Contains("--customer-deposit"))
{
    var debtDatabase = Read(fixture, "Database");
    int customerId;
    await using (var debtDb = (AppDbContext)debtDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(debtDatabase, new object?[] { Read(store, "StoreId"), null })!)
    {
        var customer = new Customer { StoreId = (int)Read(store, "StoreId"), Name = "Khách công nợ trình duyệt", HaveDebt = true };
        debtDb.Customers.Add(customer);
        if (args.Contains("--customer-deposit"))
            debtDb.StoreBankAccounts.Add(new StoreBankAccount { StoreId = (int)Read(store, "StoreId"), AccountName = "GẠO APP TEST", AccountNumber = "123456789", BankCode = "VCB", BankName = "Vietcombank", VietQrBankBin = "970436", IsDefault = true, IsActive = true, ConfirmMode = BankQrConfirmMode.Manual });
        await debtDb.SaveChangesAsync(); customerId = customer.Id;
    }
    (await http.PostAsJsonAsync("/admin/pos/shift/open", new { openingCash = 0, warehouseId = Read(store, "WarehouseId") })).EnsureSuccessStatusCode();
    var draft = await (await http.PostAsync("/admin/pos/draft", null)).Content.ReadFromJsonAsync<JsonElement>();
    var orderId = draft.GetProperty("orderId").GetInt32();
    (await http.PostAsync($"/admin/pos/{orderId}/items?variantId={Read(store, "VariantId")}&qty=3", null)).EnsureSuccessStatusCode();
    var debtRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var debtStart = new ProcessStartInfo("node") { WorkingDirectory = debtRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    debtStart.ArgumentList.Add(Path.Combine(debtRoot, "GaoApp.Tests.Browser", args.Contains("--customer-deposit") ? "customer-deposit.browser.cjs" : "customer-debt.browser.cjs"));
    using var probe = Process.Start(debtStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"), customerId, orderId
    }));
    probe.StandardInput.Close(); await probe.WaitForExitAsync();
    if (probe.ExitCode != 0) throw new Exception("Customer debt browser assertions failed.");
    Console.WriteLine("CUSTOMER DEBT BROWSER PASS — disposable debtDatabase only.");
    return;
}
if (args.Contains("--shift-admin"))
{
    var shiftAdminDatabase = Read(fixture, "Database");
    await using (var shiftAdminDb = (AppDbContext)shiftAdminDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(shiftAdminDatabase, new object?[] { Read(store, "StoreId"), null })!)
    {
        var userId = (int)Read(account, "UserId");
        var membership = await shiftAdminDb.UserInStores.SingleAsync(x => x.UserId == userId);
        membership.RoleId = await shiftAdminDb.Roles.Where(x => x.Code == "ADMIN" && x.IsSystemRole).Select(x => x.Id).SingleAsync();
        await shiftAdminDb.SaveChangesAsync();
    }
    var employee = await Call(fixture, type, "AddAccountAsync", store, new[] { "*" });
    var shiftAdminRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var shiftAdminStart = new ProcessStartInfo("node") { WorkingDirectory = shiftAdminRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    shiftAdminStart.ArgumentList.Add(Path.Combine(shiftAdminRoot, "GaoApp.Tests.Browser", "pos-shift-admin.browser.cjs"));
    using var probe = Process.Start(shiftAdminStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"),
        employeeId = Read(employee, "UserId"), employeeUser = Read(employee, "Name"), employeePassword = Read(employee, "Password"),
        terminalId = Read(store, "TerminalId"), warehouseId = Read(store, "WarehouseId")
    }));
    probe.StandardInput.Close(); await probe.WaitForExitAsync();
    if (probe.ExitCode != 0) throw new Exception("Shift administration browser test failed.");
    return;
}
if (args.Contains("--media-library"))
{
    var mediaIds = new List<int>();
    var mediaVariantId = (int)Read(store, "VariantId");
    var mediaDatabase = Read(fixture, "Database");
    await using (var mediaDb = (AppDbContext)mediaDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(mediaDatabase, new object?[] { Read(store, "StoreId"), null })!)
    {
        var names = new[] { "Sua-tuoi-180ml.png", "Banh-quy-bo.png", "Ca-phe-rang-xay.png", "Tra-xanh-500ml.png", "Anh-thu-nghiem.png", "Goi-san-pham-cu.png", "Anh-dang-tai.png", "San-pham-moi.png" };
        foreach (var name in names)
        {
            using var body = new MultipartFormDataContent();
            var content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j0ZkAAAAASUVORK5CYII="));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png"); body.Add(content, "file", name);
            using var response = await http.PostAsync("/admin/media/temp", body); response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadAsStringAsync();
            var asset = await mediaDb.MediaAssets.SingleAsync(a => a.TempToken == token); mediaIds.Add(asset.Id);
            if (mediaIds.Count <= 6) { asset.IsTemp = false; asset.TempToken = null; }
            asset.ExpireAtUtc = mediaIds.Count <= 2 ? null : mediaIds.Count <= 4 ? DateTime.UtcNow.AddDays(7) : mediaIds.Count <= 6 ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(6);
            if (mediaIds.Count <= 2)
            {
                var variant = await mediaDb.ProductVariants.SingleAsync(v => v.Id == mediaVariantId);
                mediaDb.ProductImages.Add(new ProductImage { StoreId = variant.StoreId, ProductId = variant.ProductId, MediaAssetId = asset.Id });
            }
            await mediaDb.SaveChangesAsync();
        }
    }
    var mediaRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var mediaStart = new ProcessStartInfo("node") { WorkingDirectory = mediaRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    mediaStart.Environment["NODE_PATH"] = Path.Combine(mediaRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    mediaStart.ArgumentList.Add(Path.Combine(mediaRoot, "GaoApp.Tests.Browser", "media-library.browser.cjs"));
    using var probe = Process.Start(mediaStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"), ids = mediaIds }));
    probe.StandardInput.Close(); await probe.WaitForExitAsync();
    if (probe.ExitCode != 0) throw new Exception("Media library browser assertions failed.");
    Console.WriteLine("MEDIA LIBRARY BROWSER PASS — disposable store and generated test uploads only.");
    return;
}
if (args.Contains("--receipt-barcode-proposals") || args.Contains("--receipt-intake") || args.Contains("--receipt-history") || args.Contains("--receiving-mobile"))
{
    var employee = await Call(fixture, type, "AddAccountAsync", store, new[] {
        PermissionCodes.Inventory.StockDocument.Create, PermissionCodes.Inventory.StockDocument.View,
        PermissionCodes.Inventory.StockDocument.Update, PermissionCodes.Inventory.StockDocument.Delete });
    var seed = await Call(null, typeof(ReceiptBarcodeProposalSqlServerTests), "SeedAsync", fixture, store);
    var barcodeDatabase = Read(fixture, "Database");
    await using (var barcodeDb = (AppDbContext)barcodeDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(barcodeDatabase, new object?[] { Read(store, "StoreId"), null })!)
    {
        var barcodeVariantId = (int)Read(store, "VariantId");
        var variant = await barcodeDb.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == barcodeVariantId);
        var baseConversion = new ProductUnitConversion { StoreId = variant.StoreId, ProductVariantId = variant.Id,
            UnitId = variant.Product.BaseUnitId, Factor = 1, IsBaseUnit = true };
        barcodeDb.Add(baseConversion); await barcodeDb.SaveChangesAsync();
        barcodeDb.Add(new ProductVariantUnitBarcode { StoreId = variant.StoreId, ProductUnitConversionId = baseConversion.Id,
            Barcode = "INTERNAL-BOX", BarcodeType = BarcodeType.Internal, IsPrimary = true });
        await barcodeDb.SaveChangesAsync();
        // Deterministically exercise '+' in SQL rowversion Base64. Only this
        // disposable fixture receipt is touched; no application data is used.
        var barcodeReceiptId = (int)Read(seed, "ReceiptId");
        if (args.Contains("--receiving-mobile") && (Environment.GetEnvironmentVariable("RECEIVING_SCROLL_CHECK") == "1" || Environment.GetEnvironmentVariable("RECEIVING_INTERACTION_CHECK") == "1" || Environment.GetEnvironmentVariable("RECEIVING_FEEDBACK_RELOAD_CHECK") == "1" || Environment.GetEnvironmentVariable("RECEIVING_UI05B_CHECK") == "1"))
        {
            // A long, disposable receipt exposes scroll clamping when mobile controls are rebuilt.
            var sample = await barcodeDb.StockDocumentLines.AsNoTracking()
                .FirstAsync(x => x.StockDocumentId == barcodeReceiptId);
            for (var lineNo = 3; lineNo <= 22; lineNo++)
                barcodeDb.StockDocumentLines.Add(new StockDocumentLine {
                    StockDocumentId = barcodeReceiptId, LineNo = lineNo,
                    ProductVariantId = sample.ProductVariantId, ProductUnitConversionId = sample.ProductUnitConversionId,
                    UnitId = sample.UnitId, UnitNameSnapshot = sample.UnitNameSnapshot,
                    ProductNameSnapshot = $"Hàng kiểm tra vị trí dòng {lineNo}",
                    Factor = sample.Factor, Quantity = 5, BaseQuantity = 5 * sample.Factor,
                    UnitCost = 10, UnitPriceBeforeVat = 10, LineTotal = 50
                });
            await barcodeDb.SaveChangesAsync();
        }
        if (args.Contains("--receiving-mobile") && (Environment.GetEnvironmentVariable("RECEIVING_INTERACTION_CHECK") == "1" || Environment.GetEnvironmentVariable("RECEIVING_FEEDBACK_RELOAD_CHECK") == "1"))
        {
            for (var index = 1; index <= 3; index++)
            {
                var keyboardVariant = new ProductVariant { StoreId = variant.StoreId, ProductId = variant.ProductId,
                    Sku = $"KEYBOARD-{index}", ProductVariantName = $"Sữa kiểm tra bàn phím {index}", IsActive = true };
                barcodeDb.Add(keyboardVariant); await barcodeDb.SaveChangesAsync();
                var keyboardUnit = new ProductUnitConversion { StoreId = variant.StoreId, ProductVariantId = keyboardVariant.Id,
                    UnitId = variant.Product.BaseUnitId, Factor = 1, IsBaseUnit = true };
                barcodeDb.Add(keyboardUnit); await barcodeDb.SaveChangesAsync();
                barcodeDb.Add(new ProductVariantUnitBarcode { StoreId = variant.StoreId, ProductUnitConversionId = keyboardUnit.Id,
                    Barcode = $"209990000000{index}", BarcodeType = BarcodeType.Internal, IsPrimary = true });
            }
            await barcodeDb.SaveChangesAsync();
        }
        var foundPlus = false;
        for (var attempt = 0; attempt < 1100; attempt++)
        {
            var version = await barcodeDb.StockDocuments.AsNoTracking().Where(x => x.Id == barcodeReceiptId).Select(x => x.RowVersion).SingleAsync();
            if (Convert.ToBase64String(version).Contains('+')) { foundPlus = true; break; }
            await barcodeDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE [StockDocument] SET [Note]=[Note] WHERE [Id]={barcodeReceiptId}");
        }
        if (!foundPlus) throw new Exception("Unable to create the disposable '+' rowversion regression fixture.");
        if (args.Contains("--receipt-history"))
        {
            // Read-only history UI coverage, using synthetic evidence in the disposable database only.
            foreach (var eventType in Enum.GetValues<PurchaseReceiptAuditEventType>())
            {
                var isProvisional = (int)eventType >= 37;
                var oldValues = eventType == PurchaseReceiptAuditEventType.PhysicalLineChanged
                    ? JsonSerializer.Serialize(new { Quantity = 2, UnitCost = 10000 }) : "{}";
                var newValues = isProvisional
                    ? JsonSerializer.Serialize(new { NameSnapshot = "Sữa tươi ít đường 180 ml", Quantity = 3,
                        UnitNameSnapshot = "thùng", ProposedFactor = 48, ProposedBaseUnitName = "hộp",
                        Status = eventType is PurchaseReceiptAuditEventType.ProvisionalItemResolvedExisting or PurchaseReceiptAuditEventType.ProvisionalItemResolvedQuickCreate ? 1 : 0 })
                    : eventType == PurchaseReceiptAuditEventType.PhysicalLineChanged
                        ? JsonSerializer.Serialize(new { Quantity = 5, UnitCost = 12000 })
                        : JsonSerializer.Serialize(new { Status = "PendingApproval", Note = "Đã kiểm đếm thực tế tại kho." });
                var auditFixture = new PurchaseReceiptAuditEvent
                {
                    StoreId = (int)Read(store, "StoreId"), StockDocumentId = barcodeReceiptId, EventType = eventType,
                    ActorUserId = (int)Read((int)eventType % 2 == 0 ? account : employee, "UserId"), ActorUserName = (int)eventType % 2 == 0 ? "Trần Dũng" : "Ngọc Anh",
                    OccurredAtUtc = new DateTime(2026, 9, 11, 6, 0, 0, DateTimeKind.Utc).AddMinutes((int)eventType),
                    ChangedFieldsJson = isProvisional ? "[\"ProvisionalItem\"]" : "[\"Quantity\",\"UnitCost\"]",
                    OldValuesJson = oldValues, NewValuesJson = newValues,
                    IsSuccess = eventType != PurchaseReceiptAuditEventType.ProvisionalConfirmBlocked
                };
                // Parameterized fixture insert preserves historical timestamps; SaveChanges stamps the current actor/time.
                await barcodeDb.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [PurchaseReceiptAuditEvents]
                    ([StoreId], [StockDocumentId], [EventType], [ActorUserId], [ActorUserName], [OccurredAtUtc],
                     [ChangedFieldsJson], [OldValuesJson], [NewValuesJson], [IsSuccess])
                    VALUES ({auditFixture.StoreId}, {auditFixture.StockDocumentId}, {(int)auditFixture.EventType},
                            {auditFixture.ActorUserId}, {auditFixture.ActorUserName}, {auditFixture.OccurredAtUtc},
                            {auditFixture.ChangedFieldsJson}, {auditFixture.OldValuesJson}, {auditFixture.NewValuesJson}, {auditFixture.IsSuccess})
                    """);
            }
        }
    }
    var ui05bSeam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.ReceivingDetailV2BrowserFixture")!;
    var ui05bSeed = Environment.GetEnvironmentVariable("RECEIVING_UI05B_CHECK") == "1"
        ? await Call(null, ui05bSeam, "PrepareAsync", fixture, store, Read(seed, "ReceiptId")) : null;
    var ui05cSeam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.ReceivingPackingBrowserFixture")!;
    var ui05cSeed = Environment.GetEnvironmentVariable("RECEIVING_UI05C_CHECK") == "1"
        ? await Call(null, ui05cSeam, "PrepareAsync", fixture, store, Read(seed, "ReceiptId")) : null;
    var ui05dSeam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.ReceivingAcknowledgementBrowserFixture")!;
    var ui05dSeed = Environment.GetEnvironmentVariable("RECEIVING_UI05D_CHECK") == "1"
        ? await Call(null, ui05dSeam, "PrepareAsync", fixture, store, Read(seed, "ReceiptId")) : null;
    var ui05eSeam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.ReceivingAcceptanceBrowserFixture")!;
    var ui05eSeed = Environment.GetEnvironmentVariable("RECEIVING_UI05E_CHECK") == "1"
        ? await Call(null, ui05eSeam, "PrepareAsync", fixture, store, Read(seed, "ReceiptId")) : null;
    var sourceRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var probeStart = new ProcessStartInfo("node") { WorkingDirectory = sourceRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    probeStart.Environment["NODE_PATH"] = Path.Combine(sourceRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    probeStart.ArgumentList.Add(Path.Combine(sourceRoot, "GaoApp.Tests.Browser", args.Contains("--receipt-history")
        ? "receipt-history.browser.cjs" : args.Contains("--receiving-mobile") ? "receiving-mobile.browser.cjs" : args.Contains("--receipt-intake") ? "receipt-intake.browser.cjs" : "receipt-barcode-proposals.browser.cjs"));
    using var probe = Process.Start(probeStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"),
        receiptId = Read(seed, "ReceiptId"), packId = Read(seed, "PackId"),
        ui05b = ui05bSeed is null ? null : new { emptyId = Read(ui05bSeed, "EmptyId"), rejectedId = Read(ui05bSeed, "RejectedId"),
            denied = new { user = Read(Read(ui05bSeed, "Denied"), "Name"), password = Read(Read(ui05bSeed, "Denied"), "Password") } },
        ui05c = ui05cSeed is null ? null : new { packLineId = Read(ui05cSeed, "PackLineId"), cartonLineId = Read(ui05cSeed, "CartonLineId"),
            otherLineId = Read(ui05cSeed, "OtherLineId"), baseUnitId = Read(ui05cSeed, "BaseUnitId"), receiveUnitId = Read(ui05cSeed, "ReceiveUnitId") },
        ui05e = ui05eSeed is null ? null : new { packLineId = Read(ui05eSeed, "PackLineId"), otherLineId = Read(ui05eSeed, "OtherLineId"),
            readOnlyId = Read(ui05eSeed, "ReadOnlyId"), foreignId = Read(ui05eSeed, "ForeignId"),
            denied = new { user = Read(Read(ui05eSeed, "Denied"), "Name"), password = Read(Read(ui05eSeed, "Denied"), "Password") } },
        employee = new { user = Read(employee, "Name"), password = Read(employee, "Password") }
    }));
    probe.StandardInput.Close();
    using var probeTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await probe.WaitForExitAsync(probeTimeout.Token); }
    catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Receipt barcode browser probe failed.");
    if (ui05bSeed is not null) await Call(null, ui05bSeam, "VerifyAsync", fixture, store, ui05bSeed);
    if (ui05cSeed is not null) await Call(null, ui05cSeam, "VerifyAsync", fixture, store, ui05cSeed);
    if (ui05dSeed is not null) await Call(null, ui05dSeam, "VerifyAsync", fixture, store, ui05dSeed);
    if (ui05eSeed is not null) await Call(null, ui05eSeam, "VerifyAsync", fixture, store, ui05eSeed);
    Console.WriteLine(ui05cSeed is not null ? "PASS UI-05c: exact per-unit quantities, packaging photo and completion draft; catalog, inventory and menus unchanged."
        : ui05bSeed is not null ? "PASS UI-05b: one real receiving add, one submit, exact SQL quantity/status; protected inventory, menus and other receipts unchanged."
        : args.Contains("--receipt-history") ? "PASS: Receipt history browser checks against isolated SQL."
        : "PASS: Chrome employee proposal, receipt-local scan and manager approval against isolated SQL.");
    return;
}
if (args.Contains("--role-permissions"))
{
    var roleRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var roleStart = new ProcessStartInfo("node") { WorkingDirectory = roleRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    roleStart.Environment["NODE_PATH"] = Path.Combine(roleRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    roleStart.ArgumentList.Add(Path.Combine(roleRoot, "GaoApp.Tests.Browser", "role-permissions.browser.cjs"));
    using var roleProbe = Process.Start(roleStart)!;
    await roleProbe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId")
    }));
    roleProbe.StandardInput.Close();
    using var roleTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await roleProbe.WaitForExitAsync(roleTimeout.Token); }
    catch { if (!roleProbe.HasExited) roleProbe.Kill(true); throw; }
    if (roleProbe.ExitCode != 0) throw new Exception("Role permissions browser probe failed.");
    var roleDatabase = Read(fixture, "Database");
    await using var roleDb = (AppDbContext)roleDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(roleDatabase, new object?[] { Read(store, "StoreId"), null })!;
    var savedRole = await roleDb.Roles.SingleAsync(r => r.Code == "thu_ngan_browser");
    var actual = await roleDb.RolePermissions.Where(r => r.RoleId == savedRole.Id).Select(r => r.Permission.Code).OrderBy(x => x).ToListAsync();
    var expected = new[] { PermissionCodes.System.ProductLabel.Print, PermissionCodes.Pos.Order.View, PermissionCodes.Pos.Order.Refund, PermissionCodes.Pos.Payment.Refund }.OrderBy(x => x);
    if (!actual.SequenceEqual(expected)) throw new Exception("Saved grants differ from the browser selection.");
    Console.WriteLine("PASS role creation and exact grants persisted, disposable SQL only.");
    return;
}
if (args.Contains("--pos-order-acceptance"))
{
    var acceptanceType = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosOrderAcceptanceBrowserFixture")!;
    var acceptanceSeed = await Call(null, acceptanceType, "PrepareAsync", fixture, store, account);
    var acceptanceRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var acceptanceStart = new ProcessStartInfo("node") { WorkingDirectory=acceptanceRoot, UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true };
    acceptanceStart.Environment["NODE_PATH"] = Path.Combine(acceptanceRoot,"Logs","pos-offline-browser-deps","node_modules");
    acceptanceStart.ArgumentList.Add(Path.Combine(acceptanceRoot,"GaoApp.Tests.Browser","pos-order-acceptance.browser.cjs"));
    using var acceptanceProbe = Process.Start(acceptanceStart)!;
    await acceptanceProbe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl=$"http://{Read(store,"Host")}:{((Uri)Read(fixture,"Address")).Port}",
        user=Read(account,"Name"),password=Read(account,"Password"),terminalId=Read(store,"TerminalId"),
        cases=Read(acceptanceSeed,"Cases"),writeOrderId=Read(acceptanceSeed,"WriteOrderId"),
        viewOnly=Read(acceptanceSeed,"ViewOnly"),reconcileOnly=Read(acceptanceSeed,"ReconcileOnly")
    }));
    acceptanceProbe.StandardInput.Close();
    using var acceptanceTimeout=new CancellationTokenSource(TimeSpan.FromMinutes(8));
    try { await acceptanceProbe.WaitForExitAsync(acceptanceTimeout.Token); }
    catch { if(!acceptanceProbe.HasExited)acceptanceProbe.Kill(true);throw; }
    if(acceptanceProbe.ExitCode!=0)throw new InvalidOperationException("POS order acceptance browser failed.");
    Console.WriteLine(await Call(null,acceptanceType,"VerifyAsync",fixture,store,acceptanceSeed));return;
}
if (args.Contains("--pos-order-detail"))
{
    var detailFixture = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosOrderDetailBrowserFixture")!;
    var detailSeed = await Call(null, detailFixture, "PrepareAsync", fixture, store, account);
    var detailRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var detailStart = new ProcessStartInfo("node") { WorkingDirectory = detailRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    detailStart.Environment["NODE_PATH"] = Path.Combine(detailRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    detailStart.ArgumentList.Add(Path.Combine(detailRoot, "GaoApp.Tests.Browser", "pos-order-detail.browser.cjs"));
    using var detailProbe = Process.Start(detailStart)!;
    await detailProbe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"),
        orderId = Read(detailSeed, "OrderId"), emptyOrderId = Read(detailSeed, "EmptyOrderId"),
        deliveryOrderId = Read(detailSeed, "DeliveryOrderId"),
        readOnlyUser = Read(detailSeed, "ReadOnlyUser"), readOnlyPassword = Read(detailSeed, "ReadOnlyPassword"),
        capabilityAccounts = Read(detailSeed, "CapabilityAccounts")
    }));
    detailProbe.StandardInput.Close();
    using var detailTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
    try { await detailProbe.WaitForExitAsync(detailTimeout.Token); }
    catch { if (!detailProbe.HasExited) detailProbe.Kill(true); throw; }
    if (detailProbe.ExitCode != 0) throw new InvalidOperationException("POS order detail browser probe failed.");
    Console.WriteLine(await Call(null, detailFixture, "VerifyAsync", fixture, store, detailSeed));
    return;
}
if (args.Contains("--pos-orders-page") || args.Contains("--invoice-input-stock"))
{
    var ordersRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var ordersStart = new ProcessStartInfo("node") { WorkingDirectory = ordersRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    ordersStart.Environment["NODE_PATH"] = Path.Combine(ordersRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    ordersStart.ArgumentList.Add(Path.Combine(ordersRoot, "GaoApp.Tests.Browser", args.Contains("--invoice-input-stock") ? "invoice-input-stock.browser.cjs" : "pos-orders-page.browser.cjs"));
    using var probe = Process.Start(ordersStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
        baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId")
    }));
    probe.StandardInput.Close();
    using var ordersTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await probe.WaitForExitAsync(ordersTimeout.Token); }
    catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("POS orders browser probe failed.");
    Console.WriteLine(args.Contains("--invoice-input-stock")
        ? "PASS: XML stock Razor/auth, real SQL projection API and controlled balance/ledger UI; disposable SQL only."
        : "PASS: POS orders Razor/auth, real empty API and controlled list/receipt UI; disposable SQL only.");
    return;
}
if (args.Contains("--customer-display") || args.Contains("--customer-display-receipt") || args.Contains("--customer-payment-display"))
{
    (await http.PostAsJsonAsync("/admin/pos/shift/open", new { openingCash = 0, warehouseId = Read(store, "WarehouseId") })).EnsureSuccessStatusCode();
    var displayDatabase = Read(fixture, "Database");
    var displayReceiptVariantIds = new List<int>();
    var displayCustomerIds = new List<int>();
    await using (var displayDb = (AppDbContext)displayDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(displayDatabase, new object?[] { Read(store, "StoreId"), null })!)
    {
        var displayStoreId = (int)Read(store, "StoreId");
        var displayStore = await displayDb.Stores.SingleAsync(x => x.Id == displayStoreId);
        displayStore.ReceiptName = "GẠO · MARKET";
        displayStore.ReceiptAddress = "128 Nguyễn Văn Cừ · TP. Hồ Chí Minh";
        var displayUserId = (int)Read(account, "UserId");
        (await displayDb.Users.SingleAsync(x => x.Id == displayUserId)).FullName = "Nguyễn Minh An";
        if (args.Contains("--customer-payment-display"))
            displayDb.StoreBankAccounts.Add(new StoreBankAccount { StoreId = displayStoreId, BankCode = "ACB", BankName = "Synthetic display bank",
                AccountNumber = "123456789", AccountName = "Synthetic recipient", VietQrBankBin = "970416", IsActive = true,
                IsDefault = true, QrRenderMode = BankQrRenderMode.LocalEmvQr, ConfirmMode = BankQrConfirmMode.Manual });
        if (args.Contains("--customer-display-receipt"))
        {
            var retailCustomer = new Customer { StoreId = displayStoreId, Name = "Khách điểm màn hình", Phone = "0901234567", PriceTier = "RETAIL" };
            var zeroCustomer = new Customer { StoreId = displayStoreId, Name = "Khách mới chưa tích điểm", Phone = "0901234568", PriceTier = "RETAIL" };
            var wholesaleCustomer = new Customer { StoreId = displayStoreId, Name = "Khách sỉ màn hình", Phone = "0901234569", PriceTier = "WHOLESALE" };
            displayDb.AddRange(retailCustomer, zeroCustomer, wholesaleCustomer,
                new RewardSettings { StoreId = displayStoreId, IsEnabled = true, MoneyPerPoint = 1000, PointsPerVoucher = 100, VoucherValue = 10000 },
                new CustomerRewardLedger { StoreId = displayStoreId, Customer = retailCustomer, Type = CustomerRewardLedgerType.ManualAdjust, Amount = 1250000 },
                new CustomerRewardLedger { StoreId = displayStoreId, Customer = wholesaleCustomer, Type = CustomerRewardLedgerType.ImportOldBalance, Amount = 999000 });
            await displayDb.SaveChangesAsync();
            displayCustomerIds.AddRange(new[] { retailCustomer.Id, zeroCustomer.Id, wholesaleCustomer.Id });
            var originalId = (int)Read(store, "VariantId");
            var original = await displayDb.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == originalId);
            var movementService = (GaoApp.Application.Services.Inventory.InventoryMovementService)
                typeof(GaoApp.Tests.Inventory.InventoryPosPostingContractTests).GetMethod("CreateRealMovementService", flags)!
                    .Invoke(null, new object[] { displayDb })!;
            for (var i = 0; i < 20; i++)
            {
                var name = i == 0 ? "<img src=x onerror=alert(1)> Hàng kiểm thử có tên dài" : "Sản phẩm bill dài số " + i;
                var product = new Product { StoreId = displayStoreId, Name = name, Alias = "display-bill-" + i,
                    CategoryId = original.Product.CategoryId, SupplierId = original.Product.SupplierId,
                    BaseUnitId = original.Product.BaseUnitId, BasePrice = 12000 + i * 1000, IsActive = true };
                displayDb.Add(product); await displayDb.SaveChangesAsync();
                var variant = new ProductVariant { StoreId = displayStoreId, ProductId = product.Id,
                    Sku = "DISPLAY-BILL-" + i, ProductVariantName = name,
                    Price = 12000 + i * 1000, CostPrice = 10, IsActive = true };
                displayDb.Add(variant); await displayDb.SaveChangesAsync();
                displayReceiptVariantIds.Add(variant.Id);
                await movementService.CreateAsync(new GaoApp.Application.Services.Inventory.InventoryMovementFactory()
                    .CreatePurchaseReceipt((int)Read(store, "WarehouseId"), variant.Id, 100, 10,
                        "DISPLAY-BILL-STOCK", 1000 + i, "Stock for disposable receipt test", 1));
            }
        }
        await displayDb.SaveChangesAsync();
    }
    var displayRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var displayStart = new ProcessStartInfo("node") { WorkingDirectory = displayRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    displayStart.Environment["NODE_PATH"] = Path.Combine(displayRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    displayStart.ArgumentList.Add(Path.Combine(displayRoot, "GaoApp.Tests.Browser", args.Contains("--customer-display-receipt")
        ? "customer-display-receipt.browser.cjs" : args.Contains("--customer-payment-display")
            ? "customer-payment-display.browser.cjs" : "customer-display.browser.cjs"));
    using var probe = Process.Start(displayStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), storeId = Read(store, "StoreId"), terminalId = Read(store, "TerminalId"),
        variantId = Read(store, "VariantId"), receiptVariantIds = displayReceiptVariantIds, customerIds = displayCustomerIds }));
    probe.StandardInput.Close();
    using var displayTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await probe.WaitForExitAsync(displayTimeout.Token); }
    catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Customer display browser probe failed.");
    Console.WriteLine("PASS: customer display with real Razor/SignalR and controlled display data; disposable SQL only.");
    return;
}
if (args.Contains("--label-progress") || args.Contains("--label-barcodes"))
{
    var receiptId = await Call(null, typeof(ProductLabelSqlServerTests), args.Contains("--label-barcodes") ? "SeedBarcodeReceipt" : "SeedProgressReceipt", fixture, store);
    var labelRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var labelProgressStart = new ProcessStartInfo("node") { WorkingDirectory = labelRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    labelProgressStart.Environment["NODE_PATH"] = Path.Combine(labelRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    labelProgressStart.ArgumentList.Add(Path.Combine(labelRoot, "GaoApp.Tests.Browser", args.Contains("--label-barcodes") ? "label-barcodes.browser.cjs" : "label-progress.browser.cjs"));
    using var probe = Process.Start(labelProgressStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"), receiptId }));
    probe.StandardInput.Close();
    using var labelProgressTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    var labelProgressDatabase = Read(fixture, "Database");
    // Real SQL dispatcher with a transport that only validates command bytes; no Windows printer is called.
    var worker = Task.Run(async () =>
    {
        while (!labelProgressTimeout.IsCancellationRequested)
        {
            await using var db = (AppDbContext)labelProgressDatabase.GetType().GetMethod("CreateTenantContext", flags)!.Invoke(labelProgressDatabase, new object?[] { Read(store, "StoreId"), null })!;
            var printers = await db.Set<ProductLabelPrinter>().Select(x => x.Id).ToListAsync();
            foreach (var printer in printers) await new GaoApp.Infrastructure.Printing.LabelPrintDispatcher(db, new ProbeLabelTransport()).DispatchAsync(printer, default);
            await Task.Delay(500, labelProgressTimeout.Token);
        }
    });
    try { await probe.WaitForExitAsync(labelProgressTimeout.Token); }
    finally { labelProgressTimeout.Cancel(); if (!probe.HasExited) probe.Kill(true); try { await worker; } catch (OperationCanceledException) { } }
    if (probe.ExitCode != 0) throw new Exception("Label progress browser assertions failed.");
    Console.WriteLine("LABEL PRODUCT PROGRESS BROWSER PASS — disposable SQL and fake transport only.");
    return;
}
if (args.Contains("--label-printing"))
{
    var labelEmployee = await Call(fixture, type, "AddAccountAsync", store, new[] { PermissionCodes.System.ProductLabel.Print });
    var receiptId = await Call(null, typeof(ProductLabelSqlServerTests), "SeedReceipt", fixture, store);
    var labelRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var labelStart = new ProcessStartInfo("node") { WorkingDirectory = labelRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    labelStart.Environment["NODE_PATH"] = Path.Combine(labelRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    labelStart.ArgumentList.Add(Path.Combine(labelRoot, "GaoApp.Tests.Browser", "label-printing.browser.cjs"));
    using var probe = Process.Start(labelStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"), receiptId,
        employee = new { user = Read(labelEmployee, "Name"), password = Read(labelEmployee, "Password") } }));
    probe.StandardInput.Close();
    using var labelTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await probe.WaitForExitAsync(labelTimeout.Token); }
    catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Label printing browser probe failed.");
    Console.WriteLine("PASS: Chrome label workflow; no physical printer used.");
    return;
}
if (args.Contains("--pos-after-sales-mobile"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosAfterSalesMobileBrowserFixture")!;
    var seed = await Call(null, seam, "PrepareAsync", fixture, store, account);
    var before = (string)await Call(null, seam, "FingerprintAsync", fixture, store);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var output = Path.Combine(source,"TestResults","mobile-ui-08-20261026","browser"); Directory.CreateDirectory(output);
    object LoginData(object value) => new {user=Read(value,"Name"),password=Read(value,"Password"),terminalId=Read(Read(value,"Store"),"TerminalId"),baseUrl=$"http://{Read(Read(value,"Store"),"Host")}:{((Uri)Read(fixture,"Address")).Port}"};
    var afterSalesStart = new ProcessStartInfo("node") {WorkingDirectory=source,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true};
    afterSalesStart.Environment["NODE_PATH"]=Path.Combine(source,"Logs","pos-offline-browser-deps","node_modules");
    afterSalesStart.ArgumentList.Add(Path.Combine(source,"GaoApp.Tests.Browser","pos-after-sales.mobile.browser.cjs"));
    using var probe=Process.Start(afterSalesStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {manager=LoginData(account),readOnly=LoginData(Read(seed,"ReadOnly")),denied=LoginData(Read(seed,"Denied")),other=LoginData(Read(seed,"Other")),
        readyReturnId=Read(seed,"ReadyReturnId"),blockedReturnId=Read(seed,"BlockedReturnId"),phoneOrderId=Read(seed,"PhoneOrderId"),staleOrderId=Read(seed,"StaleOrderId"),shiftOrderId=Read(seed,"ShiftOrderId"),warehouseId=Read(store,"WarehouseId"),output}));
    probe.StandardInput.Close();using var afterSalesTimeout=new CancellationTokenSource(TimeSpan.FromMinutes(12));
    try {await probe.WaitForExitAsync(afterSalesTimeout.Token);} catch {if(!probe.HasExited)probe.Kill(true);throw;}
    if(probe.ExitCode!=0)throw new InvalidOperationException("POS after-sales mobile browser failed.");
    if(before!=(string)await Call(null,seam,"FingerprintAsync",fixture,store))throw new InvalidOperationException("After-sales UI changed original source payments/archive/configuration.");
    var verified=await Call(null,seam,"VerifyAsync",fixture,store);
    await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(verified,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("POS AFTER SALES MOBILE SQL PASS: stock 96, five returns/refunds, one restock audit, one blocked fragment; stale/duplicate/shift checks and source/configuration preserved.");return;
}
if (args.Contains("--pos-requests-mobile"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosRequestsMobileBrowserFixture")!;
    var seed = await Call(null, seam, "PrepareAsync", fixture, store, account);
    var before = (string)await Call(null, seam, "FingerprintAsync", fixture, store);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var output = Path.Combine(source, "TestResults", "mobile-ui-08a-20261026", "browser");
    Directory.CreateDirectory(output);
    object LoginData(object value) => new { user = Read(value, "Name"), password = Read(value, "Password"),
        terminalId = Read(Read(value, "Store"), "TerminalId"),
        baseUrl = $"http://{Read(Read(value, "Store"), "Host")}:{((Uri)Read(fixture, "Address")).Port}" };
    var startInfo = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    startInfo.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    startInfo.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "pos-requests.mobile.browser.cjs"));
    using var probe = Process.Start(startInfo)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {cashier=LoginData(account),admin=LoginData(Read(seed,"Admin")),peer=LoginData(Read(seed,"Peer")),
        denied=LoginData(Read(seed,"Denied")),readOnly=LoginData(Read(seed,"ReadOnly")),otherStore=LoginData(Read(seed,"OtherStore")),shiftId=Read(seed,"ShiftId"),transactionId=Read(seed,"TransactionId"),
        orderId=Read(seed,"OrderId"),cashPaymentId=Read(seed,"CashPaymentId"),bankPaymentId=Read(seed,"BankPaymentId"),depositEntryId=Read(seed,"DepositEntryId"),output }));
    probe.StandardInput.Close();
    using var requestsPhoneTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
    try { await probe.WaitForExitAsync(requestsPhoneTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("POS phone requests browser assertions failed.");
    if (before != (string)await Call(null, seam, "FingerprintAsync", fixture, store))
        throw new InvalidOperationException("Requests UI changed original amounts/counts/receipt/slip/stock/deposit balance/history/menu/store configuration.");
    var verified = await Call(null, seam, "VerifyAsync", fixture, store);
    await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(verified,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("POS REQUESTS PHONE SQL PASS: cash/payment/deposit approvals once, one withdrawal/rejection, reconciled expected 195,000; original count 259,000/receipt 258,000/closing evidence and configuration unchanged.");
    return;
}
if (args.Contains("--pos-dashboard-mobile"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosDashboardMobileBrowserFixture")!;
    var seed = await Call(null, seam, "PrepareAsync", fixture, store, account);
    var before = (string)await Call(null, seam, "FingerprintAsync", fixture, store);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var output = Path.Combine(source, "TestResults", "mobile-ui-07d-20261026", "browser");
    Directory.CreateDirectory(output);
    object LoginData(object value) => new { user = Read(value, "Name"), password = Read(value, "Password"),
        terminalId = Read(Read(value, "Store"), "TerminalId"),
        baseUrl = $"http://{Read(Read(value, "Store"), "Host")}:{((Uri)Read(fixture, "Address")).Port}" };
    var startInfo = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    startInfo.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    startInfo.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "pos-dashboard.mobile.browser.cjs"));
    using var probe = Process.Start(startInfo)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { manager=LoginData(account),otherCashier=LoginData(Read(seed,"OtherCashier")),
        denied=LoginData(Read(seed,"Denied")),noShiftView=LoginData(Read(seed,"NoShiftView")),otherStore=LoginData(Read(seed,"OtherStore")),
        shiftId=Read(seed,"ShiftId"),historicalShiftId=Read(seed,"HistoricalShiftId"),foreignCashierShiftId=Read(seed,"ForeignCashierShiftId"),output }));
    probe.StandardInput.Close();
    using var dashboardPhoneTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    try { await probe.WaitForExitAsync(dashboardPhoneTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("POS phone dashboard browser assertions failed.");
    if (before != (string)await Call(null, seam, "FingerprintAsync", fixture, store))
        throw new InvalidOperationException("Read-only dashboard changed shifts/orders/payments/cash/stock/slips/menu/store configuration.");
    var verified = await Call(null, seam, "VerifyAsync", fixture, store);
    await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(verified,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("POS DASHBOARD PHONE SQL PASS: only current shift, completed sales 120,000 versus cash/noncash buckets 200,000, expected 340,000; all business/configuration unchanged.");
    return;
}
if (args.Contains("--pos-shift-review-mobile"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosShiftReviewMobileBrowserFixture")!;
    var seed = await Call(null, seam, "PrepareAsync", fixture, store, account);
    var before = (string)await Call(null, seam, "FingerprintAsync", fixture, store);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var output = Path.Combine(source, "TestResults", "mobile-ui-07c-20261026", "browser");
    Directory.CreateDirectory(output);
    object LoginData(object value) => new { user = Read(value, "Name"), password = Read(value, "Password"),
        terminalId = Read(Read(value, "Store"), "TerminalId"),
        baseUrl = $"http://{Read(Read(value, "Store"), "Host")}:{((Uri)Read(fixture, "Address")).Port}" };
    var startInfo = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    startInfo.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    startInfo.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "pos-shift-review.mobile.browser.cjs"));
    using var probe = Process.Start(startInfo)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { manager=LoginData(account),cashier=LoginData(Read(seed,"Cashier")),
        denied=LoginData(Read(seed,"Denied")),other=LoginData(Read(seed,"Other")),shiftId=Read(seed,"ShiftId"),secondShiftId=Read(seed,"SecondShiftId"),
        receivedShiftId=Read(seed,"ReceivedShiftId"),openShiftId=Read(seed,"OpenShiftId"),output }));
    probe.StandardInput.Close();
    using var shiftReviewTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    try { await probe.WaitForExitAsync(shiftReviewTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("POS phone reconciliation/approval browser assertions failed.");
    if (before != (string)await Call(null, seam, "FingerprintAsync", fixture, store))
        throw new InvalidOperationException("Reconciliation/approval changed original cash, orders, payments, stock, other receipts, menu or store configuration.");
    var verified = await Call(null, seam, "VerifyAsync", fixture, store, account);
    await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(verified,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("POS SHIFT REVIEW SQL PASS: immutable approval 224,000; employee count 225,000, expected 226,000; all other business/configuration unchanged.");
    return;
}
if (args.Contains("--pos-shift-records-mobile"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosShiftRecordsMobileBrowserFixture")!;
    var seed = await Call(null, seam, "PrepareAsync", fixture, store, account);
    var before = (string)await Call(null, seam, "FingerprintAsync", fixture, store);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var output = Path.Combine(source, "TestResults", "mobile-ui-07b-20261026", "browser");
    Directory.CreateDirectory(output);
    object LoginData(object value) => new { user = Read(value, "Name"), password = Read(value, "Password"),
        terminalId = Read(Read(value, "Store"), "TerminalId"),
        baseUrl = $"http://{Read(Read(value, "Store"), "Host")}:{((Uri)Read(fixture, "Address")).Port}" };
    var startInfo = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    startInfo.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    startInfo.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "pos-shift-records.mobile.browser.cjs"));
    using var probe = Process.Start(startInfo)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { manager = LoginData(account), cashier = LoginData(Read(seed,"Cashier")),
        denied = LoginData(Read(seed,"Denied")), other = LoginData(Read(seed,"Other")), firstShiftId=Read(seed,"FirstShiftId"),
        secondShiftId=Read(seed,"SecondShiftId"), otherCashierShiftId=Read(seed,"OtherCashierShiftId"), secondTerminalId=Read(seed,"SecondTerminalId"),
        warehouseId = Read(store,"WarehouseId"), output }));
    probe.StandardInput.Close();
    using var shiftRecordsTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    try { await probe.WaitForExitAsync(shiftRecordsTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("POS phone history/handover browser assertions failed.");
    if (before != (string)await Call(null, seam, "FingerprintAsync", fixture, store))
        throw new InvalidOperationException("Phone records UI changed original history/orders/cash/seed slips/store/menu or another store.");
    var verified = await Call(null, seam, "VerifyAsync", fixture, store);
    await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(verified,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("POS SHIFT RECORDS SQL PASS: two intended slips, one revised/received at 350,000, one cancelled; original history/store/menu unchanged.");
    return;
}
if (args.Contains("--pos-shift-mobile"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosShiftMobileBrowserFixture")!;
    var seed = await Call(null, seam, "PrepareAsync", fixture, store);
    var before = (string)await Call(null, seam, "FingerprintAsync", fixture, store);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var output = Path.Combine(source, "TestResults", "mobile-ui-07a-20261026", "browser");
    Directory.CreateDirectory(output);
    object LoginData(object value) => new { user = Read(value, "Name"), password = Read(value, "Password"),
        terminalId = Read(Read(value, "Store"), "TerminalId"),
        baseUrl = $"http://{Read(Read(value, "Store"), "Host")}:{((Uri)Read(fixture, "Address")).Port}" };
    var startInfo = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    startInfo.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    startInfo.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "pos-shift.mobile.browser.cjs"));
    using var probe = Process.Start(startInfo)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { manager = LoginData(account),
        limited = LoginData(Read(seed, "Limited")), other = LoginData(Read(seed, "Other")), warehouseId = Read(store, "WarehouseId"), output }));
    probe.StandardInput.Close();
    using var shiftPhoneTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
    try { await probe.WaitForExitAsync(shiftPhoneTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("POS phone shift browser assertions failed.");
    if (before != (string)await Call(null, seam, "FingerprintAsync", fixture, store))
        throw new InvalidOperationException("Shift UI changed configured menu/store/prices/customer preferences or another store.");
    var verified = await Call(null, seam, "VerifyAsync", fixture, store);
    await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(verified, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("POS SHIFT PHONE SQL PASS: one opening/closing, exact cash/difference/denominations; store/menu unchanged.");
    return;
}
if (args.Contains("--pos-receive-shift"))
{
    var shiftRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var shiftStart = new ProcessStartInfo("node") { WorkingDirectory = shiftRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    shiftStart.ArgumentList.Add(Path.Combine(shiftRoot, "GaoApp.Tests.Browser", "pos-receive-shift.browser.cjs"));
    using var probe = Process.Start(shiftStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"), warehouseId = Read(store, "WarehouseId") }));
    probe.StandardInput.Close();
    using var shiftTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    try { await probe.WaitForExitAsync(shiftTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("POS receive-shift browser assertions failed.");
    Console.WriteLine("PASS: POS shift reception flow, actual UI and disposable SQL only.");
    return;
}
(await http.PostAsJsonAsync("/admin/pos/shift/open", new { openingCash = 0, warehouseId = Read(store, "WarehouseId") })).EnsureSuccessStatusCode();
var database = Read(fixture, "Database");
await using var db = (AppDbContext)database.GetType().GetMethod("CreateTenantContext", flags)!.Invoke(database, new object?[] { Read(store, "StoreId"), null })!;
var bank = new StoreBankAccount { StoreId = (int)Read(store, "StoreId"), AccountName = "OFFLINE TEST", AccountNumber = "123456789", BankCode = "ACB", BankName = "TEST", VietQrBankBin = "970416", IsDefault = true };
if (args.Contains("--pos-editors-single-owner"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosEditorSingleOwnerBrowserFixture")!;
    var seed = await Call(null, seam, "PrepareAsync", fixture, store, account);
    var before = (string)await Call(null, seam, "FingerprintAsync", fixture, store);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var output = Path.Combine(source, "TestResults", "mobile-ui-06f-20261026", "browser");
    Directory.CreateDirectory(output);
    object LoginData(object value) => new { user = Read(value, "Name"), password = Read(value, "Password"),
        terminalId = Read(Read(value, "Store"), "TerminalId"),
        baseUrl = $"http://{Read(Read(value, "Store"), "Host")}:{((Uri)Read(fixture, "Address")).Port}" };
    var startInfo = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    startInfo.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    startInfo.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "pos-editors-single-owner.browser.cjs"));
    using var probe = Process.Start(startInfo)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { manager = LoginData(account),
        limited = LoginData(Read(seed, "Limited")), denied = LoginData(Read(seed, "Denied")), other = LoginData(Read(seed, "Other")),
        orderId = Read(seed, "OrderId"), output }));
    probe.StandardInput.Close();
    using var editorsTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
    try { await probe.WaitForExitAsync(editorsTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("POS single-owner editor browser assertions failed.");
    if (before != (string)await Call(null, seam, "FingerprintAsync", fixture, store))
        throw new InvalidOperationException("Shared editor refactor changed store/menu/prices/customer preferences or another store.");
    var verified = await Call(null, seam, "VerifyAsync", fixture, store, Read(seed, "OrderId"));
    await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(verified, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("POS EDITORS SQL PASS: intended note/discounts once, one payment/completed sale, exact stock/cash; store/menu unchanged.");
    return;
}
if (args.Contains("--pos-session-mobile"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosSessionMobileBrowserFixture")!;
    var seed = await Call(null, seam, "PrepareAsync", fixture, store, account);
    var before = (string)await Call(null, seam, "FingerprintAsync", fixture, store);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var output = Path.Combine(source, "TestResults", "mobile-ui-06e-20261026", "browser");
    Directory.CreateDirectory(output);
    object LoginData(object value) => new { user = Read(value, "Name"), password = Read(value, "Password"),
        terminalId = Read(Read(value, "Store"), "TerminalId"),
        baseUrl = $"http://{Read(Read(value, "Store"), "Host")}:{((Uri)Read(fixture, "Address")).Port}" };
    var startInfo = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    startInfo.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    startInfo.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "pos-session.mobile.browser.cjs"));
    using var probe = Process.Start(startInfo)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { manager = LoginData(account),
        limited = LoginData(Read(seed, "Limited")), denied = LoginData(Read(seed, "Denied")), other = LoginData(Read(seed, "Other")),
        orderId = Read(seed, "OrderId"), storeId = Read(store, "StoreId"), output }));
    probe.StandardInput.Close();
    using var sessionTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
    try { await probe.WaitForExitAsync(sessionTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("POS phone session/delivery browser assertions failed.");
    if (before != (string)await Call(null, seam, "FingerprintAsync", fixture, store))
        throw new InvalidOperationException("Session UI changed configured menu/store/prices/customer preferences or another store.");
    var verified = await Call(null, seam, "VerifyAsync", fixture, store, Read(seed, "OrderId"));
    await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(verified, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("POS SESSION SQL PASS: one delivery/revision/audit, no payment/completed sale/stock change; store/menu unchanged.");
    return;
}
if (args.Contains("--pos-transfer-mobile"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosTransferMobileBrowserFixture")!;
    var seed = await Call(null, seam, "PrepareAsync", fixture, store, account);
    var before = (string)await Call(null, seam, "FingerprintAsync", fixture, store);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var output = Path.Combine(source, "TestResults", "mobile-ui-06d-20261026", "browser");
    Directory.CreateDirectory(output);
    object LoginData(object value) => new { user = Read(value, "Name"), password = Read(value, "Password"),
        terminalId = Read(Read(value, "Store"), "TerminalId"),
        baseUrl = $"http://{Read(Read(value, "Store"), "Host")}:{((Uri)Read(fixture, "Address")).Port}" };
    var startInfo = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    startInfo.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    startInfo.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "pos-transfer.mobile.browser.cjs"));
    using var probe = Process.Start(startInfo)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { manager = LoginData(account),
        limited = LoginData(Read(seed, "Limited")), denied = LoginData(Read(seed, "Denied")), other = LoginData(Read(seed, "Other")),
        orderId = Read(seed, "OrderId"), qrId = Read(seed, "QrId"), output }));
    probe.StandardInput.Close();
    using var transferTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(7));
    try { await probe.WaitForExitAsync(transferTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("POS phone transfer browser assertions failed.");
    if (before != (string)await Call(null, seam, "FingerprintAsync", fixture, store))
        throw new InvalidOperationException("Transfer UI changed configured store/menu/prices/customer preferences or another store.");
    var verified = await Call(null, seam, "VerifyAsync", fixture, store, Read(seed, "OrderId"));
    await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(verified, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("POS TRANSFER SQL PASS: one confirmed installment; cancelled/pending QR collect nothing; store/menu unchanged.");
    return;
}
if (args.Contains("--pos-checkout-mobile"))
{
    var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosCheckoutMobileBrowserFixture")!;
    var seed = await Call(null, seam, "PrepareAsync", fixture, store, account);
    var before = (string)await Call(null, seam, "FingerprintAsync", fixture, store);
    var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var output = Path.Combine(source, "TestResults", "mobile-ui-06c-20261026", "browser");
    Directory.CreateDirectory(output);
    object LoginData(object value) => new { user = Read(value, "Name"), password = Read(value, "Password"),
        terminalId = Read(Read(value, "Store"), "TerminalId"),
        baseUrl = $"http://{Read(Read(value, "Store"), "Host")}:{((Uri)Read(fixture, "Address")).Port}" };
    var startInfo = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    startInfo.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
    startInfo.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", "pos-checkout.mobile.browser.cjs"));
    using var probe = Process.Start(startInfo)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { manager = LoginData(account),
        limited = LoginData(Read(seed, "Limited")), denied = LoginData(Read(seed, "Denied")), other = LoginData(Read(seed, "Other")),
        orderId = Read(seed, "OrderId"), output }));
    probe.StandardInput.Close();
    using var checkoutMobileTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(7));
    try { await probe.WaitForExitAsync(checkoutMobileTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new InvalidOperationException("POS phone checkout browser assertions failed.");
    if (before != (string)await Call(null, seam, "FingerprintAsync", fixture, store))
        throw new InvalidOperationException("Payment UI changed configured store/menu/prices/customer preferences or another store.");
    var verified = await Call(null, seam, "VerifyAsync", fixture, store, Read(seed, "OrderId"));
    await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(verified, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("POS CHECKOUT SQL PASS: three unique installments, one completed sale, correct stock/change/cash totals; configured store/menu unchanged.");
    return;
}
if (args.Contains("--pos-scan-feedback") || args.Contains("--pos-entry") || args.Contains("--pos-camera") || args.Contains("--pos-cart"))
{
    var scanVariantId = (int)Read(store, "VariantId");
    var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == scanVariantId);
    variant.ProductVariantName = "Bia Ken bạc 330 ml";
    var scanPhoto = new MediaAsset { StoreId = variant.StoreId, StoragePath = "uploads/products/scan-test.png", ContentType = "image/png", OriginalFileName = "scan-test.png" };
    db.Add(scanPhoto); await db.SaveChangesAsync();
    var scanImage = new ProductImage { StoreId = variant.StoreId, ProductId = variant.ProductId, MediaAssetId = scanPhoto.Id, AltText = "Bia Ken bạc", IsPrimary = true };
    db.Add(scanImage); await db.SaveChangesAsync(); variant.PrimaryProductImageId = scanImage.Id;
    var unit = await db.Units.SingleAsync(x => x.Id == variant.Product.BaseUnitId);
    unit.Name = "Lon";
    var packUnit = new Unit { StoreId = variant.StoreId, Name = "Lốc", Code = "PACK-TEST" }; db.Add(packUnit); await db.SaveChangesAsync();
    var single = new ProductUnitConversion { StoreId = variant.StoreId, ProductVariantId = variant.Id, UnitId = unit.Id, Factor = 1, IsBaseUnit = true, IsDefaultForSale = true, Price = 21000 };
    var pack = new ProductUnitConversion { StoreId = variant.StoreId, ProductVariantId = variant.Id, UnitId = packUnit.Id, Factor = 6, Price = 120000 };
    db.AddRange(single, pack); await db.SaveChangesAsync();
    db.AddRange(new ProductVariantUnitBarcode { StoreId = variant.StoreId, ProductUnitConversionId = single.Id, Barcode = "2099900000001", IsActive = true },
        new ProductVariantUnitBarcode { StoreId = variant.StoreId, ProductUnitConversionId = pack.Id, Barcode = "2099900000002", IsActive = true });
    var second = new ProductVariant { StoreId = variant.StoreId, ProductId = variant.ProductId, ProductVariantName = "Nước suối tinh khiết 500 ml", Sku = "WATER-TEST", Price = 7000, IsActive = true };
    db.Add(second); await db.SaveChangesAsync();
    var waterPhoto = new MediaAsset { StoreId = variant.StoreId, StoragePath = "uploads/products/scan-water-test.png", ContentType = "image/png", OriginalFileName = "scan-water-test.png" };
    db.Add(waterPhoto); await db.SaveChangesAsync();
    var waterImage = new ProductImage { StoreId = variant.StoreId, ProductId = variant.ProductId, MediaAssetId = waterPhoto.Id, AltText = "Nước suối" };
    db.Add(waterImage); await db.SaveChangesAsync(); second.PrimaryProductImageId = waterImage.Id;
    var secondUnit = new ProductUnitConversion { StoreId = variant.StoreId, ProductVariantId = second.Id, UnitId = unit.Id, Factor = 1, IsBaseUnit = true, IsDefaultForSale = true, Price = 7000 };
    db.Add(secondUnit); await db.SaveChangesAsync();
    db.Add(new ProductVariantUnitBarcode { StoreId = variant.StoreId, ProductUnitConversionId = secondUnit.Id, Barcode = "2099900000003", IsActive = true });
    db.Add(new InventoryBalance { StoreId = variant.StoreId, WarehouseId = (int)Read(store, "WarehouseId"), ProductVariantId = second.Id, OnHandQty = 100 });
    await db.SaveChangesAsync();
    if (args.Contains("--pos-entry") || args.Contains("--pos-camera") || args.Contains("--pos-cart"))
    {
        var seam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosEntryBrowserFixture")!;
        var seed = await Call(null, seam, "PrepareAsync", fixture, store, account);
        var cart = args.Contains("--pos-cart");
        var cartSeam = typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosCartMobileBrowserFixture")!;
        var cartSeed = cart ? await Call(null, cartSeam, "PrepareAsync", fixture, store, account, Read(seed, "OrderId"), pack.Id) : null;
        var before = (string)await Call(null, seam, "FingerprintAsync", fixture, store);
        var source = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
        var camera = args.Contains("--pos-camera");
        var output = cart ? Path.Combine(source, "TestResults", "mobile-ui-06b-20261025", "browser")
            : camera ? Environment.GetEnvironmentVariable("GAO_POS_CAMERA_OUTPUT") ?? Path.Combine(source, "TestResults", "mobile-ui-06a-camera-20261025", "browser")
            : Environment.GetEnvironmentVariable("GAO_POS_ENTRY_OUTPUT") ?? Path.Combine(source, "TestResults", "mobile-ui-06a-20261025", "browser");
        Directory.CreateDirectory(output);
        object LoginData(object value) => new { user = Read(value, "Name"), password = Read(value, "Password"),
            terminalId = Read(Read(value, "Store"), "TerminalId"),
            baseUrl = $"http://{Read(Read(value, "Store"), "Host")}:{((Uri)Read(fixture, "Address")).Port}" };
        var entryStart = new ProcessStartInfo("node") { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
        entryStart.Environment["NODE_PATH"] = Path.Combine(source, "Logs", "pos-offline-browser-deps", "node_modules");
        entryStart.ArgumentList.Add(Path.Combine(source, "GaoApp.Tests.Browser", cart ? "pos-cart.mobile.browser.cjs" : camera ? "pos-camera.mobile.browser.cjs" : "pos-entry.mobile.browser.cjs"));
        using var entryProbe = Process.Start(entryStart)!;
        await entryProbe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
            manager = LoginData(account), denied = LoginData(Read(seed, "Denied")), closed = LoginData(Read(seed, "Closed")),
            orderId = Read(seed, "OrderId"), variantId = variant.Id, packId = pack.Id, waterId = second.Id, cartSeed, output
        }));
        entryProbe.StandardInput.Close();
        using var entryTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(7));
        try { await entryProbe.WaitForExitAsync(entryTimeout.Token); } catch { if (!entryProbe.HasExited) entryProbe.Kill(true); throw; }
        if (entryProbe.ExitCode != 0) throw new InvalidOperationException("POS entry phone browser assertions failed.");
        if (before != (string)await Call(null, seam, "FingerprintAsync", fixture, store))
            throw new InvalidOperationException("Phone presentation changed store menus, stock, payments, pricing, shifts or another store.");
        var verifySeam = cart ? cartSeam : camera ? typeof(PosOfflineSqlServerTests).Assembly.GetType("GaoApp.Tests.Security.PosCameraBrowserFixture")! : seam;
        var verification = await Call(null, verifySeam, "VerifyAsync", fixture, store, Read(seed, "OrderId"));
        await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(verification, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("POS ENTRY SQL PASS: same draft, exact quantities; configured menu, stock, prices, payments, shifts and other store unchanged.");
        return;
    }
    var scanRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var scanStart = new ProcessStartInfo("node") { WorkingDirectory = scanRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    scanStart.Environment["NODE_PATH"] = Path.Combine(scanRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    scanStart.ArgumentList.Add(Path.Combine(scanRoot, "GaoApp.Tests.Browser", "pos-scan-feedback.browser.cjs"));
    using var probe = Process.Start(scanStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"), variantId = variant.Id, packId = pack.Id }));
    probe.StandardInput.Close();
    using var scanTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await probe.WaitForExitAsync(scanTimeout.Token); } catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("POS scan feedback browser assertions failed.");
    Console.WriteLine("PASS: POS scan feedback, actual UI and disposable SQL only.");
    return;
}
db.Add(bank); await db.SaveChangesAsync();
var root = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "pos-offline.browser.cjs"));
object? receiptCashier = null;
if (args.Contains("--receipt-templates")) {
    start.ArgumentList.Add("--receipt-templates");
    receiptCashier = await Call(fixture, type, "AddAccountAsync", store, new[] { PermissionCodes.Pos.Order.View, PermissionCodes.Pos.Order.Reprint });
}
using var node = Process.Start(start)!;
await node.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
    baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
    user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"),
    variantId = Read(store, "VariantId"), bankId = bank.Id,
    cashierUser = receiptCashier == null ? null : Read(receiptCashier, "Name"), cashierPassword = receiptCashier == null ? null : Read(receiptCashier, "Password")
}));
node.StandardInput.Close();
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
try { await node.WaitForExitAsync(timeout.Token); }
catch { if (!node.HasExited) node.Kill(true); throw; }
if (node.ExitCode != 0) throw new Exception("Browser probe failed; see its output and TestResults/pos-offline/browser.");
db.ChangeTracker.Clear();
var completed = await db.Orders.Where(x => x.Status == OrderStatus.Completed).ToListAsync();
if (completed.Count != 2 || completed.Sum(x => x.GrandTotal) != 100 || await db.OrderPayments.CountAsync() != 2)
    throw new Exception("Browser sales did not reconcile to exactly two orders and two payments.");
if ((await db.InventoryBalances.SingleAsync()).OnHandQty != 95) throw new Exception("Browser sales posted the wrong stock movement.");
Console.WriteLine("PASS: browser sales match SQL: 2 orders, 2 receipts, total 100, stock 95.");

sealed class ProbeLabelTransport : GaoApp.Infrastructure.Printing.ILabelPrintTransport
{
    public int Send(string printerName, string documentName, IEnumerable<byte[]> commands)
    {
        if (!commands.Any()) throw new InvalidOperationException("Empty label commands.");
        return 123;
    }
}
