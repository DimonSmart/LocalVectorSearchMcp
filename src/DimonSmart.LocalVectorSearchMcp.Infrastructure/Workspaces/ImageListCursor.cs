using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

internal static class ImageListCursor
{
    private const string Version = "v1";

    public static string Encode(string relativePath)
    {
        var payload = Encoding.UTF8.GetBytes($"{Version}\n{relativePath}");
        return Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string Decode(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            throw new WorkspaceImageException("Image list cursor is malformed.");
        }

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(
                base64.Length + ((4 - base64.Length % 4) % 4),
                '=');
            var bytes = Convert.FromBase64String(base64);
            var payload = new UTF8Encoding(false, true).GetString(bytes);
            var separator = payload.IndexOf('\n');
            if (separator < 0)
            {
                throw new WorkspaceImageException("Image list cursor is malformed.");
            }

            var version = payload[..separator];
            if (!version.Equals(Version, StringComparison.Ordinal))
            {
                throw new WorkspaceImageException(
                    $"Image list cursor version '{version}' is not supported.");
            }

            var relativePath = payload[(separator + 1)..];
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new WorkspaceImageException("Image list cursor is malformed.");
            }

            return relativePath;
        }
        catch (WorkspaceImageException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is FormatException
                or DecoderFallbackException)
        {
            throw new WorkspaceImageException("Image list cursor is malformed.");
        }
    }
}
