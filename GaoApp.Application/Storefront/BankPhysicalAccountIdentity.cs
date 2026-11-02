using System.Security.Cryptography;
using System.Text;

namespace GaoApp.Application.Storefront;

// Exact shared canonical identity: no guessed bank aliases, no dropped punctuation or leading zeros.
public static class BankPhysicalAccountIdentity
{
    public static string Create(string bankCode, string account) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Concat((bankCode + "|" + account).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant())));
}
