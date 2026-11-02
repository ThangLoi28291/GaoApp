namespace GaoApp.Storefront.Web.Services;

// A private, explicit persistent key ring is required for durable retry outcomes.
// Missing configuration uses an ephemeral provider only for browsing/anti-forgery, never new checkout.
public sealed record StorefrontCheckoutReadiness(bool Ready, string? KeyDirectory)
{
    public static StorefrontCheckoutReadiness From(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["StorefrontHost:KeyRingPath"];
        if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured)) return new(false, null);
        try
        {
            var path = Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(path) || Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar) == path ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return new(false, null);
            for (var parent = Directory.GetParent(path); parent is not null; parent = parent.Parent)
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return new(false, null);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            static bool Inside(string path, string? root, StringComparison comparison) => !string.IsNullOrWhiteSpace(root) &&
                (path.Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), comparison) ||
                 path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison));
            if (Inside(path, environment.WebRootPath, comparison) || Inside(path, configuration["StorefrontHost:UploadsRoot"], comparison) ||
                Inside(path, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), comparison) ||
                Inside(path, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), comparison) ||
                Inside(path, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aspnet"), comparison)) return new(false, null);
            return new(true, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return new(false, null); }
    }
}
