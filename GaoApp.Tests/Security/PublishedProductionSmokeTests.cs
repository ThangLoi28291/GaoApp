using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PublishedProductionSmokeTests
{
    [Fact]
    public async Task Published_migrator_and_web_support_fresh_bootstrap_https_login_and_restart()
    {
        await using var fixture = await PublishedFixture.CreateAsync();
        await fixture.RunMigratorAsync(bootstrap: true);
        await using var db = fixture.Database.CreateHostContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains("20260909080000_AddPosCollectionIdempotency", await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await db.Stores.CountAsync());
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(0, await db.Orders.CountAsync());
        var terminal = await db.POSTerminals.SingleAsync();
        // A repeated invocation must neither add another owner nor add demo business data.
        await fixture.RunMigratorAsync(bootstrap: true);
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.Stores.CountAsync());

        using var client = fixture.CreateClient();
        await fixture.StartWebAsync(client);
        foreach (var path in new[] { "/health/live", "/health/ready" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var health = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Healthy", health.RootElement.GetProperty("status").GetString());
            Assert.Single(health.RootElement.EnumerateObject());
            Assert.True(response.Headers.Contains("Strict-Transport-Security"));
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
            Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
            Assert.Contains("frame-ancestors 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
            Assert.False(response.Headers.Contains("Server"));
        }
        using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
        using (var plain = new HttpClient(handler) { BaseAddress = fixture.HttpAddress })
        {
            plain.DefaultRequestHeaders.Host = fixture.TenantHost;
            using var redirect = await plain.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.TemporaryRedirect, redirect.StatusCode);
            Assert.Equal("https", redirect.Headers.Location!.Scheme);
            Assert.Equal(fixture.TenantHost, redirect.Headers.Location.Host);
        }
        using (var invalidHost = new HttpRequestMessage(HttpMethod.Get, "/health/live"))
        {
            invalidHost.Headers.Host = "attacker.invalid";
            using var response = await client.SendAsync(invalidHost);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        foreach (var path in new[] { "/__tenant-hash", "/uploads/invoices/probe.xml", "/appsettings.json" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using (var denied = await client.GetAsync("/admin/pos"))
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);

        var loginHtml = await client.GetStringAsync("/admin/account/login");
        var tokenMatch = Regex.Match(loginHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(tokenMatch.Success);
        using (var login = await client.PostAsync("/admin/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = fixture.AdminName, ["Password"] = fixture.AdminPassword,
            ["SelectedTerminalId"] = terminal.Id.ToString(),
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value)
        })))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), x => x.StartsWith(".AspNetCore.Cookies="));
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        }
        var pos = await client.GetStringAsync("/admin/pos");
        Assert.Contains("pos.payment.js", pos);
        foreach (var path in new[] { "/Admin/js/pos/pos.payment.js", "/Admin/css/pos/pos.css", "/GaoApp.Web.styles.css" })
        {
            using var asset = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
            Assert.True((await asset.Content.ReadAsByteArrayAsync()).Length > 0);
        }
        foreach (var path in new[] { "/health/live", "/health/ready" })
        {
            using var probe = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
            Assert.False(probe.Headers.Contains("Set-Cookie"), "A public health probe must not delete or renew the login cookie.");
        }
        using (var stillAuthenticated = await client.GetAsync("/admin/pos"))
        {
            Assert.Equal(HttpStatusCode.OK, stillAuthenticated.StatusCode);
            Assert.True(stillAuthenticated.Headers.CacheControl!.NoStore);
        }
        var keyFiles = Directory.GetFiles(fixture.KeysPath, "key-*.xml");
        Assert.NotEmpty(keyFiles);
        foreach (var keyFile in keyFiles)
        {
            var key = XDocument.Load(keyFile);
            Assert.Contains(key.Descendants(), x => x.Name.LocalName == "encryptedSecret");
            Assert.DoesNotContain(key.Descendants(), x => x.Name.LocalName == "masterKey");
        }
        await fixture.StopWebAsync();
        await fixture.StartWebAsync(client);
        // Reuses the actual encrypted authentication cookie with a fresh Web process.
        using var afterRestart = await client.GetAsync("/admin/pos");
        Assert.Equal(HttpStatusCode.OK, afterRestart.StatusCode);
        Assert.Contains("pos.payment.js", await afterRestart.Content.ReadAsStringAsync());
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(0, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Published_web_refuses_missing_secrets_and_in_publish_runtime_storage()
    {
        await using var fixture = await PublishedFixture.CreateAsync();
        await fixture.AssertStartupRejectedAsync(start =>
        {
            start.Environment.Remove("DataProtection__CertificatePath");
            start.Environment.Remove("DataProtection__CertificatePassword");
        }, "CertificatePath");
        await fixture.AssertStartupRejectedAsync(start => start.Environment["ConnectionStrings__DefaultConnection"] = "", "DefaultConnection");
        await fixture.AssertStartupRejectedAsync(start => start.Environment["Storage__UploadRoot"] = Path.Combine(fixture.WebRoot, "runtime-should-not-exist"), "UploadRoot");
        Assert.False(Directory.Exists(Path.Combine(fixture.WebRoot, "runtime-should-not-exist")));
    }

    private sealed class PublishedFixture : IAsyncDisposable
    {
        internal InventoryPostingLocalDb Database { get; } = new();
        internal string TenantHost => "shop.phase6.test";
        internal string AdminName => "release_owner";
        internal string AdminPassword { get; } = "Release!" + Guid.NewGuid().ToString("N") + "7aA";
        private readonly string pfxPassword = Guid.NewGuid().ToString("N");
        private readonly string runtimeRoot;
        private readonly string releaseRoot;
        private readonly X509Certificate2 certificate;
        private readonly ConcurrentQueue<string> logs = new();
        private readonly List<Process> processes = [];
        private Process? web;
        internal string WebRoot => Path.Combine(releaseRoot, "web");
        internal string KeysPath => Path.Combine(runtimeRoot, "keys");
        internal Uri HttpAddress { get; }
        private Uri HttpsAddress { get; }

        internal static async Task<PublishedFixture> CreateAsync()
            => new(await PublishedTestRelease.GetAsync());

        private PublishedFixture(string verifiedReleaseRoot)
        {
            releaseRoot = verifiedReleaseRoot;
            Assert.True(File.Exists(Path.Combine(releaseRoot, "release-manifest.json")), "Verify the release before running smoke tests.");
            runtimeRoot = Path.Combine(FullApplicationFixture.SourceRoot(), "Logs", "security-phase6-runtime", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(KeysPath);
            Directory.CreateDirectory(Path.Combine(runtimeRoot, "uploads"));
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=GaoApp isolated release test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback); names.AddDnsName(TenantHost);
            request.CertificateExtensions.Add(names.Build());
            certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(Path.Combine(runtimeRoot, "test-only.pfx"), certificate.Export(X509ContentType.Pfx, pfxPassword));
            HttpAddress = new Uri($"http://127.0.0.1:{FreePort()}");
            HttpsAddress = new Uri($"https://127.0.0.1:{FreePort()}");
        }

        internal HttpClient CreateClient()
        {
            var expected = certificate.GetCertHashString(HashAlgorithmName.SHA256);
            var handler = new HttpClientHandler { AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.GetCertHashString(HashAlgorithmName.SHA256) == expected };
            var client = new HttpClient(handler) { BaseAddress = HttpsAddress, Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Host = TenantHost;
            return client;
        }

        private ProcessStartInfo StartInfo(string component)
        {
            var directory = Path.Combine(releaseRoot, component.ToLowerInvariant());
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment.Clear();
            foreach (var key in new[] { "PATH", "SystemRoot", "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "ProgramFiles", "ProgramFiles(x86)", "DOTNET_ROOT", "DOTNET_ROOT_X64" })
                if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
            start.ArgumentList.Add(Path.Combine(directory, $"GaoApp.{component}.dll"));
            start.Environment["DOTNET_ENVIRONMENT"] = "Production";
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            start.Environment["ConnectionStrings__DefaultConnection"] = Database.ConnectionString;
            start.Environment["DataProtection__KeysPath"] = KeysPath;
            start.Environment["DataProtection__CertificatePath"] = Path.Combine(runtimeRoot, "test-only.pfx");
            start.Environment["DataProtection__CertificatePassword"] = pfxPassword;
            start.Environment["Serilog__MinimumLevel__Default"] = "Warning";
            if (component == "Web")
            {
                start.Environment["Storage__UploadRoot"] = Path.Combine(runtimeRoot, "uploads");
                start.Environment["AppUrl__BaseUrl"] = "https://phase6.test";
                start.Environment["AppUrl__AdminUrl"] = "https://admin.phase6.test";
                start.Environment["Tenant__RootDomain"] = "phase6.test";
                start.Environment["AllowedHosts"] = "phase6.test;*.phase6.test";
                start.Environment["Kestrel__Endpoints__Http__Url"] = HttpAddress.ToString();
                start.Environment["Kestrel__Endpoints__Https__Url"] = HttpsAddress.ToString();
                start.Environment["Kestrel__Endpoints__Https__Certificate__Path"] = Path.Combine(runtimeRoot, "test-only.pfx");
                start.Environment["Kestrel__Endpoints__Https__Certificate__Password"] = pfxPassword;
                start.Environment["ASPNETCORE_HTTPS_PORT"] = HttpsAddress.Port.ToString();
            }
            return start;
        }

        private Process Start(ProcessStartInfo start)
        {
            var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => { if (e.Data is { } line) logs.Enqueue(line); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) logs.Enqueue(line); };
            Assert.True(process.Start()); processes.Add(process);
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            return process;
        }

        internal async Task RunMigratorAsync(bool bootstrap)
        {
            var schema = StartInfo("Migrator");
            schema.ArgumentList.Add("--schema-only");
            // Start registers ownership with this fixture; DisposeAsync disposes each process once.
            var migration = Start(schema);
            await migration.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
            Assert.Equal(0, migration.ExitCode);
            if (!bootstrap) return;
            var start = StartInfo("Migrator");
            start.ArgumentList.Add("--bootstrap");
            start.Environment["ProductionBootstrap__Enabled"] = bootstrap.ToString();
            foreach (var (key, value) in new Dictionary<string, string> {
                ["StoreName"] = "Release smoke store", ["StoreSubdomain"] = "shop",
                ["LegalEntityCode"] = "SMOKE", ["LegalEntityName"] = "Release smoke", ["LegalEntityLegalName"] = "Release smoke test only",
                ["WarehouseCode"] = "SMOKE", ["WarehouseName"] = "Smoke warehouse", ["TerminalCode"] = "SMOKE", ["TerminalName"] = "Smoke terminal",
                ["AdminUserName"] = AdminName, ["AdminFullName"] = "Release test owner", ["AdminPassword"] = AdminPassword })
                start.Environment["ProductionBootstrap__" + key] = value;
            var process = Start(start);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
            Assert.True(process.ExitCode == 0, "Published Migrator failed: " + string.Join('\n', logs.TakeLast(15)));
        }

        internal async Task StartWebAsync(HttpClient client)
        {
            web = Start(StartInfo("Web"));
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(45))
            {
                Assert.False(web.HasExited, "Published Web exited: " + string.Join('\n', logs.TakeLast(15)));
                try { using var response = await client.GetAsync("/health/live"); if (response.IsSuccessStatusCode) return; }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(150);
            }
            throw new TimeoutException("Published Web startup timeout.");
        }

        internal async Task AssertStartupRejectedAsync(Action<ProcessStartInfo> change, string expectedKey)
        {
            var before = logs.Count;
            var start = StartInfo("Web"); change(start);
            var process = Start(start);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains(expectedKey, string.Join('\n', logs.Skip(before)), StringComparison.OrdinalIgnoreCase);
        }

        internal async Task StopWebAsync()
        {
            if (web is not null && !web.HasExited) { web.Kill(entireProcessTree: true); await web.WaitForExitAsync(); }
            web = null;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var process in processes)
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                process.Dispose();
            }
            var evidence = Path.Combine(FullApplicationFixture.SourceRoot(), "TestResults", "security-phase6");
            Directory.CreateDirectory(evidence);
            await File.WriteAllLinesAsync(Path.Combine(evidence, "published-" + Path.GetFileName(runtimeRoot) + ".log"),
                logs.Select(line => line.Replace(AdminPassword, "[redacted]").Replace(pfxPassword, "[redacted]")));
            certificate.Dispose();
            try { await Database.DisposeAsync(); }
            finally
            {
                var parent = Path.Combine(FullApplicationFixture.SourceRoot(), "Logs", "security-phase6-runtime") + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(runtimeRoot).StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParseExact(Path.GetFileName(runtimeRoot), "N", out _)) throw new InvalidOperationException("Unsafe test cleanup path.");
                Directory.Delete(runtimeRoot, recursive: true);
            }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
        }
    }
}
