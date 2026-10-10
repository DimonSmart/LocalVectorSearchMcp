using System.Buffers.Binary;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using StbImageSharp;
using StbImageWriteSharp;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

internal static class GifPreview
{
    // Bound decoded allocations before invoking the GIF decoder.
    private const long MaxPreviewPixels = 16_777_216;

    public static byte[] CreatePng(byte[] gif)
    {
        if (gif.Length < 10
            || !(gif.AsSpan(0, 6).SequenceEqual("GIF87a"u8)
                 || gif.AsSpan(0, 6).SequenceEqual("GIF89a"u8)))
        {
            throw new WorkspaceImageException(
                "The GIF image has an invalid header.",
                "UNSUPPORTED_FORMAT");
        }

        var width = BinaryPrimitives.ReadUInt16LittleEndian(
            gif.AsSpan(6, 2));
        var height = BinaryPrimitives.ReadUInt16LittleEndian(
            gif.AsSpan(8, 2));

        if (width == 0 || height == 0
            || (long)width * height > MaxPreviewPixels)
        {
            throw new WorkspaceImageException(
                "GIF dimensions exceed the supported preview limit.");
        }

        try
        {
            var image = ImageResult.FromMemory(
                gif,
                StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if (image.Width != width || image.Height != height)
            {
                throw new InvalidDataException(
                    "The decoded GIF dimensions differ from its header.");
            }

            using var output = new MemoryStream();
            new ImageWriter().WritePng(
                image.Data,
                image.Width,
                image.Height,
                StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha,
                output);
            return output.ToArray();
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException)
        {
            throw new WorkspaceImageException(
                "The GIF image could not be decoded for preview.",
                "UNSUPPORTED_FORMAT");
        }
    }
}
