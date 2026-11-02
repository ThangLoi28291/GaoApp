using System.Buffers.Binary;

namespace GaoApp.Storefront.Web.Services;

public readonly record struct StorefrontImageInfo(int Width, int Height, string ContentType)
{
    public bool IsAnimated { get; init; }
    public long Pixels => (long)Width * Height;
    public bool WithinImageBudget => Width > 0 && Height > 0 && Width <= StorefrontVisualLimits.ImageEdge &&
        Height <= StorefrontVisualLimits.ImageEdge && Pixels <= StorefrontVisualLimits.ImagePixels;
}

// Read dimensions before passing untrusted image bytes to a native decoder.
public static class StorefrontImageHeader
{
    public static async ValueTask<StorefrontImageInfo?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        if (!stream.CanSeek || stream.Length <= 0) return null;
        var original = stream.Position;
        try
        {
            stream.Position = 0;
            var first = new byte[(int)Math.Min(32, stream.Length)];
            await stream.ReadExactlyAsync(first, ct);
            if (first.Length >= 2 && first[0] == 0xff && first[1] == 0xd8)
            {
                stream.Position = 2;
                var segment = new byte[8];
                for (var n = 0; n < 4096 && stream.Position < stream.Length; n++)
                {
                    if (stream.ReadByte() != 0xff) return null;
                    int marker; do { marker = stream.ReadByte(); } while (marker == 0xff);
                    if (marker < 0 || marker is 0xda or 0xd9) return null;
                    if (marker is 0x01 or >= 0xd0 and <= 0xd8) continue;
                    if (stream.Position > stream.Length - 2) return null;
                    await stream.ReadExactlyAsync(segment.AsMemory(0, 2), ct);
                    var length = BinaryPrimitives.ReadUInt16BigEndian(segment);
                    if (length < 2 || stream.Position + length - 2 > stream.Length) return null;
                    if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
                    {
                        if (length < 8) return null;
                        await stream.ReadExactlyAsync(segment.AsMemory(0, 6), ct);
                        return Make(BinaryPrimitives.ReadUInt16BigEndian(segment.AsSpan(3)), BinaryPrimitives.ReadUInt16BigEndian(segment.AsSpan(1)), "image/jpeg", out var jpeg) ? jpeg : null;
                    }
                    stream.Seek(length - 2, SeekOrigin.Current);
                }
                return null;
            }
            if (first.Length >= 12 && first.AsSpan(0, 4).SequenceEqual("RIFF"u8) && first.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            {
                stream.Position = 12;
                var header = new byte[18];
                for (var n = 0; n < 4096 && stream.Position <= stream.Length - 8; n++)
                {
                    await stream.ReadExactlyAsync(header.AsMemory(0, 8), ct);
                    var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)); var begin = stream.Position;
                    if (begin + length > stream.Length) return null;
                    var dataLength = (int)Math.Min(length, 10u);
                    await stream.ReadExactlyAsync(header.AsMemory(8, dataLength), ct);
                    if (header.AsSpan(0, 4).SequenceEqual("VP8X"u8) && dataLength >= 10)
                        return Make(U24(header.AsSpan(12)) + 1, U24(header.AsSpan(15)) + 1, "image/webp", out var extended) ? extended with { IsAnimated = (header[8] & 2) != 0 } : null;
                    if (header.AsSpan(0, 4).SequenceEqual("VP8L"u8) && dataLength >= 5 && header[8] == 0x2f)
                    {
                        var bits = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(9));
                        return (bits >> 29) == 0 && Make((bits & 0x3fff) + 1, ((bits >> 14) & 0x3fff) + 1, "image/webp", out var lossless) ? lossless : null;
                    }
                    if (header.AsSpan(0, 4).SequenceEqual("VP8 "u8) && dataLength >= 10 && header.AsSpan(11, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
                        return Make((uint)(BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14)) & 0x3fff), (uint)(BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(16)) & 0x3fff), "image/webp", out var lossy) ? lossy : null;
                    stream.Position = checked(begin + length + (length & 1));
                }
                return null;
            }
            if (!TryRead(first, out var image)) return null;
            if (image.ContentType == "image/png")
            {
                stream.Position = 8;
                var chunk = new byte[8];
                for (var n = 0; n < 4096 && stream.Position <= stream.Length - 8; n++)
                {
                    await stream.ReadExactlyAsync(chunk, ct);
                    var length = BinaryPrimitives.ReadUInt32BigEndian(chunk); var begin = stream.Position;
                    if (begin + length + 4 > stream.Length) return null;
                    if (chunk.AsSpan(4, 4).SequenceEqual("acTL"u8))
                    {
                        if (length < 8) return null;
                        await stream.ReadExactlyAsync(chunk.AsMemory(0, 4), ct);
                        return image with { IsAnimated = BinaryPrimitives.ReadUInt32BigEndian(chunk) > 1 };
                    }
                    if (chunk.AsSpan(4, 4).SequenceEqual("IDAT"u8)) return image;
                    stream.Position = checked(begin + length + 4);
                }
                return null;
            }
            return image;
        }
        finally { stream.Position = original; }
    }

    public static bool TryRead(ReadOnlySpan<byte> bytes, out StorefrontImageInfo info)
    {
        info = default;
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) == 13 && bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            if (!Make(BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]), BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]), "image/png", out info)) return false;
            var chunk = 8;
            while (chunk <= bytes.Length - 8)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[chunk..]);
                if ((long)chunk + 12 + length > bytes.Length) break;
                if (bytes.Slice(chunk + 4, 4).SequenceEqual("acTL"u8) && length >= 8)
                    info = info with { IsAnimated = BinaryPrimitives.ReadUInt32BigEndian(bytes[(chunk + 8)..]) > 1 };
                chunk = checked((int)((long)chunk + 12 + length));
            }
            return true;
        }
        if (bytes.Length >= 10 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)))
        {
            if (!Make(BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]), "image/gif", out info)) return false;
            info = info with { IsAnimated = true }; return true; // Static derivative only; never raw animation fallback.
        }
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            var offset = 12;
            while (offset <= bytes.Length - 8)
            {
                var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
                if (length > int.MaxValue || (long)offset + 8 + length > bytes.Length) return false;
                var payload = bytes.Slice(offset + 8, (int)length);
                var kind = bytes.Slice(offset, 4);
                if (kind.SequenceEqual("VP8X"u8) && payload.Length >= 10)
                {
                    if (!Make(U24(payload[4..]) + 1, U24(payload[7..]) + 1, "image/webp", out info)) return false;
                    info = info with { IsAnimated = (payload[0] & 2) != 0 }; return true;
                }
                if (kind.SequenceEqual("VP8L"u8) && payload.Length >= 5 && payload[0] == 0x2f)
                {
                    var bits = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]);
                    if ((bits >> 29) != 0) return false;
                    return Make((bits & 0x3fff) + 1, ((bits >> 14) & 0x3fff) + 1, "image/webp", out info);
                }
                if (kind.SequenceEqual("VP8 "u8) && payload.Length >= 10 && payload.Slice(3, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
                    return Make((uint)(BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]) & 0x3fff),
                        (uint)(BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]) & 0x3fff), "image/webp", out info);
                var next = (long)offset + 8 + length + (length & 1);
                if (next > bytes.Length) return false;
                offset = (int)next;
            }
            return false;
        }
        if (bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8)
        {
            var offset = 2;
            while (offset < bytes.Length)
            {
                if (bytes[offset++] != 0xff) return false;
                while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
                if (offset >= bytes.Length) return false;
                var marker = bytes[offset++];
                if (marker is 0xd9 or 0xda) return false;
                if (marker is 0x01 or >= 0xd0 and <= 0xd8) continue;
                if (offset > bytes.Length - 2) return false;
                var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
                if (length < 2 || offset > bytes.Length - length) return false;
                if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
                {
                    if (length < 8) return false;
                    return Make(BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]),
                        BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 3)..]), "image/jpeg", out info);
                }
                offset += length;
            }
        }
        return false;
    }
    private static uint U24(ReadOnlySpan<byte> value) => (uint)(value[0] | value[1] << 8 | value[2] << 16);
    private static bool Make(uint width, uint height, string type, out StorefrontImageInfo info)
    {
        info = default;
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue) return false;
        info = new((int)width, (int)height, type); return true;
    }
}
