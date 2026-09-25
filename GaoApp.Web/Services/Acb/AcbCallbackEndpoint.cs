namespace GaoApp.Web.Services.Acb;

public static class AcbCallbackEndpoint
{
    // Object key prevents a request parameter/header from selecting a tenant.
    public static readonly object RoutedStoreItem = new();

    public static bool IsCallbackPath(PathString path)
    {
        var value = path.Value?.TrimEnd('/');
        return string.Equals(value, "/Admin/api-callback", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "/api/acb/webhook", StringComparison.OrdinalIgnoreCase);
    }
}
