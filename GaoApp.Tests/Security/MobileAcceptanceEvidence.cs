namespace GaoApp.Tests.Security;

// UI-24 can re-run the original native probes without replacing their historical evidence.
internal static class MobileAcceptanceEvidence
{
    internal static string PathFor(string root, params string[] parts)
    {
        var configured = Environment.GetEnvironmentVariable("GAO_UI24_REVIEW_ROOT");
        if (string.IsNullOrWhiteSpace(configured)) return Path.Combine([root, .. parts]);
        var allowed = Path.GetFullPath(Path.Combine(root, "TestResults", "mobile-ui-24-20261029")) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(configured) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("UI-24 evidence must stay inside its dedicated TestResults folder.");
        if (parts.Length < 2 || parts[0] != "TestResults") throw new InvalidOperationException("Unexpected evidence path.");
        return Path.Combine([destination, .. parts.Skip(1)]);
    }
}
