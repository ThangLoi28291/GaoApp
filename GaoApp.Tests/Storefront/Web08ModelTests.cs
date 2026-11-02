using System.Text;
using System.Text.Json.Nodes;
using GaoApp.Storefront.Web.Services;

namespace GaoApp.Tests.Storefront;

// Pure bounded preflight calls: no fixture/SQL/native codec/WebGL allocation.
[Trait("Category", "StorefrontWeb08")]
public sealed class Web08ModelTests
{
    [Theory]
    [InlineData(true)][InlineData(false)]
    public void Independent_asymmetric_embedded_fixture_reports_actual_geometry_texture_and_separate_resource_estimates(bool binary)
    {
        var result = Assert.IsType<StorefrontModelInspection>(StorefrontModelInspector.Inspect(Web08FixtureAssets.Model(binary), binary ? "model/gltf-binary" : "model/gltf+json"));
        Assert.Equal(1, result.Nodes); Assert.Equal(1, result.Primitives); Assert.Equal(8, result.Vertices); Assert.Equal(12, result.Triangles); Assert.Equal(16, result.TexturePixels);
        Assert.InRange(result.EstimatedGpuBytes, 1, 64 * 1024 * 1024); Assert.InRange(result.EstimatedWorkingBytes, result.EstimatedGpuBytes, 64 * 1024 * 1024);
    }

    [Theory]
    [InlineData("nodes")][InlineData("primitives")][InlineData("materials")]
    public void Exact_supported_parser_count_boundary_is_accepted_and_one_more_is_rejected_before_loader(string count)
    {
        byte[] At(int value)
        {
            var root = JsonNode.Parse(Web08FixtureAssets.Model(false))!;
            if (count == "nodes") { root["nodes"] = new JsonArray(Enumerable.Range(0, value).Select(_ => (JsonNode)new JsonObject { ["mesh"] = 0 }).ToArray()); root["scenes"]![0]!["nodes"] = new JsonArray(Enumerable.Range(0, value).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()); }
            if (count == "materials") root["materials"] = new JsonArray(Enumerable.Range(0, value).Select(_ => (JsonNode)new JsonObject()).ToArray());
            if (count == "primitives") { var primitive = root["meshes"]![0]!["primitives"]![0]!; root["meshes"]![0]!["primitives"] = new JsonArray(Enumerable.Range(0, value).Select(_ => primitive.DeepClone()).ToArray()); }
            return Encoding.UTF8.GetBytes(root.ToJsonString());
        }
        var limit = count == "nodes" ? 256 : count == "primitives" ? 128 : 32;
        Assert.NotNull(StorefrontModelInspector.Inspect(At(limit), "model/gltf+json")); Assert.Null(StorefrontModelInspector.Inspect(At(limit + 1), "model/gltf+json"));
    }

    [Theory]
    [InlineData("cycle")][InlineData("foreign-buffer")][InlineData("foreign-image")][InlineData("file")][InlineData("relative")]
    [InlineData("external")][InlineData("blob")][InlineData("skin")][InlineData("animation")][InlineData("morph")]
    [InlineData("required-decoder")][InlineData("overflow")][InlineData("accessor-span")][InlineData("version")][InlineData("sparse")][InlineData("camera")]
    public void Unsafe_references_unsupported_features_and_invalid_spans_fail_closed_without_decoding_or_loading(string reason)
    {
        var root = JsonNode.Parse(Web08FixtureAssets.Model(false))!;
        switch (reason)
        {
            case "cycle": root["nodes"]![0]!["children"] = new JsonArray(0); break;
            case "foreign-buffer": root["bufferViews"]![0]!["buffer"] = 99; break;
            case "foreign-image": root["textures"]![0]!["source"] = 99; break;
            case "file": root["buffers"]![0]!["uri"] = "file:///unowned/secret.bin"; break;
            case "relative": root["buffers"]![0]!["uri"] = "../foreign/model.bin"; break;
            case "external": root["images"]![0]!["uri"] = "https://unowned.invalid/texture.png"; break;
            case "blob": root["buffers"]![0]!["uri"] = "blob:https://unowned.invalid/opaque"; break;
            case "skin": root["skins"] = new JsonArray(new JsonObject()); root["nodes"]![0]!["skin"] = 0; break;
            case "animation": root["animations"] = new JsonArray(new JsonObject()); break;
            case "morph": root["meshes"]![0]!["primitives"]![0]!["targets"] = new JsonArray(new JsonObject { ["POSITION"] = 0 }); break;
            case "required-decoder": root["extensionsRequired"] = new JsonArray("KHR_draco_mesh_compression"); break;
            case "overflow": root["accessors"]![0]!["count"] = long.MaxValue; break;
            case "accessor-span": root["accessors"]![0]!["byteOffset"] = 90; break;
            case "version": root["asset"]!["version"] = "1.0"; break;
            case "sparse": root["accessors"]![0]!["sparse"] = new JsonObject { ["count"] = 1 }; break;
            case "camera": root["cameras"] = new JsonArray(new JsonObject { ["type"] = "unsupported-panorama" }); root["nodes"]![0]!["camera"] = 0; break;
        }
        Assert.Null(StorefrontModelInspector.Inspect(Encoding.UTF8.GetBytes(root.ToJsonString()), "model/gltf+json"));
    }

