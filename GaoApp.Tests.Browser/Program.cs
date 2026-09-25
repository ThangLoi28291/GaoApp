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
var fixture = await Call(null, type, "StartAsync");
await using var cleanup = (IAsyncDisposable)fixture;
var store = ((IEnumerable)Read(fixture, "Stores")).Cast<object>().First();
var account = await Call(fixture, type, "AddAccountAsync", store, new[] { "*" });
var client = await Call(fixture, type, "LoginAsync", account);
using var clientCleanup = (IDisposable)client;
var http = (HttpClient)Read(client, "Http");
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
if (args.Contains("--receipt-templates")) start.ArgumentList.Add("--receipt-templates");
using var node = Process.Start(start)!;
await node.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
    baseUrl = $"http://{Read(store, "Host")}:{((Uri)Read(fixture, "Address")).Port}",
    user = Read(account, "Name"), password = Read(account, "Password"), terminalId = Read(store, "TerminalId"),
    variantId = Read(store, "VariantId"), bankId = bank.Id
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
