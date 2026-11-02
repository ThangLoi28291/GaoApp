using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace GaoApp.Storefront.Web.Services;

public sealed class StorefrontImageService : IDisposable
{
    private readonly SemaphoreSlim decode = new(StorefrontVisualLimits.ImageDecodeConcurrency);
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = StorefrontVisualLimits.ImageCacheBytes });

    // Caller has freshly authorized the catalog asset and opened its safe physical file.
    // The gate includes source buffering/header/codec work, with no waiting queue retaining inputs.
    public Task<StorefrontMediaFile> ResizeAsync(StorefrontMediaFile original, int width, string cacheScope, CancellationToken ct = default)
        => ResizeCore(original, width, cacheScope, null, ct);

    internal Task<StorefrontMediaFile> ResizeForRequestAsync(StorefrontMediaFile original, int width, string cacheScope, HttpContext? context, CancellationToken ct)
        => ResizeCore(original, width, cacheScope, context, ct);

    private async Task<StorefrontMediaFile> ResizeCore(StorefrontMediaFile original, int width, string cacheScope, HttpContext? context, CancellationToken ct)
    {
        using var transformTiming = StorefrontRequestTiming.Measure(context, "image_transform");
        var entered = false;
        try
        {
            if (!StorefrontVisualLimits.IsImageWidth(width)) return original;
            entered = await decode.WaitAsync(0, ct);
            if (!entered) return original;
            if (!original.Content.CanSeek || original.Content.Length > StorefrontVisualLimits.ImageInputBytes) return original;
            using var readTiming = StorefrontRequestTiming.Measure(context, "image_read");
            var bytes = new byte[checked((int)original.Content.Length)];
            await original.Content.ReadExactlyAsync(bytes, ct);
            original.Content.Position = 0;
            readTiming?.Dispose();
            using var headerTiming = StorefrontRequestTiming.Measure(context, "image_header");
            if (!StorefrontImageHeader.TryRead(bytes, out var header) || !header.WithinImageBudget) return original;
            headerTiming?.Dispose();
            using var cacheTiming = StorefrontRequestTiming.Measure(context, "image_cache");
            var key = cacheScope + "|" + width + "|" + Convert.ToHexString(SHA256.HashData(bytes));
            if (cache.TryGetValue<byte[]>(key, out var saved) && saved is not null)
            {
                await original.Content.DisposeAsync();
                return new(new MemoryStream(saved, writable: false), "image/webp");
            }
            cacheTiming?.Dispose();
            using var decodeTiming = StorefrontRequestTiming.Measure(context, "image_decode");
            using var input = new SKMemoryStream(bytes);
            using var codec = SKCodec.Create(input);
            if (codec is null || codec.Info.Width != header.Width || codec.Info.Height != header.Height) return original;
            var info = new SKImageInfo(header.Width, header.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            if (bitmap.GetPixels() == IntPtr.Zero || codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success) return original;
            decodeTiming?.Dispose();
            using var scaleTiming = StorefrontRequestTiming.Measure(context, "image_scale");
            var origin = codec.EncodedOrigin;
            var swapped = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var orientedWidth = swapped ? header.Height : header.Width;
            var orientedHeight = swapped ? header.Width : header.Height;
            var targetWidth = Math.Min(width, orientedWidth);
            var targetHeight = Math.Max(1, (int)Math.Round((double)orientedHeight * targetWidth / orientedWidth));
            using var resized = new SKBitmap(new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (resized.GetPixels() == IntPtr.Zero) return original;
            using (var canvas = new SKCanvas(resized))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale((float)targetWidth / orientedWidth, (float)targetHeight / orientedHeight);
                switch (origin)
                {
                    case SKEncodedOrigin.TopRight: canvas.Translate(header.Width, 0); canvas.Scale(-1, 1); break;
                    case SKEncodedOrigin.BottomRight: canvas.Translate(header.Width, header.Height); canvas.RotateDegrees(180); break;
                    case SKEncodedOrigin.BottomLeft: canvas.Translate(0, header.Height); canvas.Scale(1, -1); break;
                    case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                    case SKEncodedOrigin.RightTop: canvas.Translate(header.Height, 0); canvas.RotateDegrees(90); break;
                    case SKEncodedOrigin.RightBottom: canvas.Translate(header.Height, header.Width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                    case SKEncodedOrigin.LeftBottom: canvas.Translate(0, header.Width); canvas.RotateDegrees(270); break;
                }
                canvas.DrawBitmap(bitmap, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), null);
            }
            scaleTiming?.Dispose();
            using var encodeTiming = StorefrontRequestTiming.Measure(context, "image_encode");
            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 82);
            if (encoded is null || encoded.Size <= 0 || encoded.Size > StorefrontVisualLimits.ImageCacheEntryBytes) return original;
            var output = encoded.ToArray();
            encodeTiming?.Dispose();
            cache.Set(key, output, new MemoryCacheEntryOptions { Size = output.Length, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2) });
            await original.Content.DisposeAsync();
            return new(new MemoryStream(output, writable: false), "image/webp");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException or ArgumentException or InvalidOperationException or IOException)
        {
            if (original.Content.CanSeek) original.Content.Position = 0;
            return original;
        }
        catch
        {
            await original.Content.DisposeAsync(); throw;
        }
        finally { if (entered) decode.Release(); }
    }
    public void Dispose() { cache.Dispose(); decode.Dispose(); }
}
