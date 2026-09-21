using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

public sealed class WorkspaceMutationService(
    LocalVectorSearchMcpConfig config,
    KnowledgeBasePathGuard pathGuard,
    IMarkdownDocumentLoader loader,
    IMarkdownElementParser parser,
    IWorkspaceIndexSynchronizationScheduler synchronizationScheduler,
    IIndexSynchronizationState synchronizationState) : IWorkspaceMutationService
{
    private const int MaxPatchAttempts = 3;
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    public Task<MutationResponse> PatchAsync(
        PatchRequest request,
        CancellationToken cancellationToken)
        => RunMutationAsync(
            () => PatchCoreAsync(request, cancellationToken),
            cancellationToken);

    private async Task<MutationResponse> PatchCoreAsync(
        PatchRequest request,
        CancellationToken cancellationToken)
    {
        EnsureWritesEnabled();
        var normalized = pathGuard.ValidateRelativePath(request.Path);
        var absolute = pathGuard.ResolveMarkdownPath(normalized);
        if (!File.Exists(absolute))
        {
            throw new WorkspaceMutationException(
                $"Markdown file '{normalized}' does not exist.");
        }

        for (var attempt = 1; attempt <= MaxPatchAttempts; attempt++)
        {
            var document = await loader.LoadFileAsync(
                config.KnowledgeBase,
                normalized,
                cancellationToken);
            var elements = parser.Parse(document);
            var resolvedOperations = ResolvePatchOperations(
                request.Operations,
                elements);
            var resultingSource = MarkdownSourcePatcher.Apply(
                document.Markdown,
                elements,
                resolvedOperations);

            string updatedSourceHash;
            try
            {
                updatedSourceHash = await WriteAtomicallyAsync(
                    absolute,
                    resultingSource,
                    document.HasUtf8Bom,
                    document.SourceHash,
                    cancellationToken);
            }
            catch (DocumentConflictException) when (attempt < MaxPatchAttempts)
            {
                continue;
            }
            catch (DocumentConflictException)
            {
                throw new DocumentConflictException(
                    "The document kept changing while the patch was being applied.");
            }

            return ScheduleSynchronization(normalized, updatedSourceHash, null);
        }

        throw new DocumentConflictException(
            "The document kept changing while the patch was being applied.");
    }

    private static IReadOnlyList<PatchOperation> ResolvePatchOperations(
        IReadOnlyList<PatchOperation> operations,
        IReadOnlyList<MarkdownElement> elements)
    {
        var candidates = elements
            .Where(element => element.SourceLength > 0)
            .Select(element => new SemanticAnchorCandidate(
                element.Pointer,
                element.Kind,
                element.Text))
            .ToList();
        var result = new List<PatchOperation>(operations.Count);

        foreach (var operation in operations)
        {
            var anchor = SemanticAnchorParser.Parse(operation.Pointer);
            if (anchor.IsDocument)
            {
                result.Add(operation with { Pointer = "document" });
                continue;
            }

            if (anchor.Fingerprint is null)
            {
                throw new WorkspaceMutationException(
                    "A fingerprint is required when mutating a concrete semantic element.");
            }

            var resolved = SemanticAnchorResolver.Resolve(anchor, candidates);
            result.Add(operation with { Pointer = resolved.Value });
        }

        return result;
    }

    public Task<MutationResponse> CreateAsync(
        string path,
        string markdown,
        CancellationToken cancellationToken)
        => RunMutationAsync(
            () => CreateCoreAsync(path, markdown, cancellationToken),
            cancellationToken);

    private async Task<MutationResponse> CreateCoreAsync(
        string path,
        string markdown,
        CancellationToken cancellationToken)
    {
        EnsureWritesEnabled();
        var normalized = pathGuard.ValidateRelativePath(path);
        var absolute = pathGuard.ResolveMarkdownPath(normalized);
        if (File.Exists(absolute))
        {
            throw new WorkspaceMutationException(
                $"Markdown file '{normalized}' already exists.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        absolute = pathGuard.ResolveMarkdownPath(normalized);
        var bytes = new UTF8Encoding(false).GetBytes(markdown ?? "");
        await using (var stream = new FileStream(
            absolute,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous))
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        return ScheduleSynchronization(
            normalized,
            Core.Storage.StableHash.HashBytes(bytes),
            null);
    }

    public Task<MutationResponse> MoveAsync(
        MoveRequest request,
        CancellationToken cancellationToken)
        => RunMutationAsync(
            () => MoveCoreAsync(request, cancellationToken),
            cancellationToken);

    private async Task<MutationResponse> MoveCoreAsync(
        MoveRequest request,
        CancellationToken cancellationToken)
    {
        EnsureWritesEnabled();
        var sourcePath = pathGuard.ValidateRelativePath(request.SourcePath);
        var targetPath = pathGuard.ValidateRelativePath(request.TargetPath);
        var sourceAbsolute = pathGuard.ResolveMarkdownPath(sourcePath);
        var targetAbsolute = pathGuard.ResolveMarkdownPath(targetPath);
        if (!File.Exists(sourceAbsolute))
        {
            throw new WorkspaceMutationException(
                $"Markdown file '{sourcePath}' does not exist.");
        }

        if (File.Exists(targetAbsolute))
        {
            throw new WorkspaceMutationException(
                $"Destination '{targetPath}' already exists.");
        }

        var document = await loader.LoadFileAsync(
            config.KnowledgeBase,
            sourcePath,
            cancellationToken);
        EnsureExpectedHash(request.ExpectedSourceHash, document.SourceHash);
        Directory.CreateDirectory(Path.GetDirectoryName(targetAbsolute)!);
        targetAbsolute = pathGuard.ResolveMarkdownPath(targetPath);
        await EnsureFileHashAsync(
            sourceAbsolute,
            document.SourceHash,
            cancellationToken);
        File.Move(sourceAbsolute, targetAbsolute);

        synchronizationState.MarkDirty(sourcePath, "Index synchronization is pending.");
        synchronizationScheduler.Schedule(sourcePath);
        return ScheduleSynchronization(targetPath, document.SourceHash, sourcePath);
    }

    public Task<MutationResponse> DeleteAsync(
        DeleteRequest request,
        CancellationToken cancellationToken)
        => RunMutationAsync(
            () => DeleteCoreAsync(request, cancellationToken),
            cancellationToken);

    private async Task<MutationResponse> DeleteCoreAsync(
        DeleteRequest request,
        CancellationToken cancellationToken)
    {
        EnsureWritesEnabled();
        var normalized = pathGuard.ValidateRelativePath(request.Path);
        var absolute = pathGuard.ResolveMarkdownPath(normalized);
        if (!File.Exists(absolute))
        {
            throw new WorkspaceMutationException(
                $"Markdown file '{normalized}' does not exist.");
        }

        var document = await loader.LoadFileAsync(
            config.KnowledgeBase,
            normalized,
            cancellationToken);
        EnsureExpectedHash(request.ExpectedSourceHash, document.SourceHash);
        await EnsureFileHashAsync(
            absolute,
            document.SourceHash,
            cancellationToken);
        File.Delete(absolute);
        return ScheduleSynchronization(normalized, null, null);
    }

    private MutationResponse ScheduleSynchronization(
        string path,
        string? sourceHash,
        string? previousPath)
    {
        synchronizationState.MarkDirty(path, "Index synchronization is pending.");
        synchronizationScheduler.Schedule(path);
        return new MutationResponse(path, sourceHash, false, null, previousPath);
    }

    private void EnsureWritesEnabled()
    {
        if (!config.KnowledgeBase.AllowWrites)
        {
            throw new WorkspaceMutationException(
                "Workspace writes are disabled. Set knowledgeBase.allowWrites to true to enable mutation tools.");
        }
    }

    private static void EnsureExpectedHash(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            throw new WorkspaceMutationException(
                "expectedSourceHash is required.");
        }

        if (!string.Equals(
                expected,
                actual,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentConflictException(
                "Document has changed since it was read. Read the affected document again and retry the mutation.");
        }
    }

    private static async Task<string> WriteAtomicallyAsync(
        string absolutePath,
        string source,
        bool includeBom,
        string expectedSourceHash,
        CancellationToken cancellationToken)
    {
        await EnsureFileHashAsync(
            absolutePath,
            expectedSourceHash,
            cancellationToken);

        var payload = new UTF8Encoding(includeBom).GetPreamble()
            .Concat(new UTF8Encoding(false).GetBytes(source))
            .ToArray();
        var directory = Path.GetDirectoryName(absolutePath)!;
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(absolutePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                payload,
                cancellationToken);
            await EnsureFileHashAsync(
                absolutePath,
                expectedSourceHash,
                cancellationToken);
            File.Move(temporaryPath, absolutePath, true);
            return Core.Storage.StableHash.HashBytes(payload);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task EnsureFileHashAsync(
        string absolutePath,
        string expectedSourceHash,
        CancellationToken cancellationToken)
    {
        var currentBytes = await File.ReadAllBytesAsync(
            absolutePath,
            cancellationToken);
        var currentHash = Core.Storage.StableHash.HashBytes(currentBytes);
        EnsureExpectedHash(expectedSourceHash, currentHash);
    }

    private async Task<MutationResponse> RunMutationAsync(
        Func<Task<MutationResponse>> mutation,
        CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            return await mutation();
        }
        finally
        {
            mutationGate.Release();
        }
    }
}
