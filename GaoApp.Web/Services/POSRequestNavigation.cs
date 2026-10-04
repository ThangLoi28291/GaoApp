using Microsoft.AspNetCore.Http;

namespace GaoApp.Web.Services;

public static class POSRequestNavigation
{
    public const string Path = "/admin/pos-shift/requests";

    public static string FromLegacy(IQueryCollection query, string tab)
    {
        var values = query.Where(x => !string.Equals(x.Key, "tab", StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Value.Select(value => new KeyValuePair<string, string?>(x.Key, value)))
            .Append(new KeyValuePair<string, string?>("tab", tab));
        return Path + QueryString.Create(values);
    }
}
