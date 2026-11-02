using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using Microsoft.Extensions.Configuration;

namespace GaoApp.Infrastructure.Services.Storefront;

// Trusted, versioned local directory. Never downloads or creates geography at runtime.
public sealed class StorefrontAddressCatalog(IConfiguration configuration) : IStorefrontAddressCatalog
{
    private sealed class Document
    {
        public string Version { get; set; } = "";
        public string Source { get; set; } = "";
        public List<Province> Provinces { get; set; } = [];
    }
    private sealed class Province
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public List<Ward> Wards { get; set; } = [];
    }
    private sealed class Ward
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
    }
    public async Task<StorefrontAddressDirectory> GetAsync(CancellationToken ct = default)
    {
        const string message = "Danh mục tỉnh/thành, phường/xã chưa sẵn sàng. Cửa hàng cần kiểm tra cấu hình trước khi nhận đơn.";
        var path = configuration["StorefrontAddressCatalog:Path"];
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ValidationAppException(message);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            if (stream.Length is <= 0 or > 8 * 1024 * 1024) throw new ValidationAppException(message);
            var bytes = new byte[(int)stream.Length]; await stream.ReadExactlyAsync(bytes, ct);
            var document = JsonSerializer.Deserialize<Document>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 10 });
            if (document is null || !Text(document.Version, 100) || !Text(document.Source, 1000) || document.Provinces is null ||
                document.Provinces.Count is < 1 or > 100 || document.Provinces.Any(x => x is null || !Code(x.Code) || !Text(x.Name, 200) || x.Wards is null ||
                    x.Wards.Count is < 1 or > 1000 || x.Wards.Any(w => w is null || !Code(w.Code) || !Text(w.Name, 200))) ||
                document.Provinces.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != document.Provinces.Count)
                throw new ValidationAppException(message);
            var wards = document.Provinces.SelectMany(x => x.Wards).ToList();
            if (wards.Count > 10000 || wards.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != wards.Count) throw new ValidationAppException(message);
            return new(document.Version, document.Source, Convert.ToHexString(SHA256.HashData(bytes)), document.Provinces
                .OrderBy(x => x.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("vi-VN"), true))
                .Select(x => new StorefrontAddressProvince(x.Code, x.Name, x.Wards.OrderBy(w => w.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("vi-VN"), true))
                    .Select(w => new StorefrontAddressWard(w.Code, w.Name)).ToList())).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        { throw new ValidationAppException(message); }
    }
    private static bool Code(string? value) => value is not null && Regex.IsMatch(value, "^[A-Z0-9_-]{1,20}$");
    private static bool Text(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit && value == value.Trim() && !value.Any(char.IsControl);
}
