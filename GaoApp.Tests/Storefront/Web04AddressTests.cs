using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.Extensions.Configuration;

namespace GaoApp.Tests.Storefront;

[Trait("Category", "StorefrontWeb04")]
public sealed class Web04AddressTests
{
    [Fact]
    public async Task Versioned_local_catalog_fingerprint_is_exact_file_bytes_and_labels_remain_human_readable()
    {
        var path = FilePath(); var text = Valid(); await File.WriteAllTextAsync(path, text);
        var first = await Catalog(path).GetAsync(); Assert.Equal("SYNTHETIC-W04", first.Version); Assert.Equal("Independent synthetic fixture", first.Source);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))), first.Fingerprint);
        Assert.Equal("Tỉnh thử nghiệm", first.Provinces.Single().Name); Assert.Equal("Phường thử nghiệm", first.Provinces.Single().Wards.Single().Name);
        await File.AppendAllTextAsync(path, " "); Assert.NotEqual(first.Fingerprint, (await Catalog(path).GetAsync()).Fingerprint);
    }
    [Theory]
    [InlineData("missing")][InlineData("relative")][InlineData("empty")][InlineData("json")][InlineData("null")][InlineData("noVersion")][InlineData("noSource")]
    [InlineData("noProvinces")][InlineData("nullProvinces")][InlineData("duplicateProvince")][InlineData("duplicateWard")][InlineData("crossProvinceDuplicateWard")]
    [InlineData("wardless")][InlineData("badCode")][InlineData("badLabel")][InlineData("controlLabel")][InlineData("large")][InlineData("provinceCount")][InlineData("wardCount")][InlineData("globalWardCount")]
    public async Task Missing_invalid_ambiguous_or_oversized_catalog_fails_closed(string invalid)
    {
        var path = FilePath(); var node = JsonNode.Parse(Valid())!; var provinces = node["provinces"]!.AsArray(); var province = provinces[0]!; var wards = province["wards"]!.AsArray();
        switch (invalid)
        {
            case "missing": await Assert.ThrowsAsync<ValidationAppException>(() => Catalog(path).GetAsync()); return;
            case "relative": await Assert.ThrowsAsync<ValidationAppException>(() => Catalog("relative-addresses.json").GetAsync()); return;
            case "empty": await File.WriteAllTextAsync(path, ""); break;
            case "json": await File.WriteAllTextAsync(path, "{bad"); break;
            case "null": await File.WriteAllTextAsync(path, "null"); break;
            case "noVersion": node["version"] = ""; break;
            case "noSource": node["source"] = ""; break;
            case "noProvinces": provinces.Clear(); break;
            case "nullProvinces": node["provinces"] = null; break;
            case "duplicateProvince": provinces.Add(province.DeepClone()); break;
            case "duplicateWard": wards.Add(wards[0]!.DeepClone()); break;
            case "crossProvinceDuplicateWard": var other = province.DeepClone(); other["code"] = "02"; provinces.Add(other); break;
            case "wardless": wards.Clear(); break;
            case "badCode": province["code"] = "../01"; break;
            case "badLabel": province["name"] = " "; break;
            case "controlLabel": wards[0]!["name"] = "bad\nlabel"; break;
            case "large": await File.WriteAllTextAsync(path, new string(' ', 8 * 1024 * 1024 + 1)); break;
            case "provinceCount": provinces.Clear(); for (var i = 0; i < 101; i++) provinces.Add(Province(i, 1)); break;
            case "wardCount": provinces.Clear(); provinces.Add(Province(1, 1001)); break;
            case "globalWardCount": provinces.Clear(); for (var i = 0; i < 11; i++) provinces.Add(Province(i, 1000)); break;
        }
        if (!File.Exists(path)) await File.WriteAllTextAsync(path, node.ToJsonString());
        await Assert.ThrowsAsync<ValidationAppException>(() => Catalog(path).GetAsync());
    }
    [Fact]
    public async Task Bundled_production_directory_matches_independently_audited_official_observation_and_has_unique_34_provinces_3321_wards()
    {
        var path = Path.Combine(Web03Support.Root(), "GaoApp.Storefront.Web/Data/vn-address-catalog-observed-20261031.json"); var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(287104, bytes.Length); Assert.Equal("688F1A761199A2315C3C3B0F68F642493A961D775E5ADA2233411291D518A23D", Convert.ToHexString(SHA256.HashData(bytes)));
        var directory = await Catalog(path).GetAsync(); Assert.Equal(34, directory.Provinces.Count); var wards = directory.Provinces.SelectMany(p => p.Wards).ToArray(); Assert.Equal(3321, wards.Length);
        Assert.Equal(34, directory.Provinces.Select(p => p.Code).Distinct().Count()); Assert.Equal(3321, wards.Select(w => w.Code).Distinct().Count());
        Assert.DoesNotContain("synthetic", directory.Source, StringComparison.OrdinalIgnoreCase); Assert.Contains("nso.gov.vn", directory.Source);
    }
    private static JsonObject Province(int index, int count) => new() { ["code"] = "P" + index, ["name"] = "Synthetic " + index,
        ["wards"] = new JsonArray(Enumerable.Range(0, count).Select(i => (JsonNode)new JsonObject { ["code"] = $"P{index}W{i}", ["name"] = "Synthetic ward " + i }).ToArray()) };
    private static string FilePath() => Web04Support.Artifact("runtime/" + Guid.NewGuid().ToString("N") + "/synthetic-addresses.json");
    private static string Valid() => """{"version":"SYNTHETIC-W04","source":"Independent synthetic fixture","provinces":[{"code":"01","name":"Tỉnh thử nghiệm","wards":[{"code":"01001","name":"Phường thử nghiệm"}]}]}""";
    private static StorefrontAddressCatalog Catalog(string path) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["StorefrontAddressCatalog:Path"] = path }).Build());
}
