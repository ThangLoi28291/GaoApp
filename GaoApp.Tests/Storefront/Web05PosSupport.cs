using System.Reflection;
using System.Text.Json;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Web.Common.POS;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Storefront;

// A real, persisted POS graph used only to exercise existing POS payment paths.
// Web placement/processing never receives this runtime or creates these rows.
internal sealed class Web05PosCase : IAsyncDisposable
{
    internal required Web05Case Web;
    internal required AppDbContext Db;
    internal required AcbPaymentService Payments;
    internal required AcbProtocol Protocol;
    internal required Web05PosRuntime Runtime;
    internal required IPosRealtimeNotifier Notifier;
    internal required Order Order;
    internal required int BankAccountId;
    internal required Web05BankTransport Bank;
    internal int QrId; internal string Reference = "";
    internal static async Task<Web05PosCase> CreateAsync(Web05Case web, bool manual = false, IInterceptor? interceptor = null)
    {
        var db = web.Seed.Database.CreateTenantContext(web.StoreId, interceptor);
        try
        {
            var terminal = new POSTerminal { StoreId = web.StoreId, Code = "W05-" + Guid.NewGuid().ToString("N")[..12], Name = "Synthetic POS terminal" }; db.Add(terminal); await db.SaveChangesAsync();
            var shift = new POSShift { StoreId = web.StoreId, TerminalId = terminal.Id, WarehouseId = web.Seed.WarehouseId, OpenedByUserId = web.Seed.UserId, Status = POSShiftStatus.Open }; db.Add(shift); await db.SaveChangesAsync();
            var variant = new ProductVariant { StoreId = web.StoreId, ProductId = web.Seed.ProductId, Sku = "W05-POS-" + Guid.NewGuid().ToString("N"), IsActive = true, HasInputInvoice = !manual, Price = 100_000m };
            db.Add(variant); await db.SaveChangesAsync();
            var order = new Order { StoreId = web.StoreId, POSShiftId = shift.Id, Status = OrderStatus.Draft, GrandTotal = 100_000m, Subtotal = 100_000m, BalanceDue = 100_000m };
            order.Lines.Add(new() { StoreId = web.StoreId, ProductId = web.Seed.ProductId, VariantId = variant.Id, Quantity = 1, BaseQuantity = 1, Multiplier = 1, UnitPrice = 100_000m,
                LineTotal = 100_000m, ItemName = "Synthetic POS item", SellingUnitId = web.Seed.UnitId, UnitName = "Synthetic unit" }); db.Add(order); await db.SaveChangesAsync();
            var physical = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.Id == web.Seed.BankAccountId); var bankId = physical.Id;
            if (manual)
            {
                var aliasNumber = physical.AccountNumber[..3] + " " + physical.AccountNumber[3..];
                Assert.NotEqual(physical.AccountNumber, aliasNumber);
                Assert.Equal(GlobalBankReceiptLedger.AccountIdentity(physical.BankCode, physical.AccountNumber), GlobalBankReceiptLedger.AccountIdentity(physical.BankCode, aliasNumber));
                var alias = new StoreBankAccount { StoreId = web.StoreId, BankCode = physical.BankCode, BankName = physical.BankName, AccountNumber = aliasNumber, AccountName = physical.AccountName,
                    IsActive = true, ConfirmMode = BankQrConfirmMode.Manual, QrRenderMode = BankQrRenderMode.LocalEmvQr }; db.Add(alias); await db.SaveChangesAsync(); bankId = alias.Id;
            }
            var bank = new Web05BankTransport(physical.AccountNumber, web.Web.Clock, pos: true) { TerminalOverride = terminal.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), UserOverride = web.Seed.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            var protocol = new AcbProtocol(new HttpClient(bank), Web05Support.AdminProtection(web.AdminKeys)); var runtime = new Web05PosRuntime(web.StoreId, terminal.Id, web.Seed.UserId);
            var notifier = DispatchProxy.Create<IPosRealtimeNotifier, Web05Proxy>(); ((Web05Proxy)(object)notifier).Call = (_, _) => Task.CompletedTask;
            var pos = DispatchProxy.Create<IPOSService, Web05Proxy>(); ((Web05Proxy)(object)pos).Call = (method, _) => throw new InvalidOperationException("Partial-transfer test unexpectedly called POS operation: " + method.Name);
            var service = new AcbPaymentService(db, protocol, pos, runtime, notifier, new SqlAcbOrderLockProvider());
            var result = new Web05PosCase { Web = web, Db = db, Payments = service, Protocol = protocol, Runtime = runtime, Notifier = notifier, Order = order, BankAccountId = bankId, Bank = bank };
            if (manual)
            {
                var request = new PosPaymentQrRequest { StoreId = web.StoreId, OrderId = order.Id, BankAccountId = bankId, Amount = 45_000m,
                    RequestCode = "N" + Guid.NewGuid().ToString("N")[..20], ConfirmMode = BankQrConfirmMode.Manual, QrRenderMode = BankQrRenderMode.LocalEmvQr,
                    Status = PosPaymentQrStatus.Pending, ExpireAtUtc = DateTime.UtcNow.AddMinutes(30), Content = "Synthetic manual QR" };
                db.Add(request); await db.SaveChangesAsync(); result.QrId = request.Id; result.Reference = request.RequestCode;
            }
            else
            {
                var qr = await service.TryCreateAsync(order.Id, new() { Amount = 45_000m, ClientRequestId = Guid.NewGuid() }, default); Assert.NotNull(qr); result.QrId = qr.Id; result.Reference = qr.RequestCode;
            }
            return result;
        }
        catch { await db.DisposeAsync(); throw; }
    }
    internal async Task RetrievePaid(string externalId)
    {
        Bank.Pay(Reference, 45_000m, externalId);
        await Payments.CallbackAsync(JsonSerializer.SerializeToElement(new { requestParameters = new { request = new { requestParams = new { transactions = new[] {
            new { transactionStatus = "COMPLETED", amount = 45_000m, transactionEntityAttribute = new { custom1 = "SYNTHETIC-W05", custom2 = Runtime.TerminalId!.Value.ToString(), custom4 = Reference } } } } } } }), default);
    }
    internal async Task<(BankNotificationBridgeService Bridge, int Receipt, int Wait)> ReceivePhone(string externalId, bool includeBankTime = true)
    {
        var bank = await Db.StoreBankAccounts.SingleAsync(x => x.Id == BankAccountId);
        var device = new BankNotificationDevice { StoreId = Web.StoreId, BankAccountId = BankAccountId, BankAccountFingerprint = BankNotificationParser.AccountFingerprint(bank), Name = "Synthetic trusted phone", TokenHash = new string('A', 64) };
        Db.Add(device); await Db.SaveChangesAsync(); var bridge = new BankNotificationBridgeService(Db, Runtime, new SqlAcbOrderLockProvider(), Payments, Notifier);
        Assert.True(await bridge.ArmAsync(QrId, default));
        var at = DateTimeOffset.UtcNow; var bankTime = includeBankTime ? at.ToOffset(TimeSpan.FromHours(7)).ToString("ddMMyy-HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) : "";
        var upload = new BankNotificationUpload("mobile.acb.com.vn", $"Nhận thành công +45000 VND từ KHACH THU tại ACB POS{Order.Id} GD {externalId} {bankTime}", at.ToUnixTimeMilliseconds());
        var receipt = await bridge.AcceptAsync(device, upload, default); Assert.NotNull(receipt);
        var wait = await Db.Set<BankNotificationWait>().SingleAsync(x => x.QrId == QrId); return (bridge, receipt.Value, wait.Id);
    }
    public async ValueTask DisposeAsync() => await Db.DisposeAsync();
}

internal sealed class Web05PosRuntime(int store, int terminal, int user) : IPOSRuntimeContextAccessor
{
    public int? StoreId => store; public int? TerminalId { get; set; } = terminal; public int? UserId => user;
    public string? StoreName => "Synthetic POS store"; public string? TerminalName => "Synthetic POS terminal"; public string? TerminalCode => "W05"; public string? UserName => "Synthetic cashier";
}
public class Web05Proxy : DispatchProxy
{
    internal Func<MethodInfo, object?[]?, object?> Call = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args);
}
