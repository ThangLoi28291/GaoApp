using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GaoApp.Migrator;

/// <summary>
/// Adapts the Generic Host environment for infrastructure registrations that
/// are shared with the Web application. The migrator never serves web files;
/// the web-root values only make the shared DI graph valid at startup.
/// </summary>
internal sealed class MigratorWebHostEnvironment : IWebHostEnvironment
{
    private readonly IHostEnvironment _hostEnvironment;

    public MigratorWebHostEnvironment(IHostEnvironment hostEnvironment)
    {
        _hostEnvironment = hostEnvironment;
        WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
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
