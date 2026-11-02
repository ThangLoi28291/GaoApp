using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GaoApp.Tests.Storefront;

// Independently authored test geometry/images, not merchant content or a
// production procedural-model substitute. No production inspector is called.
internal static class Web08FixtureAssets
{
    internal static byte[] Png(int width, int height, bool headerOnly = false, bool solid = false)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        if (!headerOnly)
        {
            using var compressed = new MemoryStream();
            using (var zip = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[checked(width * 4 + 1)];
                for (var y = 0; y < height; y++)
                {
                    row[0] = 0;
                    for (var x = 0; x < width; x++)
                    {
                        var p = 1 + x * 4; row[p] = solid ? (byte)35 : (byte)((x * 31 + y * 3) % 256);
                        row[p + 1] = solid ? (byte)170 : (byte)((x * 7 + y * 29) % 256); row[p + 2] = solid ? (byte)75 : (byte)((x + y * 13) % 256); row[p + 3] = 255;
                    }
                    zip.Write(row);
                }
            }
            Chunk(output, "IDAT", compressed.ToArray());
        }
        Chunk(output, "IEND", []); return output.ToArray();
    }

    private static void Chunk(Stream stream, string kind, byte[] data)
    {
        Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, data.Length); stream.Write(length);
        var type = Encoding.ASCII.GetBytes(kind); stream.Write(type); stream.Write(data);
        uint crc = 0xffffffff;
        foreach (var value in type.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(length, crc ^ 0xffffffff); stream.Write(length);
    }

    internal static byte[] Model(bool binary = true)
    {
        // Eight asymmetric vertices and twelve triangles: deliberately not a
        // rotation-invariant sphere. UV/image fiducials expose real rotation.
        float[] positions = [-1, -.5f, -.4f, 1, -.5f, -.4f, 1, .9f, -.4f, -1, .5f, -.4f,
                             -1, -.5f, .4f, 1, -.5f, .4f, 1, .9f, .4f, -1, .5f, .4f];
        float[] uv = [0, 0, 1, 0, 1, 1, 0, 1, 0, 0, 1, 0, 1, 1, 0, 1];
        ushort[] indices = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4,
                            3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);
        foreach (var value in positions) writer.Write(value);
        var uvOffset = checked((int)buffer.Position); foreach (var value in uv) writer.Write(value);
        var indexOffset = checked((int)buffer.Position); foreach (var value in indices) writer.Write(value);
        var imageOffset = checked((int)buffer.Position); var png = Png(4, 4);
        if (binary) writer.Write(png);
        while (buffer.Length % 4 != 0) writer.Write((byte)0);
        var bytes = buffer.ToArray();
        var views = new List<object>
        {
            new { buffer = 0, byteOffset = 0, byteLength = positions.Length * 4, target = 34962 },
            new { buffer = 0, byteOffset = uvOffset, byteLength = uv.Length * 4, target = 34962 },
            new { buffer = 0, byteOffset = indexOffset, byteLength = indices.Length * 2, target = 34963 }
        };
        if (binary) views.Add(new { buffer = 0, byteOffset = imageOffset, byteLength = png.Length });
        object sourceBuffer = binary ? new { byteLength = bytes.Length } : new { byteLength = bytes.Length, uri = "data:application/octet-stream;base64," + Convert.ToBase64String(bytes) };
        object image = binary ? new { bufferView = 3, mimeType = "image/png" } : new { uri = "data:image/png;base64," + Convert.ToBase64String(png) };
        var document = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = "WEB08 independent synthetic fixture; not merchant media" },
            ["scene"] = 0, ["scenes"] = new[] { new { nodes = new[] { 0 } } }, ["nodes"] = new[] { new { mesh = 0 } },
            ["buffers"] = new[] { sourceBuffer }, ["bufferViews"] = views,
            ["accessors"] = new object[]
            {
                new { bufferView = 0, componentType = 5126, count = 8, type = "VEC3", min = new[] { -1f, -.5f, -.4f }, max = new[] { 1f, .9f, .4f } },
                new { bufferView = 1, componentType = 5126, count = 8, type = "VEC2" },
                new { bufferView = 2, componentType = 5123, count = 36, type = "SCALAR" }
            },
            ["images"] = new[] { image }, ["samplers"] = new[] { new { magFilter = 9729, minFilter = 9729 } },
            ["textures"] = new[] { new { source = 0, sampler = 0 } },
            ["materials"] = new[] { new { pbrMetallicRoughness = new { baseColorTexture = new { index = 0 }, metallicFactor = 0, roughnessFactor = .8 } } },
            ["meshes"] = new[] { new { name = "WEB08 asymmetric fixture", primitives = new[] { new { attributes = new Dictionary<string, int> { ["POSITION"] = 0, ["TEXCOORD_0"] = 1 }, indices = 2, material = 0, mode = 4 } } } }
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(document);
        if (!binary) return json;
        var paddedJson = (json.Length + 3) & ~3;
        using var glb = new MemoryStream(); using var file = new BinaryWriter(glb, Encoding.UTF8, leaveOpen: true);
        file.Write(0x46546c67u); file.Write(2u); file.Write(checked((uint)(12 + 8 + paddedJson + 8 + bytes.Length)));
        file.Write(paddedJson); file.Write(0x4e4f534au); file.Write(json);
        for (var i = json.Length; i < paddedJson; i++) file.Write((byte)' ');
        file.Write(bytes.Length); file.Write(0x004e4942u); file.Write(bytes);
        return glb.ToArray();
    }

    internal static byte[] MutateJson(byte[] gltf, Action<JsonElement, Dictionary<string, object?>> change)
    {
        using var parsed = JsonDocument.Parse(gltf);
        var properties = parsed.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value.Clone());
        change(parsed.RootElement, properties); return JsonSerializer.SerializeToUtf8Bytes(properties);
    }

    internal static byte[] OverlappingViewAllocationWitness()
    {
        const int span = 7_000_000, views = 128;
        var document = new
        {
            asset = new { version = "2.0", generator = "WEB08 bounded security witness; never submit to a scene loader" },
            scene = 0, scenes = new[] { new { nodes = new[] { 0 } } }, nodes = new[] { new { mesh = 0 } },
            buffers = new[] { new { byteLength = span } },
            bufferViews = Enumerable.Range(0, views).Select(_ => new { buffer = 0, byteOffset = 0, byteLength = span, target = 34962 }).ToArray(),
            accessors = Enumerable.Range(0, views).Select(i => new { bufferView = i, componentType = 5126, count = 3, type = "VEC3",
                min = new[] { 0f, 0f, 0f }, max = new[] { 1f, 1f, 0f } }).ToArray(),
            meshes = new[] { new { primitives = Enumerable.Range(0, views).Select(i => new { attributes = new Dictionary<string, int> { ["POSITION"] = i }, mode = 4 }).ToArray() } }
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(document); var padded = (json.Length + 3) & ~3;
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x46546c67u); writer.Write(2u); writer.Write(12 + 8 + padded + 8 + span);
        writer.Write(padded); writer.Write(0x4e4f534au); writer.Write(json);
        for (var i = json.Length; i < padded; i++) writer.Write((byte)' ');
        writer.Write(span); writer.Write(0x004e4942u);
        var binary = new byte[span]; float[] triangle = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        for (var i = 0; i < triangle.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(binary.AsSpan(i * 4), triangle[i]);
        writer.Write(binary);
        return output.ToArray();
    }

    internal static byte[] InterleavedModel(int padding = 4096, int instances = 1, int accessors = 1)
    {
        const int stride = 252; var length = checked(3 * stride + padding); Assert.Equal(0, length % 4);
        var document = new
        {
            asset = new { version = "2.0", generator = "WEB08 legal high-stride fixture; no runtime allocation claim" }, scene = 0,
            scenes = new[] { new { nodes = Enumerable.Range(0, instances).ToArray() } },
            nodes = Enumerable.Range(0, instances).Select(_ => new { mesh = 0 }).ToArray(),
            buffers = new[] { new { byteLength = length } }, bufferViews = Enumerable.Range(0, accessors).Select(_ => new { buffer = 0, byteOffset = 0, byteLength = length, byteStride = stride, target = 34962 }).ToArray(),
            accessors = Enumerable.Range(0, accessors).Select(i => new { bufferView = i, componentType = 5126, count = 3, type = "VEC3", min = new[] { 0f, 0f, 0f }, max = new[] { 1f, 1f, 0f } }).ToArray(),
            meshes = new[] { new { primitives = Enumerable.Range(0, accessors).Select(i => new { attributes = new Dictionary<string, int> { ["POSITION"] = i }, mode = 4 }).ToArray() } }
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(document); var padded = (json.Length + 3) & ~3;
        var binary = new byte[length]; float[][] points = [[0, 0, 0], [1, 0, 0], [0, 1, 0]];
        for (var vertex = 0; vertex < points.Length; vertex++) for (var axis = 0; axis < 3; axis++)
            BinaryPrimitives.WriteSingleLittleEndian(binary.AsSpan(vertex * stride + axis * 4), points[vertex][axis]);
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x46546c67u); writer.Write(2u); writer.Write(12 + 8 + padded + 8 + length);
        writer.Write(padded); writer.Write(0x4e4f534au); writer.Write(json);
        for (var i = json.Length; i < padded; i++) writer.Write((byte)' ');
        writer.Write(length); writer.Write(0x004e4942u); writer.Write(binary); return output.ToArray();
    }

    internal static byte[] GeometryModel(int vertices, int triangles)
    {
        Assert.True(vertices >= 3 && triangles >= 1);
        var positionBytes = checked(vertices * 12); var indexBytes = checked(triangles * 3 * 2);
        var length = (checked(positionBytes + indexBytes) + 3) & ~3; var bytes = new byte[length];
        float[] first = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        for (var i = 0; i < first.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), first[i]);
        for (var i = 0; i < triangles * 3; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(positionBytes + i * 2), (ushort)(i % 3));
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            asset = new { version = "2.0", generator = "WEB08 exact geometry-count boundary fixture" }, scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } }, nodes = new[] { new { mesh = 0 } },
            buffers = new[] { new { byteLength = bytes.Length } },
            bufferViews = new object[] { new { buffer = 0, byteOffset = 0, byteLength = positionBytes, target = 34962 }, new { buffer = 0, byteOffset = positionBytes, byteLength = indexBytes, target = 34963 } },
            accessors = new object[] { new { bufferView = 0, componentType = 5126, count = vertices, type = "VEC3", min = new[] { 0f, 0f, 0f }, max = new[] { 1f, 1f, 0f } },
                new { bufferView = 1, componentType = 5123, count = triangles * 3, type = "SCALAR" } },
            meshes = new[] { new { primitives = new[] { new { attributes = new Dictionary<string, int> { ["POSITION"] = 0 }, indices = 1, mode = 4 } } } }
        });
        var padded = (json.Length + 3) & ~3; using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x46546c67u); writer.Write(2u); writer.Write(12 + 8 + padded + 8 + bytes.Length);
        writer.Write(padded); writer.Write(0x4e4f534au); writer.Write(json); for (var i = json.Length; i < padded; i++) writer.Write((byte)' ');
        writer.Write(bytes.Length); writer.Write(0x004e4942u); writer.Write(bytes); return output.ToArray();
    }

    internal static byte[] TwoImageModel()
    {
        var root = JsonNode.Parse(Model(false))!;
        root["images"]!.AsArray().Add(new JsonObject { ["uri"] = "data:image/png;base64," + Convert.ToBase64String(Png(4, 4, solid: true)) });
        root["textures"]!.AsArray().Add(new JsonObject { ["source"] = 1, ["sampler"] = 0 });
        var second = root["materials"]![0]!.DeepClone(); second["pbrMetallicRoughness"]!["baseColorTexture"]!["index"] = 1; root["materials"]!.AsArray().Add(second);
        var primitive = root["meshes"]![0]!["primitives"]![0]!.DeepClone(); primitive["material"] = 1; root["meshes"]![0]!["primitives"]!.AsArray().Add(primitive);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }
}
