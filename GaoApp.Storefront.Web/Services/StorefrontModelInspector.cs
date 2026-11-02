using System.Buffers.Binary;
using System.Text.Json;

namespace GaoApp.Storefront.Web.Services;

public sealed record StorefrontModelInspection(string Format, int Nodes, int Primitives, long Vertices,
    long Triangles, long TexturePixels, long EstimatedGpuBytes)
{
    public long EstimatedWorkingBytes { get; init; }
}

// Static product assets only. No resource URI is delegated to a loader without inspection.
public static class StorefrontModelInspector
{
    private static readonly HashSet<string> Extensions = ["KHR_materials_unlit", "KHR_texture_transform", "EXT_texture_webp"];

    public static StorefrontModelInspection? Inspect(ReadOnlyMemory<byte> bytes, string contentType)
    {
        if (bytes.Length is <= 0 or > StorefrontVisualLimits.ModelTransferBytes) return null;
        try
        {
            ReadOnlyMemory<byte> json = bytes, binary = default;
            var format = "gltf";
            if (contentType == "model/gltf-binary")
            {
                format = "glb";
                Need(bytes.Length >= 20 && U32(bytes.Span, 0) == 0x46546c67 && U32(bytes.Span, 4) == 2 && U32(bytes.Span, 8) == bytes.Length);
                var offset = 12; var chunks = 0;
                while (offset < bytes.Length)
                {
                    Need(offset <= bytes.Length - 8);
                    var length = checked((int)U32(bytes.Span, offset)); var kind = U32(bytes.Span, offset + 4);
                    Need(length > 0 && length % 4 == 0 && (long)offset + 8 + length <= bytes.Length);
                    if (chunks == 0) { Need(kind == 0x4e4f534a); json = bytes.Slice(offset + 8, length); }
                    else { Need(chunks == 1 && kind == 0x004e4942); binary = bytes.Slice(offset + 8, length); }
                    chunks++; offset = checked(offset + 8 + length);
                }
                Need(chunks is 1 or 2);
            }
            else Need(contentType == "model/gltf+json");
            Need(json.Length <= StorefrontVisualLimits.ModelJsonBytes);
            if (json.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) json = json[3..];
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement; Need(root.ValueKind == JsonValueKind.Object);
            Need(root.TryGetProperty("asset", out var asset) && Text(asset, "version") == "2.0");
            CheckTree(root);
            Need(Items(root, "skins", 256).Length == 0 && Items(root, "animations", 256).Length == 0);
            return Validate(root, binary, format, bytes.Length, json.Length);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or OverflowException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return null; }
    }

