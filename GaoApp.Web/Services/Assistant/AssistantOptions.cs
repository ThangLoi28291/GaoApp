namespace GaoApp.Web.Services.Assistant;

public sealed class AssistantOptions
{
    public const string Section = "Assistant";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string PrivateKeyFile { get; set; } = "";
    internal string? ManagedPrivateKeyPem { get; set; }
    public int TimeoutSeconds { get; set; } = 100;
    public int TokenLifetimeSeconds { get; set; } = 120;
    public bool ReceivingEnabled { get; set; } = true;
    public bool NormalizeEnabled { get; set; } = true;
    public string StoreSettingsDirectory { get; set; } = "App_Data/Assistant";
    public string StorageId { get; set; } = "";
}
