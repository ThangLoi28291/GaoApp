using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace GaoApp.Tests.Storefront;

// Actual separate public Main; own real TLS and plain HTTP listeners. It does
// not rewrite schemes, install trust or run the Admin application/hosted jobs.
internal sealed class Web09PublicProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly ConcurrentQueue<string> lines = new();
    private readonly string runtime;
    private bool started, disposed;
    internal HttpClient Http { get; }
    internal string BaseUrl { get; }
    internal string PlainUrl { get; }
    internal string SpkiPin { get; }
    internal string CertificateSHA256 { get; }
    internal string Runtime => runtime;
    internal string LogPath => Path.Combine(runtime, "public-host.log");

    private Web09PublicProcess(Process process, string runtime, int https, int http, string certificateHash, string spki)
    {
        this.process = process; this.runtime = runtime; CertificateSHA256 = certificateHash; SpkiPin = spki;
        BaseUrl = $"https://localhost:{https}"; PlainUrl = $"http://localhost:{http}";
        Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer(),
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate is not null &&
                Convert.ToHexString(SHA256.HashData(certificate.RawData)) == CertificateSHA256 }) { BaseAddress = new Uri(BaseUrl) };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lines.Enqueue(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lines.Enqueue(e.Data); };
    }

    internal static async Task<Web09PublicProcess> StartAsync(Web09Case c, bool release = true,
        string environment = "Production", bool preview = false, string keyProtection = "WindowsDpapiCurrentUser",
        string? host = null, string? subdomain = null, string? keyRoot = null, string? connection = null)
    {
        connection ??= c.Seed.Database.ConnectionString;
        var database = new SqlConnectionStringBuilder(connection).InitialCatalog; Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", database);
        var runtime = Web09Support.Runtime("WEB09 actual public Main HTTPS fixture; new TLS PFX retained, no real release/owner acceptance");
        var https = Port(); var http = Port(); Assert.NotEqual(https, http);
        var pfx = Path.Combine(runtime, "owned-new-transport.pfx"); Assert.False(File.Exists(pfx));
        await File.WriteAllTextAsync(Path.Combine(runtime, "transport-before-create.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow,
            Owner = Web09Support.Owner(), Path = pfx, InitiallyAbsent = true, Purpose = "New fixture TLS transport only", RetainPfx = true }));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string certificateHash, spki;
        // This key is created now in memory. Only this new transport key is
        // serialized; no existing certificate or DP ring is exported/copied.
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build()); request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
            certificateHash = Convert.ToHexString(SHA256.HashData(certificate.RawData)); spki = Convert.ToBase64String(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
            await File.WriteAllBytesAsync(pfx, certificate.Export(X509ContentType.Pfx, password));
        }
        var dll = Environment.GetEnvironmentVariable("GAOAPP_WEB09_PUBLIC_DLL") ?? Path.Combine(Web09Support.Root(),
            ".artifacts/storefront-rollout/WEB09/build/bin/GaoApp.Storefront.Web/debug/GaoApp.Storefront.Web.dll");
        dll = Path.GetFullPath(dll); Web09Support.NoReparse(dll); Assert.True(File.Exists(dll), "Build/freeze the separate WEB09 public DLL before execution.");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = runtime, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll); start.ArgumentList.Add("--contentRoot"); start.ArgumentList.Add(Path.Combine(Web09Support.Root(), "GaoApp.Storefront.Web"));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = start.Environment["DOTNET_ENVIRONMENT"] = environment;
        start.Environment["ConnectionStrings__DefaultConnection"] = connection;
        start.Environment["Kestrel__Endpoints__OwnedTls__Url"] = $"https://127.0.0.1:{https}";
        start.Environment["Kestrel__Endpoints__OwnedTls__Certificate__Path"] = pfx;
        start.Environment["Kestrel__Endpoints__OwnedTls__Certificate__Password"] = password;
        start.Environment["Kestrel__Endpoints__OwnedPlain__Url"] = $"http://127.0.0.1:{http}";
        start.Environment["StorefrontHost__Host"] = host ?? "localhost";
        start.Environment["StorefrontHost__StoreSubdomain"] = subdomain ?? await c.Seed.SubdomainAsync();
        start.Environment["StorefrontHost__ReleaseEnabled"] = release.ToString(); start.Environment["StorefrontHost__PreviewEnabled"] = preview.ToString();
        start.Environment["StorefrontHost__KeyProtection"] = keyProtection; start.Environment["StorefrontHost__KeyRingPath"] = keyRoot ?? c.Checkout.KeysDirectory;
        start.Environment["StorefrontHost__UploadsRoot"] = Path.Combine(runtime, "uploads"); start.Environment["StorefrontAddressCatalog__Path"] = c.Checkout.AddressFile;
        start.Environment["StorefrontDiagnostics__RequestTimings"] = "false";
        var process = new Process { StartInfo = start }; var result = new Web09PublicProcess(process, runtime, https, http, certificateHash, spki);
        await File.WriteAllTextAsync(Path.Combine(runtime, "transport-before-host.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow,
            Owner = Web09Support.Owner(), KeyCreatedNow = true, ExistingKeyExported = false, CertificateStoreInstalled = false, PersistentCngCreated = false,
            Pfx = pfx, PfxBytes = new FileInfo(pfx).Length, PfxSHA256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(pfx))),
            CertificateSHA256 = certificateHash, SpkiPin = spki, RetainPfx = true, PasswordPersistedInEvidence = false }));
        try
        {
            Assert.True(process.Start()); result.started = true;
            StorefrontTestResourceMetadata.Producer(process, keyRoot ?? c.Checkout.KeysDirectory, database);
            await File.WriteAllTextAsync(Path.Combine(runtime, "owned-process.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Owner = Web09Support.Owner(),
                ProcessId = process.Id, StartedUtc = process.StartTime.ToUniversalTime(), Executable = process.MainModule?.FileName,
                PublicDll = dll, PublicDllSHA256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(dll))), ContentRoot = Path.Combine(Web09Support.Root(), "GaoApp.Storefront.Web"),
                Database = database, KeyRoot = keyRoot ?? c.Checkout.KeysDirectory, ActualHttps = true, PlainHttpNegativeListener = true, release, preview, environment,
                Host = host ?? "localhost", CertificateSHA256 = certificateHash, SpkiPin = spki, NoPasswordOrConnectionStringInMetadata = true }));
            process.BeginOutputReadLine(); process.BeginErrorReadLine(); var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(35))
            {
                if (process.HasExited) throw new InvalidOperationException("Owned WEB09 public host exited before liveness: " + string.Join('\n', result.lines));
                try { using var health = await result.Http.GetAsync("/health"); if (health.IsSuccessStatusCode) return result; } catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            throw new TimeoutException("Owned WEB09 public host liveness timeout.");
        }
        catch { await result.DisposeAsync(); throw; }
    }
    private static int Port()
    { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    internal async Task<string> CsrfAsync(string route = "/shop/orders")
    {
        var html = await Http.GetStringAsync(route); var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Actual public Razor must issue the CSRF token."); return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; Http.Dispose();
        if (started && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        await File.WriteAllLinesAsync(LogPath, lines);
        await File.WriteAllTextAsync(Path.Combine(runtime, "host-disposed.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow,
            Started = started, ProcessId = started ? process.Id : (int?)null, HasExited = !started || process.HasExited,
            ExitCode = started && process.HasExited ? process.ExitCode : (int?)null, RetainNewPfxAndRing = true, NoUnknownProcessOrPrivateCleanup = true }));
        process.Dispose();
    }
}