    private static StorefrontModelInspection Validate(JsonElement root, ReadOnlyMemory<byte> binary, string format, int encodedBytes, int jsonBytes)
    {
        var bufferElements = Items(root, "buffers", 256); Need(bufferElements.Length > 0);
        var buffers = new ReadOnlyMemory<byte>[bufferElements.Length]; long decodedBufferBytes = 0;
        for (var i = 0; i < buffers.Length; i++)
        {
            var declared = Int(bufferElements[i], "byteLength", -1); Need(declared > 0);
            if (bufferElements[i].TryGetProperty("uri", out var uri))
            {
                var decoded = Data(uri.GetString()!, ["application/octet-stream", "application/gltf-buffer"], out _);
                Need(decoded.Length == declared); buffers[i] = decoded;
            }
            else { Need(i == 0 && binary.Length >= declared && binary.Length - declared <= 3); buffers[i] = binary[..declared]; }
            decodedBufferBytes = checked(decodedBufferBytes + buffers[i].Length);
            Need(decodedBufferBytes <= StorefrontVisualLimits.ModelTransferBytes);
        }
        var viewElements = Items(root, "bufferViews", 4096);
        var views = new View[viewElements.Length]; long viewCopyBytes = 0;
        for (var i = 0; i < views.Length; i++)
        {
            var v = viewElements[i]; var buffer = Int(v, "buffer", -1); var offset = Int(v, "byteOffset", 0); var length = Int(v, "byteLength", -1);
            Need(buffer >= 0 && buffer < buffers.Length && offset >= 0 && length > 0 && (long)offset + length <= buffers[buffer].Length);
            // r187 copies each distinct used bufferView, including its padding. Count
            // every declared view conservatively; overlaps do not share this budget.
            viewCopyBytes = checked(viewCopyBytes + length); Need(viewCopyBytes <= StorefrontVisualLimits.ModelWorkingBytes);
            var stride = Int(v, "byteStride", 0); Need(stride == 0 || stride is >= 4 and <= 252 && stride % 4 == 0);
            views[i] = new(buffers[buffer].Slice(offset, length), stride);
        }
        var elements = Items(root, "accessors", 4096);
        var accessors = new Accessor[elements.Length]; long geometryBytes = 0;
        for (var i = 0; i < accessors.Length; i++)
        {
            var a = elements[i]; Need(!a.TryGetProperty("sparse", out _));
            var view = Int(a, "bufferView", -1); var offset = Int(a, "byteOffset", 0); var count = Int(a, "count", -1); var component = Int(a, "componentType", -1);
            var size = component switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => 0 };
            var dimensions = Text(a, "type") switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => 0 };
            Need(view >= 0 && view < views.Length && count > 0 && offset >= 0 && size > 0 && dimensions > 0 && offset % size == 0);
            var stride = views[view].Stride == 0 ? size * dimensions : views[view].Stride;
            Need(stride >= size * dimensions && stride % size == 0 && checked((long)offset + (long)(count - 1) * stride + size * dimensions) <= views[view].Bytes.Length);
            // Interleaved attributes upload their backing array, not just the named
            // components. Count the full view for each such accessor conservatively;
            // sharing/overlap never discounts stride or padding exposure.
            var accessorExposure = Math.Max((long)count * dimensions * 4, views[view].Stride == 0 ? 0 : views[view].Bytes.Length);
            geometryBytes = checked(geometryBytes + accessorExposure);
            Need(geometryBytes <= StorefrontVisualLimits.EstimatedGpuBytes);
            if (component == 5126)
                for (var n = 0; n < count; n++) for (var d = 0; d < dimensions; d++)
                    Need(float.IsFinite(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(views[view].Bytes.Span[(offset + n * stride + d * 4)..]))));
            accessors[i] = new(views[view].Bytes, offset, stride, count, component, dimensions);
        }
        var images = Items(root, "images", 256);
        var imageInfo = new StorefrontImageInfo[images.Length]; long encodedImageBytes = 0;
        for (var i = 0; i < images.Length; i++)
        {
            var image = images[i]; ReadOnlyMemory<byte> content; string? mime;
            if (image.TryGetProperty("uri", out var uri))
            {
                Need(!image.TryGetProperty("bufferView", out _)); content = Data(uri.GetString()!, ["image/png", "image/jpeg", "image/webp"], out var dataMime); mime = dataMime;
                if (image.TryGetProperty("mimeType", out var declared)) Need(declared.GetString() == mime);
            }
            else
            {
                var view = Int(image, "bufferView", -1); Need(view >= 0 && view < views.Length);
                content = views[view].Bytes; mime = Text(image, "mimeType");
            }
            Need(StorefrontImageHeader.TryRead(content.Span, out var info) && !info.IsAnimated && info.ContentType == mime &&
                mime is "image/png" or "image/jpeg" or "image/webp" && info.Width <= StorefrontVisualLimits.TextureEdge && info.Height <= StorefrontVisualLimits.TextureEdge);
            imageInfo[i] = info;
            encodedImageBytes = checked(encodedImageBytes + content.Length);
        }
        var samplers = Items(root, "samplers", 256);
        foreach (var sampler in samplers)
        {
            Need(Int(sampler, "magFilter", 9729) is 9728 or 9729);
            Need(Int(sampler, "minFilter", 9987) is 9728 or 9729 or 9984 or 9985 or 9986 or 9987);
            Need(Int(sampler, "wrapS", 10497) is 33071 or 33648 or 10497);
            Need(Int(sampler, "wrapT", 10497) is 33071 or 33648 or 10497);
        }
        var textures = Items(root, "textures", 256); long pixels = 0;
        foreach (var texture in textures)
        {
            var source = Int(texture, "source", -1);
            var sampler = Int(texture, "sampler", -1); Need(sampler == -1 || sampler >= 0 && sampler < samplers.Length);
            if (texture.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("EXT_texture_webp", out var webp))
            { source = Int(webp, "source", -1); Need(source >= 0 && source < imageInfo.Length && imageInfo[source].ContentType == "image/webp"); }
            Need(source >= 0 && source < imageInfo.Length); pixels = checked(pixels + imageInfo[source].Pixels);
            Need(pixels <= StorefrontVisualLimits.TexturePixels);
        }
        // Also bound unreferenced embedded images before a loader can decode them.
        Need(imageInfo.Sum(x => x.Pixels) <= StorefrontVisualLimits.TexturePixels);
        // Preflight known loader-visible CPU exposure before GLTFLoader/bitmap decode:
        // transfer, JSON text, decoded buffers, view slices, possible accessor copies,
        // image Blob bytes and decoded image pixels. Not a total JS/driver heap claim.
        var working = checked(encodedBytes + 2L * jsonBytes + decodedBufferBytes + viewCopyBytes +
            2 * geometryBytes + encodedImageBytes + 4 * imageInfo.Sum(x => x.Pixels));
        Need(working <= StorefrontVisualLimits.ModelWorkingBytes);
        var gpu = checked(geometryBytes + (pixels * 16 + 2) / 3); Need(gpu <= StorefrontVisualLimits.EstimatedGpuBytes);
        var materials = Items(root, "materials", StorefrontVisualLimits.ModelMaterials);
        foreach (var material in materials) CheckMaterialTextures(material, textures.Length);
        var meshes = Items(root, "meshes", StorefrontVisualLimits.ModelPrimitives); Need(meshes.Length > 0);
        var meshStats = new (long Vertices, long Triangles)[meshes.Length]; var primitiveCount = 0;
        for (var m = 0; m < meshes.Length; m++)
        {
            Need(Items(meshes[m], "weights", 256).Length == 0);
            var primitives = Items(meshes[m], "primitives", StorefrontVisualLimits.ModelPrimitives); Need(primitives.Length > 0);
            foreach (var primitive in primitives)
            {
                Need(++primitiveCount <= StorefrontVisualLimits.ModelPrimitives && Int(primitive, "mode", 4) == 4 && Items(primitive, "targets", 256).Length == 0);
                Need(primitive.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object);
                var position = Int(attributes, "POSITION", -1); Need(position >= 0 && position < accessors.Length);
                var pos = accessors[position]; Need(pos.Component == 5126 && pos.Dimensions == 3 && pos.Count <= StorefrontVisualLimits.ModelVertices);
                foreach (var attribute in attributes.EnumerateObject())
                { Need(attribute.Value.TryGetInt32(out var index) && index >= 0 && index < accessors.Length && accessors[index].Count == pos.Count); }
                var material = Int(primitive, "material", -1); Need(material == -1 || material >= 0 && material < materials.Length);
                var indices = Int(primitive, "indices", -1); long triangleCount;
                if (indices >= 0)
                {
                    Need(indices < accessors.Length); var a = accessors[indices]; Need(a.Dimensions == 1 && a.Component is 5121 or 5123 or 5125 && a.Count % 3 == 0);
                    for (var n = 0; n < a.Count; n++)
                    {
                        var span = a.Bytes.Span[(a.Offset + n * a.Stride)..];
                        var value = a.Component == 5121 ? span[0] : a.Component == 5123 ? BinaryPrimitives.ReadUInt16LittleEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);
                        Need(value < pos.Count);
                    }
                    triangleCount = a.Count / 3;
                }
                else { Need(pos.Count % 3 == 0); triangleCount = pos.Count / 3; }
                meshStats[m] = (checked(meshStats[m].Vertices + pos.Count), checked(meshStats[m].Triangles + triangleCount));
            }
        }
        var nodes = Items(root, "nodes", StorefrontVisualLimits.ModelNodes); Need(nodes.Length > 0);
        var cameras = Items(root, "cameras", StorefrontVisualLimits.ModelNodes);
        // Embedded cameras are not used for framing, but GLTFLoader still parses
        // them. Validate their supported shapes before any loader/decode work.
        foreach (var camera in cameras)
        {
            if (camera.TryGetProperty("name", out var name)) Need(name.ValueKind == JsonValueKind.String);
            var kind = Text(camera, "type"); Need(kind is "perspective" or "orthographic");
            Need(camera.TryGetProperty(kind!, out var parameters) && parameters.ValueKind == JsonValueKind.Object);
            var near = Number(parameters, "znear", -1);
            var far = Number(parameters, "zfar", double.PositiveInfinity);
            if (kind == "perspective") Need(near > 0 && far > near && Number(parameters, "yfov", -1) is > 0 and < Math.PI && Number(parameters, "aspectRatio", 1) > 0);
            else Need(near >= 0 && double.IsFinite(far) && far > near && Number(parameters, "xmag", -1) > 0 && Number(parameters, "ymag", -1) > 0);
        }
        var children = new int[nodes.Length][]; var incoming = new int[nodes.Length]; var nodeMeshes = new int[nodes.Length];
        for (var i = 0; i < nodes.Length; i++)
        {
            Need(!nodes[i].TryGetProperty("skin", out _) && Items(nodes[i], "weights", 256).Length == 0);
            var camera = Int(nodes[i], "camera", -1); Need(camera == -1 || camera >= 0 && camera < cameras.Length);
            nodeMeshes[i] = Int(nodes[i], "mesh", -1); Need(nodeMeshes[i] == -1 || nodeMeshes[i] >= 0 && nodeMeshes[i] < meshes.Length);
            children[i] = Items(nodes[i], "children", StorefrontVisualLimits.ModelNodes).Select(x => x.GetInt32()).ToArray();
            foreach (var child in children[i]) { Need(child >= 0 && child < nodes.Length); Need(++incoming[child] <= 1); }
            foreach (var (name, length) in new[] { ("matrix", 16), ("translation", 3), ("rotation", 4), ("scale", 3) })
                if (nodes[i].TryGetProperty(name, out var transform)) Need(transform.ValueKind == JsonValueKind.Array && transform.GetArrayLength() == length && transform.EnumerateArray().All(x => x.TryGetDouble(out var value) && double.IsFinite(value)));
        }
        var visited = new byte[nodes.Length];
        void Cycle(int id) { Need(visited[id] != 1); if (visited[id] == 2) return; visited[id] = 1; foreach (var child in children[id]) Cycle(child); visited[id] = 2; }
        for (var i = 0; i < nodes.Length; i++) Cycle(i);
        var scenes = Items(root, "scenes", StorefrontVisualLimits.ModelNodes); var selected = Int(root, "scene", 0); Need(scenes.Length > 0 && selected >= 0 && selected < scenes.Length);
        long vertices = 0, triangles = 0; var instantiatedNodes = 0; var selectedMeshes = 0;
        for (var s = 0; s < scenes.Length; s++)
        {
            var seen = new HashSet<int>();
            void Visit(int id)
            {
                Need(id >= 0 && id < nodes.Length && seen.Add(id)); Need(++instantiatedNodes <= StorefrontVisualLimits.ModelNodes);
                if (nodeMeshes[id] >= 0)
                {
                    var stats = meshStats[nodeMeshes[id]]; vertices = checked(vertices + stats.Vertices); triangles = checked(triangles + stats.Triangles);
                    Need(vertices <= StorefrontVisualLimits.ModelVertices && triangles <= StorefrontVisualLimits.ModelTriangles);
                    if (s == selected) selectedMeshes++;
                }
                foreach (var child in children[id]) Visit(child);
            }
            foreach (var id in Items(scenes[s], "nodes", StorefrontVisualLimits.ModelNodes)) { var index = id.GetInt32(); Need(index >= 0 && index < nodes.Length && incoming[index] == 0); Visit(index); }
        }
        Need(selectedMeshes > 0 && triangles > 0);
        return new(format, instantiatedNodes, primitiveCount, vertices, triangles, pixels, gpu) { EstimatedWorkingBytes = working };
    }

    private static void CheckTree(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                Need(property.Name is not ("__proto__" or "constructor" or "prototype"));
                if (property.Name == "uri")
                {
                    Need(property.Value.ValueKind == JsonValueKind.String); var uri = property.Value.GetString()!;
                    Need(uri.StartsWith("data:", StringComparison.Ordinal) && uri.Contains(";base64,", StringComparison.Ordinal) && !uri.Any(char.IsControl));
                }
                if (property.Name == "extensions") { Need(property.Value.ValueKind == JsonValueKind.Object); foreach (var ext in property.Value.EnumerateObject()) Need(Extensions.Contains(ext.Name)); }
                if (property.Name is "extensionsUsed" or "extensionsRequired") { Need(property.Value.ValueKind == JsonValueKind.Array); foreach (var ext in property.Value.EnumerateArray()) Need(ext.ValueKind == JsonValueKind.String && Extensions.Contains(ext.GetString()!)); }
                CheckTree(property.Value);
            }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) CheckTree(item);
        else if (value.ValueKind == JsonValueKind.Number) Need(value.TryGetDouble(out var number) && double.IsFinite(number));
    }
    private static void CheckMaterialTextures(JsonElement value, int count)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name.EndsWith("Texture", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.Object)
            { var index = Int(property.Value, "index", -1); Need(index >= 0 && index < count); }
            CheckMaterialTextures(property.Value, count);
        }
    }
    private static byte[] Data(string uri, string[] allowed, out string mime)
    {
        var comma = uri.IndexOf(','); Need(comma > 12 && uri.StartsWith("data:", StringComparison.Ordinal) && uri[..comma].EndsWith(";base64", StringComparison.Ordinal));
        mime = uri[5..(comma - 7)]; Need(allowed.Contains(mime, StringComparer.Ordinal));
        var data = uri[(comma + 1)..]; Need(data.Length > 0 && !data.Any(char.IsWhiteSpace));
        var bytes = Convert.FromBase64String(data); Need(bytes.Length <= StorefrontVisualLimits.ModelTransferBytes); return bytes;
    }
    private static JsonElement[] Items(JsonElement value, string name, int max)
    {
        if (!value.TryGetProperty(name, out var array)) return [];
        Need(array.ValueKind == JsonValueKind.Array && array.GetArrayLength() <= max); return array.EnumerateArray().ToArray();
    }
    private static int Int(JsonElement value, string name, int fallback)
    { if (!value.TryGetProperty(name, out var item)) return fallback; Need(item.TryGetInt32(out var number)); return number; }
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
    private static double Number(JsonElement value, string name, double fallback)
    { if (!value.TryGetProperty(name, out var item)) return fallback; Need(item.TryGetDouble(out var number) && double.IsFinite(number)); return number; }
    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static void Need(bool condition) { if (!condition) throw new InvalidDataException("Model is outside the supported bounded format."); }
    private readonly record struct View(ReadOnlyMemory<byte> Bytes, int Stride);
    private readonly record struct Accessor(ReadOnlyMemory<byte> Bytes, int Offset, int Stride, int Count, int Component, int Dimensions);
}
