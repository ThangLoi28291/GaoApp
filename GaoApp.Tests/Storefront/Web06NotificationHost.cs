using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Controllers;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Storefront;

// Actual HTTPS socket + production controller + real device token verification.
// No application Program, forwarded-HTTPS forgery, native trust installation or bank transport.
internal sealed class Web06NotificationHost(WebApplication app, Web06TlsCertificate tls, HttpClient client) : IAsyncDisposable
{
    internal HttpClient Client => client;
    internal static BankNotificationBridgeService Bridge(Web01Case c, AppDbContext db, string keys)
    {
        var runtime = new Web05PosRuntime(c.StoreId, 0, c.UserId);
        var notifier = System.Reflection.DispatchProxy.Create<IPosRealtimeNotifier, Web05Proxy>();
        ((Web05Proxy)(object)notifier).Call = (method, _) => method.ReturnType == typeof(Task) ? Task.CompletedTask : throw new InvalidOperationException(method.Name);
        var protocol = new AcbProtocol(new HttpClient(new Web05RejectAllTransport()), Web05Support.AdminProtection(keys));
        var payments = new AcbPaymentService(db, protocol, Web06FinanceSupport.NoExternal<IPOSService>(), runtime, notifier, new SqlAcbOrderLockProvider());
        return new(db, runtime, new SqlAcbOrderLockProvider(), payments, notifier);
    }
    internal static Task<Web06NotificationHost> StartAsync(Web06Case c, IInterceptor? interceptor = null)
        => StartAsync(c.Seed, Path.Combine(c.Checkout.Directory, "admin-keys"), interceptor);
    private static async Task<Web06NotificationHost> StartAsync(Web01Case c, string adminKeys, IInterceptor? interceptor)
    {
        var directory = Web06Support.Runtime("WEB06 owned HTTPS native acceptance; in-memory certificate with temporary exact-owned Windows user key");
        var tls = Web06TlsCertificate.Create(directory); WebApplication? app = null; HttpClient? client = null;
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ApplicationName = typeof(BankNotificationDeviceController).Assembly.FullName, ContentRootPath = Web03Support.Root() });
            builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(tls.Certificate)));
            builder.Services.AddScoped<AppDbContext>(_ => c.Database.CreateTenantContext(c.StoreId, interceptor)); builder.Services.AddScoped(s => Bridge(c, s.GetRequiredService<AppDbContext>(), adminKeys));
            builder.Services.AddControllers().AddApplicationPart(typeof(BankNotificationDeviceController).Assembly);
            app = builder.Build(); app.MapControllers();
            await app.StartAsync(); var origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var thumbprint = tls.Certificate.Thumbprint;
            var handler = new HttpClientHandler { AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (_, actual, _, _) => actual?.Thumbprint == thumbprint };
            client = new HttpClient(handler) { BaseAddress = new(origin) };
            await File.WriteAllTextAsync(Path.Combine(directory, "owned-host.json"), System.Text.Json.JsonSerializer.Serialize(new { Purpose = "https-device-acceptance", ProcessId = Environment.ProcessId,
                ProcessStartedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(), StartedAtUtc = DateTime.UtcNow, Origin = origin, StoreId = c.StoreId,
                Database = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Database.ConnectionString).InitialCatalog, AdminKeyRoot = adminKeys, PrivateCertificatePersisted = false,
                NamedPrivateKeyTemporarilyPersisted = OperatingSystem.IsWindows(), tls.KeyName }));
            return new(app, tls, client);
        }
        catch { client?.Dispose(); try { if (app is not null) await app.DisposeAsync(); } finally { tls.Dispose(); } throw; }
    }
    internal async Task<HttpResponseMessage> UploadAsync(int device, string token, BankNotificationUpload upload, bool authenticate = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bank-notifications") { Content = JsonContent.Create(upload) };
        if (authenticate) request.Headers.Add("X-GaoApp-Notification-Device", device + ":" + token);
        return await client.SendAsync(request);
    }
    public async ValueTask DisposeAsync()
    {
        client.Dispose(); try { await app.StopAsync(); } finally { try { await app.DisposeAsync(); } finally { tls.Dispose(); } }
    }
}

