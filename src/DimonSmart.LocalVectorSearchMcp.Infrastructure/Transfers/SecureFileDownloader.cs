using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Transfers;

public sealed class SecureFileDownloader(HttpClient httpClient) : IRemoteFileDownloader
{
    private const int MaxRedirects = 5;
    private const int BufferSize = 64 * 1024;

    public static HttpMessageHandler CreatePrimaryHandler()
        => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = ConnectValidatedAsync
        };

    public async Task<RemoteFileDownloadResult> DownloadAsync(
        string downloadUrl,
        string destinationPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var currentUri = await ParseAndValidateUriAsync(
            downloadUrl,
            cancellationToken);

        try
        {
            for (var redirect = 0; redirect <= MaxRedirects; redirect++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirect == MaxRedirects)
                    {
                        throw new WorkspaceImageException(
                            "The image download exceeded the redirect limit.");
                    }

                    var location = response.Headers.Location
                        ?? throw new WorkspaceImageException(
                            "The image download returned a redirect without a Location header.");
                    var redirected = location.IsAbsoluteUri
                        ? location
                        : new Uri(currentUri, location);
                    currentUri = await ParseAndValidateUriAsync(
                        redirected.ToString(),
                        cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new WorkspaceImageException(
                        $"Downloading the supplied image failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).");
                }

                if (response.Content.Headers.ContentLength is long contentLength
                    && contentLength > maxBytes)
                {
                    throw new WorkspaceImageException(
                        $"The supplied image exceeds the {maxBytes} byte limit.");
                }

                return await CopyResponseAsync(
                    response,
                    destinationPath,
                    maxBytes,
                    cancellationToken);
            }

            throw new WorkspaceImageException(
                "The image download exceeded the redirect limit.");
        }
        catch (WorkspaceImageException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WorkspaceImageException("Downloading the supplied image timed out.");
        }
        catch (HttpRequestException)
        {
            throw new WorkspaceImageException(
                "Downloading the supplied image failed due to a network error.");
        }
    }

    private static async Task<RemoteFileDownloadResult> CopyResponseAsync(
        HttpResponseMessage response,
        string destinationPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var source = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var prefix = new byte[16];
        var prefixLength = 0;
        var buffer = new byte[BufferSize];
        long totalBytes = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > maxBytes)
            {
                throw new WorkspaceImageException(
                    $"The supplied image exceeds the {maxBytes} byte limit.");
            }

            await destination.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
            hash.AppendData(buffer.AsSpan(0, read));

            if (prefixLength < prefix.Length)
            {
                var copyLength = Math.Min(prefix.Length - prefixLength, read);
                buffer.AsSpan(0, copyLength)
                    .CopyTo(prefix.AsSpan(prefixLength));
                prefixLength += copyLength;
            }
        }

        await destination.FlushAsync(cancellationToken);
        return new RemoteFileDownloadResult(
            totalBytes,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            prefix[..prefixLength],
            response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<Uri> ParseAndValidateUriAsync(
        string value,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceImageException(
                "download_url must be an absolute HTTPS URL.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new WorkspaceImageException(
                "download_url must not contain user-info.");
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceImageException(
                "download_url resolves to a local or private destination.");
        }

        var addresses = await ResolveAddressesAsync(
            uri.DnsSafeHost,
            cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsUnsafeAddress))
        {
            throw new WorkspaceImageException(
                "download_url resolves to a local or private destination.");
        }

        return uri;
    }

    private static async ValueTask<Stream> ConnectValidatedAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await ResolveAddressesAsync(
            context.DnsEndPoint.Host,
            cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsUnsafeAddress))
        {
            throw new HttpRequestException(
                "Remote endpoint is not permitted.");
        }

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(
                address.AddressFamily,
                SocketType.Stream,
                ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (
                exception is SocketException
                    or OperationCanceledException)
            {
                socket.Dispose();
                lastError = exception;
                if (exception is OperationCanceledException)
                {
                    throw;
                }
            }
        }

        throw new HttpRequestException(
            "Could not connect to the remote endpoint.",
            lastError);
    }

    private static async Task<IPAddress[]> ResolveAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return [literal];
        }

        try
        {
            return await Dns.GetHostAddressesAsync(
                host,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is SocketException
                or ArgumentException)
        {
            throw new WorkspaceImageException(
                "The image download host could not be resolved.");
        }
    }

    private static bool IsUnsafeAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 0
                   || bytes[0] == 10
                   || bytes[0] == 127
                   || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                   || (bytes[0] == 169 && bytes[1] == 254)
                   || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168)
                   || (bytes[0] == 198 && bytes[1] is 18 or 19)
                   || bytes[0] >= 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal
                   || address.IsIPv6SiteLocal
                   || address.IsIPv6Multicast
                   || (bytes[0] & 0xfe) == 0xfc;
        }

        return true;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
