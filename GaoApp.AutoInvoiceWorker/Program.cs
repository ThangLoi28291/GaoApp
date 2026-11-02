using GaoApp.Application;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Infrastructure;
using GaoApp.Infrastructure.Configuration;
using GaoApp.Infrastructure.Security;
using GaoApp.Infrastructure.Services.Invoices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Always load appsettings from the published/executable directory. Visual
// Studio and Windows Service can use different working directories; relying on
// the current directory made the Release executable miss appsettings.* and
// start with an empty DataProtection:KeysPath.
var checkCredentials = args.Contains("--check-credentials", StringComparer.OrdinalIgnoreCase);
var checkKeys = args.Contains("--check-keys", StringComparer.OrdinalIgnoreCase);
var checkOnly = checkCredentials || checkKeys;
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Where(x => !string.Equals(x, "--check-credentials", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(x, "--check-keys", StringComparison.OrdinalIgnoreCase)).ToArray(),
    ContentRootPath = AppContext.BaseDirectory
});
var hostSettingsPath = AutoInvoiceWorkerHostSettings.Load(
    builder.Configuration, builder.Environment.EnvironmentName, AppContext.BaseDirectory);
Console.WriteLine($"Môi trường Worker: {builder.Environment.EnvironmentName}");
Console.WriteLine(hostSettingsPath is null
    ? "Cấu hình Worker: appsettings của ứng dụng và biến môi trường."
    : $"Cấu hình riêng của máy: {hostSettingsPath}");
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = builder.Configuration["Worker:ServiceName"] ?? "GaoApp.AutoInvoiceWorker";
});

// AddWindowsService registers the Windows Event Log provider. Visual Studio
// runs this project as a console process and ordinary developer accounts may
// not write the '.NET Runtime' Event Log source, so keep worker diagnostics on
// stdout instead. The same provider works when the executable is installed as
// a Windows Service and avoids startup failure caused by logging itself.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
// Web owns the shared key ring and credential writes. A Worker with a
// missing/wrong certificate must not generate replacement keys encrypted
// with its own fallback protection while trying to read Web credentials.
builder.Services.AddDataProtection().DisableAutomaticKeyGeneration();

// AddInfrastructure is shared with the Web host and therefore contains a few
// services whose constructors use Web-only/POS request abstractions. The
// worker does not serve HTTP and never has a current POS terminal, but the
// generic host still validates the complete service graph at startup. Provide
// explicit worker adapters so those shared registrations are valid without
// pretending that a request or terminal exists.
builder.Services.AddSingleton<IWebHostEnvironment>(sp =>
    new WorkerWebHostEnvironment(sp.GetRequiredService<IHostEnvironment>()));
builder.Services.AddScoped<ICurrentPOSContext, WorkerCurrentPOSContext>();
builder.Services.AddScoped<ICurrentUser, WorkerCurrentUser>();
if (!checkOnly)
{
    builder.Services.AddHostedService<AutoInvoiceWorkerHostedService>();
    builder.Services.AddHostedService<GaoApp.Infrastructure.Services.Audit.AuditOutboxWorker>();
}

using var host = builder.Build();

// The Web host initializes DataProtection after its startup validation. The
// Windows Worker has no Web startup pipeline, so it must perform the same
// path validation and state initialization before any hosted service starts.
var keysPathResolver = host.Services.GetRequiredService<IDataProtectionKeysPathResolver>();
var keysPath = keysPathResolver.Resolve();
if (!checkOnly)
    host.Services.GetRequiredService<IDataProtectionKeysDirectoryValidator>().Validate(keysPath);
host.Services.GetRequiredService<DataProtectionKeysPathState>()
    .Initialize(keysPath);

