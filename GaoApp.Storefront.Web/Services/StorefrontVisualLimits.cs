namespace GaoApp.Storefront.Web.Services;

public static class StorefrontVisualLimits
{
    public const int ModelTransferBytes = 8 * 1024 * 1024;
    public const int ModelJsonBytes = 1024 * 1024;
    public const int ModelNodes = 256;
    public const int ModelPrimitives = 128;
    public const int ModelMaterials = 32;
    public const int ModelVertices = 100_000;
    public const int ModelTriangles = 100_000;
    public const int TextureEdge = 2048;
    public const long TexturePixels = 8_000_000;
    // Asset estimate, not a measurement of graphics-driver memory.
    public const long EstimatedGpuBytes = 64L * 1024 * 1024;
    public const long ModelWorkingBytes = 64L * 1024 * 1024;
    public const int ModelLoadSeconds = 15;
    public const int RenderPixels = 1_000_000;
    public const int ImageInputBytes = 20 * 1024 * 1024;
    public const int ImageEdge = 8192;
    public const long ImagePixels = 16_000_000;
    public const int ImageDecodeConcurrency = 2;
    public const long ImageCacheBytes = 64L * 1024 * 1024;
    public const int ImageCacheEntryBytes = 3 * 1024 * 1024;
    public static bool IsImageWidth(int width) => width is 160 or 320 or 640 or 960;
}
