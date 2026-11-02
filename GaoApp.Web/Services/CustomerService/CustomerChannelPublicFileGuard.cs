using GaoApp.Web.Services.Assistant;

namespace GaoApp.Web.Services.CustomerService;

public static class CustomerChannelPublicFileGuard
{
    private static readonly string[] CredentialFields = ["AccessTokenFile", "AppSecretFile", "VerifyTokenFile"];

    public static bool Blocked(string? requestPath, IWebHostEnvironment environment, IConfiguration configuration)
    {
        try
        {
            var path = Uri.UnescapeDataString(requestPath ?? "").Replace('\\', '/');
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment.Equals(CustomerChannelCredentialSafety.ReservedDirectory, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Read raw keys: a disabled integration can retain incomplete legacy options.
            var files = configuration.GetSection("CustomerChannels:Connections").GetChildren()
                .SelectMany(connection => CredentialFields.Select(field => connection[field]))
                .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            foreach (var file in files)
            {
                var leaf = file!.Replace('\\', '/').Split('/').Last().TrimEnd(' ', '.');
                if (leaf.Length > 0 && segments.Any(segment => segment.TrimEnd(' ', '.').Equals(leaf, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            if (files.Length == 0 || !bool.TryParse(configuration["CustomerChannels:Enabled"], out var enabled) || enabled)
                return false;

            // Startup skips credential validation while off. Keep static aliases from
            // exposing private files, including aliases whose target has another name.
            if (!string.IsNullOrWhiteSpace(environment.WebRootPath))
            {
                var relative = path.TrimStart('/');
                var candidate = Path.GetFullPath(relative.Length == 0 ? "." : relative, environment.WebRootPath);
                if (!AssistantStorageSafety.Inside(candidate, environment.WebRootPath)) return true;
                AssistantStorageSafety.NoLinks(candidate);
            }
            var physical = environment.WebRootFileProvider.GetFileInfo(path).PhysicalPath;
            if (!string.IsNullOrWhiteSpace(physical)) AssistantStorageSafety.NoLinks(physical);
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        {
            return true;
        }
    }
}