if (checkOnly)
{
    // Do not start the host: no timer, queue claim, heartbeat, credential
    // migration, key generation or Viettel request is permitted in this mode.
    Console.WriteLine($"Máy xử lý: {Environment.MachineName}");
    Console.WriteLine($"DataProtection:KeysPath: {keysPath}");
    Console.WriteLine(string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:CertificatePath"])
        ? "Bảo vệ khóa: Windows DPAPI (khóa phải được tạo trên cùng máy)."
        : "Bảo vệ khóa: PFX (phải trùng certificate/private key của Web).");
    if (!Directory.Exists(keysPath))
    {
        Console.Error.WriteLine("Không tồn tại thư mục DataProtection keys đã cấu hình.");
        Environment.ExitCode = 1;
        return;
    }

    if (checkKeys)
    {
        var keys = host.Services.GetRequiredService<IKeyManager>().GetAllKeys();
        var failures = 0;
        foreach (var key in keys)
        {
            try
            {
                if (key.CreateEncryptor() == null)
                    throw new CryptographicException("Không tạo được encryptor cho khóa đã lưu.");
                Console.WriteLine($"Khóa {key.KeyId}: Giải mã OK.");
            }
            catch (CryptographicException ex)
            {
                failures++;
                Console.Error.WriteLine($"Khóa {key.KeyId}: " +
                    GaoApp.Application.Common.InvoiceCredentialErrors.CannotDecrypt(ex, "Không mở được khóa đã lưu.").Message);
            }
        }
        if (keys.Count == 0)
            Console.Error.WriteLine("Key ring không có khóa. Chế độ kiểm tra không tạo khóa mới.");
        Environment.ExitCode = keys.Count > 0 && failures == 0 ? 0 : 1;
        if (!checkCredentials || Environment.ExitCode != 0)
            return;
    }

    using var scope = host.Services.CreateScope();
    var results = await InvoiceProviderCredentialCheck.RunAsync(
        scope.ServiceProvider.GetRequiredService<IInvoiceProviderSettingRepository>(),
        scope.ServiceProvider.GetRequiredService<IInvoiceProviderCredentialProtector>());
    foreach (var result in results)
        Console.WriteLine($"Cấu hình #{result.SettingId}, cửa hàng #{result.StoreId}: " +
            (result.CanDecrypt ? "Giải mã mật khẩu đã lưu OK." : result.Error));
    if (results.Count == 0)
        Console.Error.WriteLine("Không có cấu hình Viettel đang hoạt động để kiểm tra.");
    Environment.ExitCode = results.Count > 0 && results.All(x => x.CanDecrypt) ? 0 : 1;
    return;
}

await host.RunAsync();

internal sealed class WorkerCurrentUser : ICurrentUser
{
    public int? UserId => null;
    public string? UserName => "GaoApp Auto Invoice Worker";
    public int? TerminalId => null;
    public string? TerminalCode => null;
    public bool IsAuthenticated => false;
}

internal sealed class WorkerCurrentPOSContext : ICurrentPOSContext
{
    public int StoreId => 0;
    public int TerminalId => 0;
    public int? UserId => null;
    public bool IsAvailable => false;
}

/// <summary>
/// Adapts the generic worker environment for shared infrastructure services
/// that are also used by the Web host. The worker never serves static files;
/// the empty web-root provider only satisfies the shared DI graph.
/// </summary>
internal sealed class WorkerWebHostEnvironment : IWebHostEnvironment
{
    private readonly IHostEnvironment _hostEnvironment;

    public WorkerWebHostEnvironment(IHostEnvironment hostEnvironment)
    {
        _hostEnvironment = hostEnvironment;
        WebRootPath = Path.Combine(hostEnvironment.ContentRootPath, "wwwroot");
        WebRootFileProvider = new NullFileProvider();
    }

    public string ApplicationName
    {
        get => _hostEnvironment.ApplicationName;
        set => _hostEnvironment.ApplicationName = value;
    }

    public string EnvironmentName
    {
        get => _hostEnvironment.EnvironmentName;
        set => _hostEnvironment.EnvironmentName = value;
    }

    public string ContentRootPath
    {
        get => _hostEnvironment.ContentRootPath;
        set => _hostEnvironment.ContentRootPath = value;
    }

    public IFileProvider ContentRootFileProvider
    {
        get => _hostEnvironment.ContentRootFileProvider;
        set => _hostEnvironment.ContentRootFileProvider = value;
    }

    public string WebRootPath { get; set; }

    public IFileProvider WebRootFileProvider { get; set; }
}
