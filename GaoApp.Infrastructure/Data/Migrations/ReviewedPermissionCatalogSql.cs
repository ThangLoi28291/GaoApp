using System.Security.Cryptography;
using System.Text;

namespace GaoApp.Infrastructure.Data.Migrations;

// Permission-only migrations add no schema objects. Accept their exact
// reviewed data statements, never arbitrary SQL.
internal static class ReviewedPermissionCatalogSql
{
    private static readonly IReadOnlyDictionary<string, string> Hashes = new Dictionary<string, string>
    {
        ["20261030110000_AddCustomerInvoiceNotificationsPermission"] = "78ffc1217537fffb911b53d32ea83133604dd0812160e3124e0c80cb93067d2f",
        ["20261030120000_AddCustomerMarketingConsentP6"] = "608a9d4911f972f9b6be9af4f3f8e061b59e21ee428a0950b5b9f361073c528c",
        ["20261030150000_AddCustomerServicePermissionCatalog"] = "6198221c5668169bea3acf01f9935d46fd6a6f60f65175e03f724a7a8a1aab01",
        ["20261101010000_AddDeliverySalePricePermission"] = "c36e0f82e85eb7517f4b653fcf1604d7c3fa1ae7a34d24bdecf4593d07741cef"
    };
    internal static bool IsMatch(string migrationId, string sql)
    {
        if (!Hashes.TryGetValue(migrationId, out var expected)) return false;
        var normalized = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))), expected, StringComparison.OrdinalIgnoreCase);
    }
}
