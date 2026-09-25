using GaoApp.Application;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Infrastructure;
using GaoApp.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Always load appsettings from the published/executable directory. Visual
// Studio and Windows Service can use different working directories; relying on
// the current directory made the Release executable miss appsettings.* and
// start with an empty DataProtection:KeysPath.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "GaoApp Auto Invoice Worker";
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
builder.Services.AddHostedService<AutoInvoiceWorkerHostedService>();

var host = builder.Build();

// The Web host initializes DataProtection after its startup validation. The
// Windows Worker has no Web startup pipeline, so it must perform the same
// path validation and state initialization before any hosted service starts.
var keysPathResolver = host.Services.GetRequiredService<IDataProtectionKeysPathResolver>();
var keysPath = keysPathResolver.Resolve();
host.Services.GetRequiredService<IDataProtectionKeysDirectoryValidator>()
    .Validate(keysPath);
host.Services.GetRequiredService<DataProtectionKeysPathState>()
    .Initialize(keysPath);

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
