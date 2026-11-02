namespace GaoApp.Tests.Storefront;

// Reuses the prior isolated full customer executable, never the admin Program.
// Per-run root READY must bind every public-DLL selector to the WEB07 build.
internal sealed class Web07PublicHost(HttpClient http, string origin, string log, IAsyncDisposable owned) : IAsyncDisposable
{
    private readonly HashSet<string> capabilities = new(StringComparer.Ordinal);
    private bool disposed;
    internal void ProtectCapability(string value) => capabilities.Add(value);
    internal HttpClient Http => http;
    internal string Origin => origin;
    internal string LogPath => log;
    internal static async Task<Web07PublicHost> StartAsync(Web07Case c, string environment = "Development", bool preview = true)
    {
        var expected = Environment.GetEnvironmentVariable("GAOAPP_WEB07_PUBLIC_DLL");
        Assert.False(string.IsNullOrWhiteSpace(expected), "Root READY must select the isolated WEB07 public executable explicitly.");
        var selector = Environment.GetEnvironmentVariable(c.Banking is null ? "GAOAPP_WEB04_PUBLIC_DLL" : "GAOAPP_WEB05_PUBLIC_DLL");
        Assert.Equal(Path.GetFullPath(expected!), Path.GetFullPath(selector!));
        IAsyncDisposable owner; HttpClient http; string origin; string log;
        if (c.Banking is not null)
        {
            var host = await Web05PublicProcess.StartAsync(c.Banking, environment, preview);
            owner = host; http = host.Http; origin = host.BaseUrl; log = host.LogPath;
        }
        else
        {
            var host = await Web04PublicProcess.StartAsync(c.Checkout, environment: environment, preview: preview);
            owner = host; http = host.Http; origin = host.BaseUrl; log = host.LogPath;
        }
        try
        {
            await File.WriteAllTextAsync(Path.Combine(c.Directory, "public-host-lineage.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                AtUtc = DateTime.UtcNow, SourceRuntimeLog = log, PublicDll = Path.GetFullPath(expected!),
                PublicDllSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(expected!))),
                FullCustomerHost = true, NoAdminMain = true
            }));
            var result = new Web07PublicHost(http, origin, log, owner); result.ProtectCapability(c.Token); return result;
        }
        catch { await owner.DisposeAsync(); throw; }
    }
    internal async Task CsrfAsync()
    {
        var html = await http.GetStringAsync("/shop/orders");
        var match = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success); http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        http.DefaultRequestHeaders.Add("RequestVerificationToken", System.Net.WebUtility.HtmlDecode(match.Groups[1].Value));
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; await owned.DisposeAsync();
        var text = await File.ReadAllTextAsync(log);
        foreach (var capability in capabilities) Web07Support.NoCapability(text, capability, "owned full public-host log");
    }
}
