using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Services.POSPaymentQrs;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.POSPaymentQrs;
using GaoApp.Infrastructure.Repositories.StoreBankAccounts;
using GaoApp.Tests.Storefront;
using GaoApp.Web.Controllers;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    // Real SQL/HTTPS intake, without application bootstrap, bank transport,
    // finalization, native certification or a fabricated acceptance attestation.
    private sealed class SqlNotificationFixture : IAsyncDisposable
    {
        private readonly Web01Fixture sql = new();
        private Web01Case seed = null!;
        private WebApplication? app;
        private Web06TlsCertificate? tls;
        private HttpClient? bankTransport;
        private AcbProtocol protocol = null!;
        private Web05PosRuntime runtime = null!;
        private IPosRealtimeNotifier notifier = null!;
        private int requestStore;
        public AppDbContext Db = null!;
        public Order Order = null!;
        public BankNotificationDevice Device = null!;
        public BankNotificationBridgeService Bridge = null!;
        public readonly BankNotificationMonitor Monitor = new();
        public HttpClient Client = null!;
        public HttpClient PlainClient = null!;
        public int StoreId => seed.StoreId;
        public int ForeignStoreId { get; private set; }

        public static async Task<SqlNotificationFixture> Create()
        {
            var f = new SqlNotificationFixture();
            try
            {
                await f.sql.InitializeAsync(); f.seed = await f.sql.CaseAsync();
                f.requestStore = f.StoreId; f.ForeignStoreId = (await f.sql.CaseAsync()).StoreId;
                f.Db = f.seed.Context();
                var bank = await f.Db.StoreBankAccounts.SingleAsync(x => x.Id == f.seed.BankAccountId);
                bank.IsDefault = true; bank.ConfirmMode = BankQrConfirmMode.Manual; bank.QrRenderMode = BankQrRenderMode.LocalEmvQr;
                var variant = await f.Db.ProductVariants.SingleAsync(x => x.Id == f.seed.VariantId); variant.HasInputInvoice = false;
                var terminal = new POSTerminal { StoreId = f.StoreId, Code = "SQL-NOTIFY", Name = "Owned notification counter" };
                f.Db.Add(terminal); await f.Db.SaveChangesAsync();
                var shift = new POSShift { StoreId = f.StoreId, TerminalId = terminal.Id, WarehouseId = f.seed.WarehouseId,
                    OpenedByUserId = f.seed.UserId, Status = POSShiftStatus.Open, OpenedAtUtc = DateTime.UtcNow };
                f.Db.Add(shift); await f.Db.SaveChangesAsync();
                f.Order = new Order { StoreId = f.StoreId, POSShiftId = shift.Id, Subtotal = 100_000m, GrandTotal = 100_000m,
                    PaidTotal = 30_000m, BalanceDue = 70_000m, Status = OrderStatus.Draft, PaymentStatus = PaymentStatus.PartiallyPaid };
                f.Order.Lines.Add(new() { StoreId = f.StoreId, ProductId = f.seed.ProductId, VariantId = f.seed.VariantId,
                    Quantity = 1, BaseQuantity = 1, Multiplier = 1, UnitPrice = 100_000m, LineTotal = 100_000m,
                    ItemName = "Owned notification POS item", UnitName = "Unit", SellingUnitId = f.seed.UnitId });
                f.Order.Payments.Add(new() { StoreId = f.StoreId, Method = PaymentMethod.Cash, Amount = 30_000m, PaidAtUtc = DateTime.UtcNow });
                f.Device = new() { StoreId = f.StoreId, BankAccountId = bank.Id, BankAccountFingerprint = BankNotificationParser.AccountFingerprint(bank),
                    Name = "Owned SQL phone", TokenHash = new string('A', 64), Enabled = true };
                f.Db.AddRange(f.Order, f.Device); await f.Db.SaveChangesAsync();
                Assert.Equal(8, bank.RowVersion.Length); Assert.Equal(8, f.Device.RowVersion.Length);
                f.runtime = new(f.StoreId, terminal.Id, f.seed.UserId);
                f.notifier = DispatchProxy.Create<IPosRealtimeNotifier, Web05Proxy>();
                ((Web05Proxy)(object)f.notifier).Call = (method, _) => method.ReturnType == typeof(Task) ? Task.CompletedTask : throw new InvalidOperationException(method.Name);
                f.bankTransport = new(new Web05RejectAllTransport());
                f.protocol = new(f.bankTransport, new EphemeralDataProtectionProvider());
                var payments = f.Payments(f.Db);
                var manual = new POSPaymentQrService(new StoreBankAccountRepository(f.Db), new POSPaymentQrRequestRepository(f.Db), new FakeQrGenerator());
                var qr = await payments.CreateQrAsync(new OrderDraftDto { OrderId = f.Order.Id }, Installment(20_000m), manual, default);
                f.Bridge = f.MakeBridge(f.Db); Assert.True(await f.Bridge.ArmAsync(qr.Id, default));
                Assert.Equal(20_000m, qr.Amount); Assert.Equal(100_000m, f.Order.GrandTotal); await f.AssertOriginalPaymentOnly();
                await f.StartHost(); return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }

        private AcbPaymentService Payments(AppDbContext db) => new(db, protocol, Web06FinanceSupport.NoExternal<IPOSService>(), runtime, notifier, new SqlAcbOrderLockProvider());
        private BankNotificationBridgeService MakeBridge(AppDbContext db) => new(db, runtime, new SqlAcbOrderLockProvider(), Payments(db), notifier, Monitor);
        public void SelectRequestStore(int store) => Volatile.Write(ref requestStore, store);
        public BankNotificationUpload Body() => Upload(pos: "POS" + Order.Id);
        public void SetToken(string token)
        {
            Client.DefaultRequestHeaders.Remove("X-GaoApp-Notification-Device");
            Client.DefaultRequestHeaders.Add("X-GaoApp-Notification-Device", Device.Id + ":" + token);
        }
        public async Task RefreshDevice() => await Db.Entry(Device).ReloadAsync();
        public async Task AssertOriginalPaymentOnly()
        {
            var payment = Assert.Single(await Db.OrderPayments.AsNoTracking().Where(x => x.OrderId == Order.Id).ToListAsync());
            Assert.Equal(PaymentMethod.Cash, payment.Method); Assert.Equal(30_000m, payment.Amount);
            Assert.Equal(1, await seed.Database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.OrderPayments"));
            var order = await Db.Orders.AsNoTracking().SingleAsync(x => x.Id == Order.Id);
            Assert.Equal(OrderStatus.Draft, order.Status); Assert.Equal(100_000m, order.GrandTotal);
            Assert.Equal(30_000m, order.PaidTotal); Assert.Equal(70_000m, order.BalanceDue);
            Assert.Empty(await Db.Set<BankVerifiedReceipt>().ToListAsync()); Assert.Empty(await Db.Set<StorefrontNotificationCertification>().ToListAsync());
        }
        public async Task AssertAuthenticatedIntakeOnly()
        {
            var receipt = Assert.Single(await Db.Set<BankNotificationReceipt>().AsNoTracking().ToListAsync());
            var attestation = Assert.Single(await Db.Set<StorefrontNotificationAttestation>().AsNoTracking().ToListAsync());
            Assert.Equal(20_000m, receipt.Amount); Assert.Equal(Order.Id, receipt.OrderId); Assert.Equal(Body().Content, receipt.Content);
            Assert.Equal(receipt.Id, attestation.BankNotificationReceiptId); Assert.Equal(Device.Id, attestation.DeviceId);
            Assert.Equal("mobile.acb.com.vn", attestation.SourcePackage); Assert.NotEmpty(attestation.AccountVersion);
            Assert.Equal(1, await seed.Database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.BankNotificationReceipts"));
            Assert.Equal(1, await seed.Database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.StorefrontNotificationAttestations"));
            Assert.Null(attestation.BankOccurredAtUtc); await AssertOriginalPaymentOnly();
        }
        private async Task StartHost()
        {
            var directory = Web06Support.Runtime("WEB06 V16 legacy notification SQL endpoint; actual TLS, no financial certification");
            tls = Web06TlsCertificate.Create(directory);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ApplicationName = typeof(BankNotificationDeviceController).Assembly.FullName, ContentRootPath = Web03Support.Root() });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(server => { server.Listen(IPAddress.Loopback, 0); server.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(tls.Certificate)); });
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            builder.Services.AddScoped<AppDbContext>(_ => seed.Database.CreateTenantContext(Volatile.Read(ref requestStore)));
            builder.Services.AddScoped(s => MakeBridge(s.GetRequiredService<AppDbContext>()));
            builder.Services.AddAuthorization();
            builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
                .AddApplicationPart(typeof(BankNotificationDeviceController).Assembly).AddApplicationPart(typeof(BankNotificationHttpCsrfControlController).Assembly);
            app = builder.Build(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            var https = addresses.Single(x => x.StartsWith("https://", StringComparison.Ordinal)); var http = addresses.Single(x => x.StartsWith("http://", StringComparison.Ordinal));
            var thumbprint = tls.Certificate.Thumbprint;
            Client = new(new HttpClientHandler { AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (_, actual, _, _) => actual?.Thumbprint == thumbprint }) { BaseAddress = new(https) };
            PlainClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new(http) };
            await File.WriteAllTextAsync(Path.Combine(directory, "owned-host.json"), JsonSerializer.Serialize(new { Purpose = "legacy-device-endpoint-real-SQL-HTTPS-and-HTTP", ProcessId = Environment.ProcessId,
                ProcessStartedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(), StartedAtUtc = DateTime.UtcNow, Https = https, Http = http, StoreId,
                Database = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(seed.Database.ConnectionString).InitialCatalog, OrderId = Order.Id,
                CertificateStoreInstalled = false, NamedPrivateKeyTemporarilyPersisted = OperatingSystem.IsWindows(), tls.KeyName }));
        }
        public async ValueTask DisposeAsync()
        {
            Client?.Dispose(); PlainClient?.Dispose();
            try { if (app is not null) await app.StopAsync(); }
            finally { try { if (app is not null) await app.DisposeAsync(); }
                finally { try { if (Db is not null) await Db.DisposeAsync(); }
                    finally { try { bankTransport?.Dispose(); tls?.Dispose(); }
                        finally { await sql.DisposeAsync(); } } } }
        }
    }
}