// Windows Schannel actually requires a persisted provider key on this host;
// ephemeral RSA and ephemeral PFX both failed in the isolated TLS-only probe.
// The certificate stays in memory. Only this newly created CurrentUser key is
// deleted; no certificate-store install, key enumeration or other-key deletion.
internal sealed class Web06TlsCertificate(string directory) : IDisposable
{
    private RSA? rsa; private X509Certificate2? certificate; private CngKey? key;
    private string? uniqueName, knownUserFile; private bool created, disposed;
    internal string? KeyName { get; private set; }
    internal X509Certificate2 Certificate => certificate ?? throw new InvalidOperationException("Certificate not initialized");
    internal static Web06TlsCertificate Create(string directory)
    {
        var material = new Web06TlsCertificate(directory);
        try
        {
            if (OperatingSystem.IsWindows()) material.CreateWindows(); else { material.rsa = RSA.Create(2048); material.certificate = Build(material.rsa); }
            return material;
        }
        catch { material.Dispose(); throw; }
    }
    [SupportedOSPlatform("windows")]
    private void CreateWindows()
    {
        KeyName = "GaoApp-WEB06-TLS-" + Guid.NewGuid().ToString("N"); var provider = CngProvider.MicrosoftSoftwareKeyStorageProvider;
        File.WriteAllText(Path.Combine(directory, "tls-key-before-create.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, ProcessId = Environment.ProcessId,
            ProcessStartedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(), KeyName, Provider = provider.Provider,
            MachineKey = false, CertificateStoreInstalled = false, OverwriteExistingKey = false }));
        if (CngKey.Exists(KeyName, provider, CngKeyOpenOptions.None)) throw new InvalidOperationException("Refuse preexisting TLS key");
        var parameters = new CngKeyCreationParameters { Provider = provider, KeyCreationOptions = CngKeyCreationOptions.None,
            ExportPolicy = CngExportPolicies.None, KeyUsage = CngKeyUsages.Signing | CngKeyUsages.Decryption };
        parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
        key = CngKey.Create(CngAlgorithm.Rsa, KeyName, parameters); created = true;
        if (key.KeyName != KeyName || key.IsMachineKey || key.Provider?.Provider != provider.Provider) throw new InvalidOperationException("Unexpected TLS key identity");
        uniqueName = key.UniqueName ?? throw new InvalidOperationException("Missing owned key identity");
        knownUserFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Crypto", "Keys", uniqueName);
        File.WriteAllText(Path.Combine(directory, "tls-key-created.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, ProcessId = Environment.ProcessId,
            ProcessStartedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(), CreatedByThisProcess = created,
            KeyName, UniqueName = uniqueName, UserCryptoPath = knownUserFile, UserFileExists = File.Exists(knownUserFile),
            Provider = key.Provider?.Provider, MachineKey = key.IsMachineKey, PrivateKeyBytesCopied = false, CertificateStoreInstalled = false }));
        rsa = new RSACng(key); certificate = Build(rsa);
    }
    private static X509Certificate2 Build(RSA rsa)
    {
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try { certificate?.Dispose(); } finally { try { rsa?.Dispose(); } finally { if (OperatingSystem.IsWindows()) DeleteWindows(); } }
    }
    [SupportedOSPlatform("windows")]
    private void DeleteWindows()
    {
        if (!created) return; string? error = null;
        try
        {
            if (key is null || KeyName is null || !Regex.IsMatch(KeyName, "^GaoApp-WEB06-TLS-[a-f0-9]{32}$") || key.KeyName != KeyName ||
                key.UniqueName != uniqueName || key.IsMachineKey || key.Provider?.Provider != CngProvider.MicrosoftSoftwareKeyStorageProvider.Provider)
                throw new InvalidOperationException("Refuse deletion of an unrelated TLS key");
            key.Delete();
        }
        catch (Exception ex) { error = ex.Message; }
        finally { key?.Dispose(); }
        var exists = CngKey.Exists(KeyName!, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.None);
        var fileExists = knownUserFile is not null && File.Exists(knownUserFile);
        File.WriteAllText(Path.Combine(directory, "tls-key-cleanup.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, ProcessId = Environment.ProcessId,
            KeyName, UniqueName = uniqueName, UserCryptoPath = knownUserFile, CreatedByThisProcess = created, KeyStillExists = exists, FileStillExists = fileExists,
            Error = error, PrivateKeyBytesCopied = false, MachineKey = false, CertificateStoreInstalled = false, OtherKeysEnumeratedOrDeleted = false }));
        if (error is not null || exists || fileExists) throw new InvalidOperationException("Owned TLS key cleanup incomplete: " + error);
    }
}
