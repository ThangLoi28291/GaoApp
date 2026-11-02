using System.Security.Cryptography;
using GaoApp.Application.Common;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;

namespace GaoApp.Infrastructure.Services.Invoices;

public sealed record InvoiceProviderCredentialCheckResult(
    int SettingId, int StoreId, bool CanDecrypt, string? Error);

/// <summary>Reads stored ciphertext without migrating legacy passwords or issuing invoices.</summary>
public static class InvoiceProviderCredentialCheck
{
    public static async Task<IReadOnlyList<InvoiceProviderCredentialCheckResult>> RunAsync(
        IInvoiceProviderSettingRepository repository,
        IInvoiceProviderCredentialProtector protector,
        CancellationToken ct = default)
    {
        var settings = await repository.GetAllAsync(ct);
        var results = new List<InvoiceProviderCredentialCheckResult>();
        foreach (var setting in settings.Where(x => !x.IsDeleted && x.IsActive &&
                     string.Equals(x.ProviderCode, "VIETTEL", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var canDecrypt = !string.IsNullOrWhiteSpace(protector.Unprotect(setting.Password));
                results.Add(new(setting.Id, setting.StoreId, canDecrypt,
                    canDecrypt ? null : "Mật khẩu đã lưu đang trống."));
            }
            catch (CryptographicException ex)
            {
                results.Add(new(setting.Id, setting.StoreId, false,
                    InvoiceCredentialErrors.CannotDecrypt(ex,
                        "Worker không giải mã được mật khẩu Viettel đã lưu.").Message));
            }
        }
        return results;
    }
}
