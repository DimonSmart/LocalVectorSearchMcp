using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Markdown;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

public sealed class WorkspaceMutationService(
    LocalVectorSearchMcpConfig config,
    KnowledgeBasePathGuard pathGuard,
    IMarkdownDocumentLoader loader,
    IMarkdownElementParser parser,
    IWorkspaceIndexSynchronizationScheduler synchronizationScheduler) : IWorkspaceMutationService
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
        for (var attempt = 1; attempt <= MaxPatchAttempts; attempt++)
        {
            var document = await loader.LoadExistingAsync(
                config.KnowledgeBase,
                normalized,
                cancellationToken);
            var elements = parser.Parse(document);
            var resolvedOperations = ResolvePatchOperations(
                request.Operations,
                elements);
            ValidateReplacementOperations(
                document,
                elements,
                resolvedOperations);
            var resultingSource = MarkdownSourcePatcher.Apply(
                document.Markdown,
                elements,
                resolvedOperations);

            string updatedSourceHash;
            try
            {
                updatedSourceHash = await WriteAtomicallyAsync(
                    absolute,
                    normalized,
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
                element.Text,
                element.SelfHash,
                element.SubtreeHash))
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

            if (anchor.SelfHash is null)
            {
                throw new WorkspaceMutationException(
                    "A fingerprint is required when mutating a concrete semantic element.");
            }

            var scope = operation.Kind.GetMutationScope();
            if (scope == MutationScope.Subtree && anchor.SubtreeHash is null)
            {
                throw new SemanticAnchorConflictException(
                    SemanticAnchorConflictReason.MissingSubtreeHash,
                    "The semantic pointer does not contain a subtree hash. " +
                    "Read the document again and use the current semantic pointer.");
            }

            var resolved = SemanticAnchorResolver.ResolveCandidate(anchor, candidates);
            if (scope == MutationScope.Subtree
                && !string.Equals(
                    anchor.SubtreeHash,
                    resolved.SubtreeHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new SemanticAnchorConflictException(
                    SemanticAnchorConflictReason.SubtreeHashMismatch,
                    "The target section changed after the pointer was created.");
            }

            result.Add(operation with { Pointer = resolved.Pointer.Value });
        }

        return result;
    }

    private void ValidateReplacementOperations(
        MarkdownSourceDocument document,
        IReadOnlyList<MarkdownElement> elements,
        IReadOnlyList<PatchOperation> operations)
    {
        var byPointer = elements
            .Where(element => element.SourceLength > 0)
            .ToDictionary(
                element => element.Pointer.Value,
                StringComparer.Ordinal);

        foreach (var operation in operations)
        {
            if (operation.Pointer == "document")
            {
                if (operation.Kind == PatchOperationKind.ReplaceSection)
                {
                    throw new WorkspaceMutationException(
                        "replace_section requires a heading pointer.");
                }

                if (operation.Kind == PatchOperationKind.ReplaceElement)
                {
                    throw new WorkspaceMutationException(
                        "replace_element requires a concrete Markdown element pointer.");
                }

                continue;
            }

            if (!byPointer.TryGetValue(operation.Pointer, out var target))
            {
                throw new WorkspaceMutationException(
                    $"Pointer '{operation.Pointer}' was not found in the requested document revision.");
            }

            switch (operation.Kind)
            {
                case PatchOperationKind.Replace:
                case PatchOperationKind.ReplaceElement:
                    ValidateElementReplacement(document, target, operation.Markdown);
                    break;

                case PatchOperationKind.ReplaceSection:
                    ValidateSectionReplacement(document, target, operation.Markdown);
                    break;
            }
        }
    }

    private void ValidateElementReplacement(
        MarkdownSourceDocument document,
        MarkdownElement target,
        string? markdown)
    {
        if (markdown is null)
        {
            return;
        }

        var replacementElements = ParseReplacement(document, markdown);
        if (replacementElements.Count != 1
            || HasNonWhitespaceOutsideElement(
                markdown,
                replacementElements.SingleOrDefault()))
        {
            throw new WorkspaceMutationException(
                "replace_element expects markdown containing exactly one editable Markdown element. " +
                "Use replace_section when replacing a heading together with its section content.");
        }

        var replacement = replacementElements[0];
        if (target.Kind == MarkdownElementKind.Heading
            && (replacement.Kind != MarkdownElementKind.Heading
                || replacement.HeadingLevel != target.HeadingLevel))
        {
            throw new WorkspaceMutationException(
                "replace_element cannot change a heading level or replace a heading with a non-heading element.");
        }
    }

    private void ValidateSectionReplacement(
        MarkdownSourceDocument document,
        MarkdownElement target,
        string? markdown)
    {
        if (target.Kind != MarkdownElementKind.Heading)
        {
            throw new WorkspaceMutationException(
                "replace_section requires a heading pointer.");
        }

        if (markdown is null)
        {
            return;
        }

        var replacementElements = ParseReplacement(document, markdown);
        if (replacementElements.Count == 0)
        {
            throw InvalidSectionReplacement();
        }

        var root = replacementElements[0];
        if (root.Kind != MarkdownElementKind.Heading
            || root.HeadingLevel != target.HeadingLevel
            || !string.IsNullOrWhiteSpace(markdown[..root.SourceStart])
            || replacementElements
                .Skip(1)
                .Any(element =>
                    element.Kind == MarkdownElementKind.Heading
                    && element.HeadingLevel <= target.HeadingLevel))
        {
            throw InvalidSectionReplacement();
        }
    }

    private IReadOnlyList<MarkdownElement> ParseReplacement(
        MarkdownSourceDocument document,
        string markdown)
    {
        var fragment = new MarkdownSourceDocument(
            document.RelativePath,
            document.AbsolutePath,
            markdown,
            "",
            document.LastWriteTimeUtc);

        return parser.Parse(fragment)
            .Where(element =>
                element.Kind != MarkdownElementKind.Document
                && element.SourceLength > 0)
            .ToList();
    }

    private static bool HasNonWhitespaceOutsideElement(
        string markdown,
        MarkdownElement? element)
    {
        if (element is null)
        {
            return true;
        }

        var before = markdown[..element.SourceStart];
        var end = element.SourceStart + element.SourceLength;
        var after = markdown[end..];
        return !string.IsNullOrWhiteSpace(before)
            || !string.IsNullOrWhiteSpace(after);
    }

    private static WorkspaceMutationException InvalidSectionReplacement()
        => new(
            "replace_section replacement must contain exactly one root section. " +
            "Additional headings must be nested below the replacement heading.");

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
        var document = await loader.LoadExistingAsync(
            config.KnowledgeBase,
            sourcePath,
            cancellationToken);

        if (File.Exists(targetAbsolute))
        {
            throw new WorkspaceMutationException(
                $"Destination '{targetPath}' already exists.");
        }
        EnsureExpectedHash(request.ExpectedSourceHash, document.SourceHash);
        Directory.CreateDirectory(Path.GetDirectoryName(targetAbsolute)!);
        targetAbsolute = pathGuard.ResolveMarkdownPath(targetPath);
        await EnsureFileHashAsync(
            sourceAbsolute,
            sourcePath,
            document.SourceHash,
            cancellationToken);
        File.Move(sourceAbsolute, targetAbsolute);

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
        var document = await loader.LoadExistingAsync(
            config.KnowledgeBase,
            normalized,
            cancellationToken);
        EnsureExpectedHash(request.ExpectedSourceHash, document.SourceHash);
        await EnsureFileHashAsync(
            absolute,
            normalized,
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
        string relativePath,
        string source,
        bool includeBom,
        string expectedSourceHash,
        CancellationToken cancellationToken)
    {
        await EnsureFileHashAsync(
            absolutePath,
            relativePath,
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
                relativePath,
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
        string relativePath,
        string expectedSourceHash,
        CancellationToken cancellationToken)
    {
        byte[] currentBytes;
        try
        {
            currentBytes = await File.ReadAllBytesAsync(
                absolutePath,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new DocumentNotFoundException(relativePath, exception);
        }

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
