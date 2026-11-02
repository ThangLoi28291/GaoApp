using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using GaoApp.Domain.Storefront;
using GaoApp.Storefront.Web.Services;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb08"), Trait("Category", "StorefrontWeb08")]
public sealed class Web08MediaTests(Web08Fixture fixture)
{
    [Fact]
    public async Task Actual_JPEG_derivatives_preserve_all_eight_EXIF_orientation_and_mirror_pixel_markers()
    {
        var c = await Web08Case.CreateAsync(fixture, models: false);
        var data = Path.Combine(Web08Support.Root(), "GaoApp.Tests/Storefront/Fixtures/Web08");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(data, "orientation-fixtures.json")));
        var vectors = new List<object>(); var request = await c.Seed.SavedAsync();
        foreach (var vector in manifest.RootElement.GetProperty("Fixtures").EnumerateArray())
        {
            var name = vector.GetProperty("File").GetString()!; var bytes = await File.ReadAllBytesAsync(Path.Combine(data, name));
            Assert.Equal(vector.GetProperty("SHA256").GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
            var id = await c.AssetAsync(name, "image/jpeg", bytes);
            request.Media.Add(new() { MediaAssetId = id, Kind = StorefrontMediaKind.Image, AltText = "Independent EXIF marker " + name });
            vectors.Add(new { id, width = vector.GetProperty("ExpectedDerivativeWidth").GetInt32(), height = vector.GetProperty("ExpectedDerivativeHeight").GetInt32(),
                orientation = vector.GetProperty("ExifOrientation").GetInt32(), corners = vector.GetProperty("ExpectedCornersTLTRBLBR").Clone() });
        }
        await using (var db = c.Context()) await c.Seed.Products(db).SaveAsync(c.ProductId, request);
        await using var host = await Web08MediaHost.StartAsync(c); var output = Web08Support.NewDirectory("WEB08 real browser decodes actual HTTP EXIF derivative pixels");
        var before = await c.Web.CountsAsync();
        using var report = await Web08Node.RunAsync(new { mode = "orientation", baseUrl = host.Http.BaseAddress!.ToString().TrimEnd('/'), output, vectors }, output, "Actual Windows Skia HTTP output decoded by Chromium");
        Assert.Equal(8, report.RootElement.GetProperty("pixels").GetArrayLength()); Assert.Equal(before, await c.Web.CountsAsync());
    }

    [Theory]
    [InlineData(160, 160, 80)][InlineData(320, 320, 160)][InlineData(640, 640, 320)][InlineData(960, 640, 320)]
    public async Task Actual_image_derivative_decodes_to_requested_bounded_aspect_without_upscaling_and_without_business_effects(int width, int expectedWidth, int expectedHeight)
    {
        var c = await Web08Case.CreateAsync(fixture, models: false); await using var host = await Web08MediaHost.StartAsync(c);
        var before = await c.Web.CountsAsync();
        using var response = await host.Http.GetAsync($"/shop/media/{c.ImageId}?width={width}"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore); Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var bytes = await response.Content.ReadAsByteArrayAsync(); var dimensions = Dimensions(bytes);
        Assert.Equal((expectedWidth, expectedHeight), dimensions); Assert.Equal(before, await c.Web.CountsAsync());
    }

    [Theory]
    [InlineData("0")][InlineData("159")][InlineData("161")][InlineData("1280")][InlineData("bad")][InlineData("160&width=320")]
    public async Task Actual_route_rejects_nonallowlisted_or_duplicate_width_instead_of_allocating_arbitrary_output(string width)
    {
        var c = await Web08Case.CreateAsync(fixture, models: false); await using var host = await Web08MediaHost.StartAsync(c);
        using var response = await host.Http.GetAsync($"/shop/media/{c.ImageId}?width={width}"); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("edge")][InlineData("pixels")][InlineData("input-bytes")][InlineData("unknown-header")]
    public async Task Actual_route_rejects_raw_image_bounds_or_unknown_header_before_native_decode_and_never_returns_unsafe_original(string bound)
    {
        var c = await Web08Case.CreateAsync(fixture, models: false);
        byte[] bytes = bound switch
        {
            "edge" => Web08FixtureAssets.Png(8193, 1, headerOnly: true),
            "pixels" => Web08FixtureAssets.Png(4001, 4000, headerOnly: true),
            "input-bytes" => new byte[20 * 1024 * 1024 + 1],
            _ => Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>not an image decoder input</script></svg>")
        };
        if (bound == "input-bytes") Web08FixtureAssets.Png(1, 1).CopyTo(bytes, 0);
        var id = await c.AssetAsync("unsafe-" + bound + ".png", "image/png", bytes);
        await using (var db = c.Context())
        {
            var request = await c.Seed.SavedAsync(); request.Media.Add(new() { MediaAssetId = id, Kind = StorefrontMediaKind.Image }); await c.Seed.Products(db).SaveAsync(c.ProductId, request);
        }
        await using var host = await Web08MediaHost.StartAsync(c); var before = await c.Web.CountsAsync();
        foreach (var path in new[] { "/shop/media/" + id, $"/shop/media/{id}?width=160" })
        { using var response = await host.Http.GetAsync(path); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
        Assert.Equal(before, await c.Web.CountsAsync());
    }

    [Fact]
    public async Task Real_selfcontained_GLB_and_embedded_gltf_are_served_exactly_and_model_width_is_not_an_image_conversion()
    {
        var c = await Web08Case.CreateAsync(fixture); await using var host = await Web08MediaHost.StartAsync(c);
        foreach (var (id, binary, mime) in new[] { (c.GlbId, true, "model/gltf-binary"), (c.GltfId, false, "model/gltf+json") })
        {
            using var response = await host.Http.GetAsync("/shop/media/" + id); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(mime, response.Content.Headers.ContentType!.MediaType); Assert.Equal(Web08FixtureAssets.Model(binary), await response.Content.ReadAsByteArrayAsync());
            Assert.True(response.Headers.CacheControl?.NoStore); Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            using var width = await host.Http.GetAsync($"/shop/media/{id}?width=160"); Assert.Equal(HttpStatusCode.NotFound, width.StatusCode);
        }
    }

    [Fact]
    public async Task Warm_image_and_model_reads_revalidate_current_publication_and_fixed_server_store_scope()
    {
        var c = await Web08Case.CreateAsync(fixture); var foreign = await Web08Case.CreateAsync(fixture);
        await using var host = await Web08MediaHost.StartAsync(c);
        using (var warm = await host.Http.GetAsync($"/shop/media/{c.ImageId}?width=160")) Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        using (var warm = await host.Http.GetAsync("/shop/media/" + c.GlbId)) Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        host.SelectOwnedStore(foreign.Seed.StoreId);
        foreach (var path in new[] { $"/shop/media/{c.ImageId}?width=160", "/shop/media/" + c.GlbId })
        { using var denied = await host.Http.GetAsync(path); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); }
        host.SelectOwnedStore(c.Seed.StoreId);
        await using (var db = c.Context())
        {
            var saved = await c.Seed.Products(db).GetAsync(c.ProductId);
            var changed = await c.Seed.Products(db).SetPublicationAsync(new() { ProductId = c.ProductId, IsPublished = false, Version = saved.Product.Version });
            Assert.Equal(GaoApp.Application.Storefront.StorefrontPublicationOutcome.Updated, changed.Outcome);
        }
        foreach (var path in new[] { $"/shop/media/{c.ImageId}?width=160", "/shop/media/" + c.GlbId })
        { using var denied = await host.Http.GetAsync(path); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); }
    }

    [Theory]
    [InlineData("external")][InlineData("relative")][InlineData("blob")][InlineData("malformed")][InlineData("truncated")]
    [InlineData("nodes")][InlineData("materials")][InlineData("unsupportedCodec")]
    public async Task Unsupported_unsafe_or_parser_overbudget_models_are_denied_by_actual_media_route(string kind)
    {
        var c = await Web08Case.CreateAsync(fixture); byte[] bytes;
        if (kind == "malformed") bytes = Encoding.UTF8.GetBytes("{not a model");
        else if (kind == "truncated") bytes = Web08FixtureAssets.Model(false)[..40];
        else
        {
            var root = JsonNode.Parse(Web08FixtureAssets.Model(false))!;
            if (kind is "external" or "relative" or "blob") root["buffers"]![0]!["uri"] = kind switch
            { "external" => "https://unowned.invalid/private.bin", "relative" => "../foreign/private.bin", _ => "blob:https://unowned.invalid/opaque" };
            if (kind == "nodes") root["nodes"] = new JsonArray(Enumerable.Range(0, 257).Select(i => (JsonNode)(i == 0 ? new JsonObject { ["mesh"] = 0 } : new JsonObject())).ToArray());
            if (kind == "materials") root["materials"] = new JsonArray(Enumerable.Range(0, 33).Select(_ => (JsonNode)new JsonObject()).ToArray());
            if (kind == "unsupportedCodec") root["extensionsRequired"] = new JsonArray("KHR_draco_mesh_compression");
            bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        }
        var id = await c.AssetAsync("bad-" + kind + ".gltf", "model/gltf+json", bytes);
        await using (var db = c.Context())
        {
            var request = await c.Seed.SavedAsync(); request.Media.Add(new() { MediaAssetId = id, Kind = StorefrontMediaKind.Model3D, AltText = "Unsupported fixture" });
            await c.Seed.Products(db).SaveAsync(c.ProductId, request);
        }
        await using var host = await Web08MediaHost.StartAsync(c); var before = await c.Web.CountsAsync();
        using var denied = await host.Http.GetAsync("/shop/media/" + id); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(before, await c.Web.CountsAsync());
    }

    [Fact]
    public async Task Overlapping_legal_bufferView_padding_is_rejected_before_any_scene_loader_or_native_allocation()
    {
        var witness = Web08FixtureAssets.OverlappingViewAllocationWitness(); Assert.True(witness.Length < 8 * 1024 * 1024);
        Assert.Null(StorefrontModelInspector.Inspect(witness, "model/gltf-binary")); // Inspection only; never GLTFLoader.parse.
        var c = await Web08Case.CreateAsync(fixture); var id = await c.AssetAsync("overlap-working-budget.glb", "model/gltf-binary", witness);
        await using (var db = c.Context())
        {
            var request = await c.Seed.SavedAsync(); request.Media.Add(new() { MediaAssetId = id, Kind = StorefrontMediaKind.Model3D, AltText = "Bounded unsafe working-exposure witness" });
            await c.Seed.Products(db).SaveAsync(c.ProductId, request);
        }
        await using var host = await Web08MediaHost.StartAsync(c);
        using var response = await host.Http.GetAsync("/shop/media/" + id); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Two_real_decode_slots_return_only_safe_original_and_reject_a_dimension_bomb_before_busy_fallback()
    {
        var c = await Web08Case.CreateAsync(fixture, models: false);
        var bomb = await c.AssetAsync("header-bomb.png", "image/png", Web08FixtureAssets.Png(16384, 16384, headerOnly: true));
        await using (var db = c.Context())
        {
            var request = await c.Seed.SavedAsync(); request.Media.Add(new() { MediaAssetId = bomb, Kind = StorefrontMediaKind.Image });
            await c.Seed.Products(db).SaveAsync(c.ProductId, request);
        }
        await using var host = await Web08MediaHost.StartAsync(c);
        using var first = new ReadBarrierStream(Web08FixtureAssets.Png(640, 320)); using var second = new ReadBarrierStream(Web08FixtureAssets.Png(640, 320));
        var jobs = new[] { Task.Run(() => host.Images.ResizeAsync(new(first, "image/png"), 160, "own-busy-first")),
                           Task.Run(() => host.Images.ResizeAsync(new(second, "image/png"), 160, "own-busy-second")) };
        try
        {
            await Task.WhenAll(first.Entered.Task, second.Entered.Task).WaitAsync(TimeSpan.FromSeconds(10));
            using var response = await host.Http.GetAsync($"/shop/media/{c.ImageId}?width=160").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(Web08FixtureAssets.Png(640, 320), await response.Content.ReadAsByteArrayAsync());
            Assert.False(jobs[0].IsCompleted); Assert.False(jobs[1].IsCompleted);
            using var denied = await host.Http.GetAsync($"/shop/media/{bomb}?width=160").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        finally
        {
            first.Release.TrySetResult(); second.Release.TrySetResult();
            foreach (var result in await Task.WhenAll(jobs).WaitAsync(TimeSpan.FromSeconds(15))) await result.Content.DisposeAsync();
        }
    }

    [Fact]
    public async Task Cancellation_disposes_owned_input_and_releases_decode_capacity_for_a_following_real_image()
    {
        using var images = new StorefrontImageService(); using var input = new ReadBarrierStream(Web08FixtureAssets.Png(640, 320));
        using var cancelled = new CancellationTokenSource();
        var job = Task.Run(() => images.ResizeAsync(new(input, "image/png"), 160, "own-cancelled", cancelled.Token));
        await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await job); Assert.True(input.Disposed);
        using var next = new MemoryStream(Web08FixtureAssets.Png(640, 320));
        var result = await images.ResizeAsync(new(next, "image/png"), 160, "own-following").WaitAsync(TimeSpan.FromSeconds(10));
        try { using var bytes = new MemoryStream(); await result.Content.CopyToAsync(bytes); Assert.Equal((160, 80), Dimensions(bytes.ToArray())); }
        finally { await result.Content.DisposeAsync(); }
    }

    [Fact]
    public void Legal_high_stride_padding_uses_backing_view_exposure_while_distinct_overbudget_backing_views_are_rejected_without_GPU_load()
    {
        var below = Web08FixtureAssets.InterleavedModel();
        var inspection = Assert.IsType<StorefrontModelInspection>(StorefrontModelInspector.Inspect(below, "model/gltf-binary"));
        Assert.Equal(3, inspection.Vertices); Assert.Equal(1, inspection.Triangles);
        Assert.True(inspection.EstimatedGpuBytes >= 3 * 252 + 4096); // Actual backing array, not just36 payload bytes.
        Assert.True(inspection.EstimatedWorkingBytes >= inspection.EstimatedGpuBytes * 2);
        var over = Web08FixtureAssets.InterleavedModel(padding: 7_000_000 - 3 * 252, accessors: 10);
        Assert.True(over.Length < 8 * 1024 * 1024);
        Assert.Null(StorefrontModelInspector.Inspect(over, "model/gltf-binary")); // Never load/upload this witness.
    }

    private sealed class ReadBarrierStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes, writable: false);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }
        public override bool CanRead => !Disposed; public override bool CanSeek => !Disposed; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return await inner.ReadAsync(buffer, ct); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { Disposed = true; inner.Dispose(); } base.Dispose(disposing); }
        public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    // Independent PNG/WebP container dimensions; not production resize/header math.
    internal static (int Width, int Height) Dimensions(byte[] bytes)
    {
        if (bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return (BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)));
        Assert.True(bytes.Length >= 30 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP");
        var kind = Encoding.ASCII.GetString(bytes, 12, 4);
        if (kind == "VP8X") return (1 + bytes[24] + (bytes[25] << 8) + (bytes[26] << 16), 1 + bytes[27] + (bytes[28] << 8) + (bytes[29] << 16));
        if (kind == "VP8L") { var bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(21)); return (1 + (int)(bits & 0x3fff), 1 + (int)((bits >> 14) & 0x3fff)); }
        Assert.Equal("VP8 ", kind); Assert.Equal(new byte[] { 0x9d, 0x01, 0x2a }, bytes.AsSpan(23, 3).ToArray());
        return (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26)) & 0x3fff, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28)) & 0x3fff);
    }
}