    [Theory]
    [InlineData(100000, 1, true)][InlineData(100001, 1, false)][InlineData(3, 100000, true)][InlineData(3, 100001, false)]
    public void Exact_geometry_budgets_accept_limit_and_reject_one_more_with_real_consistent_buffer_spans(int vertices, int triangles, bool accepted)
    {
        var bytes = Web08FixtureAssets.GeometryModel(vertices, triangles); // Bounded <=1.7MiB; never a native scene.
        var result = StorefrontModelInspector.Inspect(bytes, "model/gltf-binary");
        if (accepted) { Assert.NotNull(result); Assert.Equal(vertices, result.Vertices); Assert.Equal(triangles, result.Triangles); }
        else Assert.Null(result);
    }

    [Theory]
    [InlineData("json")][InlineData("aggregate")][InlineData("glb-length")][InlineData("unknown-mime")]
    public void Encoded_JSON_container_and_MIME_limits_are_enforced_before_loader(string reason)
    {
        byte[] bytes; var mime = "model/gltf+json";
        if (reason == "json") { var root = JsonNode.Parse(Web08FixtureAssets.Model(false))!; root["extras"] = new string('x', 1024 * 1024); bytes = Encoding.UTF8.GetBytes(root.ToJsonString()); Assert.True(bytes.Length < 8 * 1024 * 1024); }
        else if (reason == "aggregate") { bytes = new byte[8 * 1024 * 1024 + 1]; Web08FixtureAssets.Model().CopyTo(bytes, 0); mime = "model/gltf-binary"; }
        else if (reason == "glb-length") { bytes = Web08FixtureAssets.Model()[..^4]; mime = "model/gltf-binary"; }
        else { bytes = Web08FixtureAssets.Model(); mime = "application/octet-stream"; }
        Assert.Null(StorefrontModelInspector.Inspect(bytes, mime));
    }

    [Theory]
    [InlineData(2048, 2048, 1, true)][InlineData(2049, 16, 1, false)][InlineData(2000, 2000, 2, true)][InlineData(2001, 2000, 2, false)]
    public void Real_compressed_texture_headers_obey_edge_and_aggregate_decoded_pixel_budgets(int width, int height, int count, bool accepted)
    {
        var root = JsonNode.Parse(Web08FixtureAssets.Model(false))!;
        var image = "data:image/png;base64," + Convert.ToBase64String(Web08FixtureAssets.Png(width, height, solid: true));
        root["images"] = new JsonArray(Enumerable.Range(0, count).Select(_ => (JsonNode)new JsonObject { ["uri"] = image }).ToArray());
        root["textures"] = new JsonArray(Enumerable.Range(0, count).Select(x => (JsonNode)new JsonObject { ["source"] = x }).ToArray());
        if (count == 2) root["materials"]![0]!["pbrMetallicRoughness"]!["metallicRoughnessTexture"] = new JsonObject { ["index"] = 1 };
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString()); Assert.True(bytes.Length < 1024 * 1024);
        var result = StorefrontModelInspector.Inspect(bytes, "model/gltf+json");
        if (accepted) { Assert.NotNull(result); Assert.Equal((long)width * height * count, result.TexturePixels); } else Assert.Null(result);
    }
}
