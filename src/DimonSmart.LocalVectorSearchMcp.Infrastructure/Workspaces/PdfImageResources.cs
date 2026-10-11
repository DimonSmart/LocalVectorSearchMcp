using System.Security.Cryptography;
using iText.IO.Image;
using iText.Kernel.Pdf.Xobject;
using iText.Layout.Element;
using SkiaSharp;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

internal sealed record PdfExportSettings(int TargetDpi, int JpegQuality)
{
    public static PdfExportSettings For(DimonSmart.LocalVectorSearchMcp.Core.Workspaces.PdfExportProfile profile) => profile switch
    {
        DimonSmart.LocalVectorSearchMcp.Core.Workspaces.PdfExportProfile.Screen => new(96, 65),
        DimonSmart.LocalVectorSearchMcp.Core.Workspaces.PdfExportProfile.Standard => new(150, 82),
        DimonSmart.LocalVectorSearchMcp.Core.Workspaces.PdfExportProfile.Print => new(300, 95),
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown PDF export profile.")
    };
}

/// <summary>
/// Owns image resources for one PDF export. Keeps only XObjects and compact keys,
/// never decoded bitmap data or a second encoded-byte cache.
/// </summary>
internal sealed class PdfImageResources(
    PdfExportSettings settings,
    List<string> warnings,
    CancellationToken cancellationToken)
{
    private const long MaxDecodedBytes = 128L * 1024 * 1024;
    private const double MinResizeRatio = 1.15;
    private readonly Dictionary<(string SourceHash, int Width, int Height), PdfImageXObject> _variants = new();
    private readonly Dictionary<string, PdfImageXObject> _byEncodedHash = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    public Image Create(byte[] sourceBytes, string source, float maxWidthPt, float maxHeightPt)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceData = ImageDataFactory.Create(sourceBytes);
        // Capture exactly the physical geometry the previous renderer used.
        var original = new Image(sourceData);
        original.ScaleToFit(maxWidthPt, maxHeightPt);
        var displayWidth = original.GetImageScaledWidth();
        var displayHeight = original.GetImageScaledHeight();

        var sourceWidth = (int)sourceData.GetWidth();
        var sourceHeight = (int)sourceData.GetHeight();
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return original;

        var requestedWidth = Math.Max(1, (int)Math.Ceiling(displayWidth * settings.TargetDpi / 72d));
        var requestedHeight = Math.Max(1, (int)Math.Ceiling(displayHeight * settings.TargetDpi / 72d));
        var scale = Math.Min(1d, Math.Min((double)requestedWidth / sourceWidth, (double)requestedHeight / sourceHeight));
        var targetWidth = scale <= 1d / MinResizeRatio ? Math.Clamp((int)Math.Floor(sourceWidth * scale), 1, sourceWidth) : sourceWidth;
        var targetHeight = scale <= 1d / MinResizeRatio ? Math.Clamp((int)Math.Floor(sourceHeight * scale), 1, sourceHeight) : sourceHeight;
        var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes));
        var variantKey = (sourceHash, targetWidth, targetHeight);

        if (!_variants.TryGetValue(variantKey, out var resource))
        {
            var selected = sourceBytes;
            if (targetWidth < sourceWidth || targetHeight < sourceHeight)
            {
                try
                {
                    selected = Optimize(sourceBytes, targetWidth, targetHeight, source);
                    cancellationToken.ThrowIfCancellationRequested();
                    // Verify that the re-encoded image can actually be embedded by iText.
                    _ = ImageDataFactory.Create(selected);
                }
                catch (Exception error) when (error is not OperationCanceledException
                    and not OutOfMemoryException and not StackOverflowException)
                {
                    Warn(source, "resampling failed: " + error.GetType().Name);
                    selected = sourceBytes;
                }
            }

            var selectedHash = Convert.ToHexString(SHA256.HashData(selected));
            if (!_byEncodedHash.TryGetValue(selectedHash, out resource))
            {
                try
                {
                    resource = new PdfImageXObject(ImageDataFactory.Create(selected));
                }
                catch (Exception error) when (!ReferenceEquals(selected, sourceBytes)
                    && error is not OperationCanceledException and not OutOfMemoryException
                    and not StackOverflowException)
                {
                    Warn(source, "optimized image could not be embedded: " + error.GetType().Name);
                    selected = sourceBytes;
                    selectedHash = sourceHash;
                    if (!_byEncodedHash.TryGetValue(selectedHash, out resource))
                        resource = new PdfImageXObject(sourceData);
                }
                _byEncodedHash[selectedHash] = resource;
            }
            _variants[variantKey] = resource;
        }

        // Never call ScaleToFit on the downsampled bitmap: its intrinsic size changed.
        // Restoring the former dimensions also preserves small-image (non-upscaled) layout.
        return new Image(resource).ScaleAbsolute(displayWidth, displayHeight);
    }

    private byte[] Optimize(byte[] original, int width, int height, string source)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var jpeg = original.Length >= 3 && original[0] == 0xff && original[1] == 0xd8;
        var png = original.Length >= 8 && original.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (!jpeg && !png)
            return original;

        // JPEG APP1/APP2/APP14 (EXIF, ICC, Adobe) and PNG color metadata must
        // remain untouched; Skia encoding does not round-trip those reliably.
        if (jpeg && HasUnsafeJpegMetadata(original) || png && HasPngColorMetadata(original))
            return original;

        using var data = SKData.CreateCopy(original);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedOrigin != SKEncodedOrigin.TopLeft)
            return original;

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || 4L * info.Width * info.Height > MaxDecodedBytes)
        {
            Warn(source, "bitmap exceeds the resampling memory limit");
            return original;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
            throw new InvalidOperationException("The source image cannot be decoded.");
        cancellationToken.ThrowIfCancellationRequested();
        using var resized = decoded.Resize(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized is null)
            throw new InvalidOperationException("Image resampling failed.");
        cancellationToken.ThrowIfCancellationRequested();
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png,
            jpeg ? settings.JpegQuality : 100);
        if (encoded is null)
            throw new InvalidOperationException("Image encoding failed.");
        cancellationToken.ThrowIfCancellationRequested();
        var result = encoded.ToArray();
        return result.Length < original.Length ? result : original;
    }

    private void Warn(string source, string reason)
    {
        if (_warned.Add(source + "\\0" + reason))
            warnings.Add($"Image '{source}' was not optimized: {reason}.");
    }

    private static bool HasUnsafeJpegMetadata(byte[] bytes)
    {
        for (var offset = 2; offset + 4 < bytes.Length;)
        {
            if (bytes[offset] != 0xff) return false;
            var marker = bytes[offset + 1];
            if (marker is 0xda or 0xd9) break;
            if (marker is 0xe1 or 0xe2 or 0xee) return true;
            var length = bytes[offset + 2] * 256 + bytes[offset + 3];
            if (length < 2 || offset + 2 + length > bytes.Length) return true;
            // CMYK JPEG has four components; never silently change its color space.
            if ((marker is 0xc0 or 0xc1 or 0xc2 or 0xc3)
                && length >= 8 && bytes[offset + 9] != 3) return true;
            offset += 2 + length;
        }
        return false;
    }

    private static bool HasPngColorMetadata(byte[] bytes)
    {
        for (var offset = 8; offset + 12 <= bytes.Length;)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > int.MaxValue || (long)offset + length + 12 > bytes.Length) return true;
            var type = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (type is "iCCP" or "sRGB" or "gAMA" or "cHRM") return true;
            if (type == "IDAT") break;
            offset += (int)length + 12;
        }
        return false;
    }
}
