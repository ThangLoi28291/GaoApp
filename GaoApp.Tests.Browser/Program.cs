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
var fixture = await Call(null, type, args.Contains("--receipt-supplier") || args.Contains("--input-invoice-library") ? "StartWithInvoiceLibraryAsync" : "StartAsync");
await using var cleanup = (IAsyncDisposable)fixture;
var store = ((IEnumerable)Read(fixture, "Stores")).Cast<object>().First();
var account = args.Contains("--receipt-templates") || args.Contains("--kiosk")
    ? await Call(null, typeof(PosShiftAdministrationSqlServerTests), "AddAdminAsync", fixture, store)
    : await Call(fixture, type, "AddAccountAsync", store, new[] { "*" });
var client = await Call(fixture, type, "LoginAsync", account);
using var clientCleanup = (IDisposable)client;
var http = (HttpClient)Read(client, "Http");
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
        if (args.Contains("--receiving-mobile") && (Environment.GetEnvironmentVariable("RECEIVING_SCROLL_CHECK") == "1" || Environment.GetEnvironmentVariable("RECEIVING_INTERACTION_CHECK") == "1" || Environment.GetEnvironmentVariable("RECEIVING_FEEDBACK_RELOAD_CHECK") == "1"))
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
        employee = new { user = Read(employee, "Name"), password = Read(employee, "Password") }
    }));
    probe.StandardInput.Close();
    using var probeTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await probe.WaitForExitAsync(probeTimeout.Token); }
    catch { if (!probe.HasExited) probe.Kill(true); throw; }
    if (probe.ExitCode != 0) throw new Exception("Receipt barcode browser probe failed.");
    Console.WriteLine(args.Contains("--receipt-history") ? "PASS: Receipt history browser checks against isolated SQL."
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
if (args.Contains("--customer-display"))
{
    (await http.PostAsJsonAsync("/admin/pos/shift/open", new { openingCash = 0, warehouseId = Read(store, "WarehouseId") })).EnsureSuccessStatusCode();
    var displayDatabase = Read(fixture, "Database");
    await using (var displayDb = (AppDbContext)displayDatabase.GetType().GetMethod("CreateTenantContext", flags)!
        .Invoke(displayDatabase, new object?[] { Read(store, "StoreId"), null })!)
    {
        var displayStoreId = (int)Read(store, "StoreId");
        var displayStore = await displayDb.Stores.SingleAsync(x => x.Id == displayStoreId);
        displayStore.ReceiptName = "GẠO · MARKET";
        displayStore.ReceiptAddress = "128 Nguyễn Văn Cừ · TP. Hồ Chí Minh";
        var displayUserId = (int)Read(account, "UserId");
        (await displayDb.Users.SingleAsync(x => x.Id == displayUserId)).FullName = "Nguyễn Minh An";
        await displayDb.SaveChangesAsync();
    }
    var displayRoot = (string)type.GetMethod("SourceRoot", flags)!.Invoke(null, null)!;
    var displayStart = new ProcessStartInfo("node") { WorkingDirectory = displayRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    displayStart.Environment["NODE_PATH"] = Path.Combine(displayRoot, "Logs", "pos-offline-browser-deps", "node_modules");
    displayStart.ArgumentList.Add(Path.Combine(displayRoot, "GaoApp.Tests.Browser", "customer-display.browser.cjs"));
    using var probe = Process.Start(displayStart)!;
    await probe.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
        user = Read(account, "Name"), password = Read(account, "Password"), storeId = Read(store, "StoreId"), terminalId = Read(store, "TerminalId") }));
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
if (args.Contains("--pos-scan-feedback"))
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
