namespace GaoApp.Application.Common.Security;

public static class AssistantPermissions
{
    public const string Use = "assistant.use";
    public const string Chat = "assistant.chat";
    public const string Receiving = "assistant.receiving.resolve";
    public const string NormalizeName = "assistant.products.normalize_name";
    public const string Retail = "quotes.retail.read";
    public const string Wholesale = "quotes.wholesale.read";
    public const string Catalog = "catalog.read";
    public const string Monitor = "assistant.monitor";
    public static readonly string[] All = [Use, Chat, Receiving, NormalizeName, Retail, Wholesale, Catalog];
    public static readonly string[] Known = [..All, Monitor];
}
