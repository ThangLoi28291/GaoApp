namespace GaoApp.Web.Configuration;

/// <summary>
/// Cấu hình reverse proxy / forwarded headers cho web app.
/// </summary>
public class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>
    /// Có bật xử lý forwarded headers hay không.
    /// Nếu app chạy sau IIS/Nginx/Proxy thì nên bật.
    /// </summary>
    public bool EnableForwardedHeaders { get; set; } = true;

    /// <summary>
    /// Danh sách IP proxy tin cậy.
    /// Ví dụ:
    /// - 127.0.0.1
    /// - 10.10.10.1
    /// </summary>
    public List<string> KnownProxies { get; set; } = new();

    /// <summary>
    /// Danh sách network proxy tin cậy.
    /// Ví dụ:
    /// - 10.0.0.0/24
    /// - 192.168.1.0/24
    /// </summary>
    public List<string> KnownNetworks { get; set; } = new();
}