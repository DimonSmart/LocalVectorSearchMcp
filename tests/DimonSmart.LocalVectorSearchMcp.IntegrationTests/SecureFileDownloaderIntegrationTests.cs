using System.Net;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Transfers;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;
using DimonSmart.LocalVectorSearchMcp.IntegrationTests.Helpers;

namespace DimonSmart.LocalVectorSearchMcp.IntegrationTests;

public sealed class SecureFileDownloaderIntegrationTests
{
    private const string PublicTestUrl =
        "https://93.184.216.34/image.png";

    [Theory]
    [InlineData("http://93.184.216.34/image.png")]
    [InlineData("https://127.0.0.1/image.png")]
    [InlineData("https://10.0.0.1/image.png")]
    [InlineData("https://localhost/image.png")]
    [InlineData("https://user:secret@93.184.216.34/image.png")]
    public async Task UnsafeDownloadUrlsAreRejectedBeforeRequest(
        string url)
    {
        using var temp = new TemporaryDirectory();
        var handler = new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
            });
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        var downloader = new SecureFileDownloader(client);

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => downloader.DownloadAsync(
                url,
                Path.Combine(temp.Path, "download.tmp"),
                WorkspaceImageService.MaxImageBytes,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task RedirectToPrivateAddressIsRejected()
    {
        using var temp = new TemporaryDirectory();
        var handler = new StubHandler(
            _ =>
            {
                var response = new HttpResponseMessage(
                    HttpStatusCode.Redirect);
                response.Headers.Location = new Uri(
                    "https://127.0.0.1/private.png");
                return response;
            });
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        var downloader = new SecureFileDownloader(client);

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => downloader.DownloadAsync(
                PublicTestUrl,
                Path.Combine(temp.Path, "download.tmp"),
                WorkspaceImageService.MaxImageBytes,
                TestContext.Current.CancellationToken));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ContentLengthAboveLimitIsRejectedWithoutWriting()
    {
        using var temp = new TemporaryDirectory();
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentLength =
            WorkspaceImageService.MaxImageBytes + 1;
        var handler = new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        var downloader = new SecureFileDownloader(client);
        var destination = Path.Combine(
            temp.Path,
            "download.tmp");

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => downloader.DownloadAsync(
                PublicTestUrl,
                destination,
                WorkspaceImageService.MaxImageBytes,
                TestContext.Current.CancellationToken));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task StreamedBytesAboveLimitAreRejectedWithoutContentLength()
    {
        using var temp = new TemporaryDirectory();
        var content = new StreamContent(
            new GeneratedStream(
                WorkspaceImageService.MaxImageBytes + 1));
        content.Headers.ContentLength = null;
        var handler = new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        var downloader = new SecureFileDownloader(client);

        await Assert.ThrowsAsync<WorkspaceImageException>(
            () => downloader.DownloadAsync(
                PublicTestUrl,
                Path.Combine(temp.Path, "download.tmp"),
                WorkspaceImageService.MaxImageBytes,
                TestContext.Current.CancellationToken));
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) :
        HttpMessageHandler
    {
        private int callCount;

        public int CallCount => callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class GeneratedStream(long length) : Stream
    {
        private long position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            if (position >= length)
            {
                return 0;
            }

            var read = (int)Math.Min(
                count,
                length - position);
            Array.Clear(buffer, offset, read);
            position += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position >= length)
            {
                return ValueTask.FromResult(0);
            }

            var read = (int)Math.Min(
                buffer.Length,
                length - position);
            buffer.Span[..read].Clear();
            position += read;
            return ValueTask.FromResult(read);
        }

        public override void Flush()
        {
        }

        public override long Seek(
            long offset,
            SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
            => throw new NotSupportedException();
    }
}
