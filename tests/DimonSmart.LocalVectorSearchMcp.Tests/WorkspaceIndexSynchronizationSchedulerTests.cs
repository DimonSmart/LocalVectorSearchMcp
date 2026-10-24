using DimonSmart.LocalVectorSearchMcp.Core.Embeddings;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Indexing;
using Microsoft.Extensions.Logging;

namespace DimonSmart.LocalVectorSearchMcp.Tests;

public sealed class WorkspaceIndexSynchronizationSchedulerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReconciliationFailure_LogsExpectedDiagnosticsAndRetainsDirtyState(bool embeddingFailure)
    {
        const string path = "Радио.md";
        Exception failure = embeddingFailure
            ? new EmbeddingProviderException(
                "Embedding endpoint is unavailable: http://localhost:11434/v1.",
                new HttpRequestException("Connection refused", new Exception("Socket failure")))
            : new InvalidOperationException("Unexpected index failure");
        var state = new InMemoryIndexSynchronizationState();
        var logger = new RecordingLogger();
        using var scheduler = new WorkspaceIndexSynchronizationScheduler(
            new FailingSynchronizer(failure), state, logger);
        var cancellationToken = TestContext.Current.CancellationToken;

        await scheduler.StartAsync(cancellationToken);
        try
        {
            scheduler.Schedule(path);
            var entry = await logger.Entry.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            await scheduler.StopAsync(cancellationToken);

            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains(path, entry.Message);
            if (embeddingFailure)
            {
                Assert.Null(entry.Exception);
                Assert.Equal($"Could not synchronize committed Markdown file {path}: {failure.Message}", entry.Message);
            }
            else
            {
                Assert.Same(failure, entry.Exception);
            }

            var status = state.GetStatus();
            Assert.Equal(1, status.PendingFiles);
            Assert.Contains(path, status.Paths);
            Assert.Equal(failure.Message, status.LastError);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    private sealed class FailingSynchronizer(Exception failure) : IWorkspaceIndexSynchronizer
    {
        public Task<bool> ReconcileAsync(string relativePath, CancellationToken cancellationToken)
            => Task.FromException<bool>(failure);
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLogger : ILogger<WorkspaceIndexSynchronizationScheduler>
    {
        public TaskCompletionSource<LogEntry> Entry { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entry.TrySetResult(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}
