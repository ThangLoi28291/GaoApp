using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace GaoApp.Web.Configuration;

/// <summary>Opt-in local launch profiles open ordinary Chrome; no debugger or browser shutdown ownership.</summary>
public static class DevelopmentChromeLauncher
{
    public static void Register(WebApplication app)
    {
        if (!OperatingSystem.IsWindows() || !app.Environment.IsDevelopment()
            || !app.Configuration.GetValue<bool>("GaoAppDevBrowser:Enabled")) return;

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            try
            {
                var addresses = app.Services.GetRequiredService<IServer>().Features
                    .Get<IServerAddressesFeature>()?.Addresses;
                var address = addresses?.Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
                    .FirstOrDefault(uri => uri is not null && uri.IsLoopback
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                        && string.IsNullOrEmpty(uri.UserInfo));
                if (address is null)
                {
                    app.Logger.LogWarning("Local Chrome was not opened: no loopback HTTP endpoint is available.");
                    return;
                }

                var chrome = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                }.Where(folder => !string.IsNullOrWhiteSpace(folder))
                 .Select(folder => Path.Combine(folder, "Google", "Chrome", "Application", "chrome.exe"))
                 .FirstOrDefault(File.Exists);
                if (chrome is null)
                {
                    app.Logger.LogWarning("Local Chrome was not opened: Chrome was not found in the standard installation folders.");
                    return;
                }

                var start = new ProcessStartInfo
                {
                    FileName = chrome,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };
                start.ArgumentList.Add("--new-window");
                start.ArgumentList.Add(new Uri(address, "/admin").AbsoluteUri);
                // Dispose releases our process handle only. There is deliberately no stop/exit callback.
                using var launched = Process.Start(start);
            }
            catch (Exception exception)
            {
                // Browser availability must never stop the web server or expose unrelated settings.
                app.Logger.LogWarning("Local Chrome could not be opened ({ExceptionType}). Open the local Web URL manually.", exception.GetType().Name);
            }
        });
    }
}
