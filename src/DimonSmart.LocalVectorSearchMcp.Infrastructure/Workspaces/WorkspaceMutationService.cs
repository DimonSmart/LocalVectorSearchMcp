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
                resolvedOperations,
                out var plannedEdits);
            if (resolvedOperations.Any(operation => operation.Pointer != "document"
                && elements.Any(element => element.Pointer.Value == operation.Pointer
                    && element.Kind is MarkdownElementKind.ListItem or MarkdownElementKind.BlockQuote)))
            {
                ValidateStructuredPatch(elements, parser.Parse(document with
                {
                    Markdown = resultingSource
                }), resolvedOperations, plannedEdits);
            }

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

            if (elements.FirstOrDefault()?.ReservedPointers?.Contains(anchor.LogicalPointer.Value) == true)
            {
                throw new SemanticAnchorConflictException(
                    SemanticAnchorConflictReason.SemanticTargetNotFound,
                    "The original pointer belongs to a now-atomic list or quote. " +
                    "Read the document again and use its list_item or block_quote pointer.");
            }

            var resolved = SemanticAnchorResolver.ResolveCandidate(anchor, candidates);
            var scope = operation.Kind.GetMutationScope(resolved.Kind);
            if (scope == MutationScope.Subtree && anchor.SubtreeHash is null)
            {
                throw new SemanticAnchorConflictException(
                    SemanticAnchorConflictReason.MissingSubtreeHash,
                    "The semantic pointer does not contain a subtree hash. " +
                    "Read the document again and use the current semantic pointer.");
            }

            if (scope == MutationScope.Subtree
                && !string.Equals(
                    anchor.SubtreeHash,
                    resolved.SubtreeHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new SemanticAnchorConflictException(
                    SemanticAnchorConflictReason.SubtreeHashMismatch,
                    "The target subtree changed after the pointer was created. Read the document again.");
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

                if (operation.Kind == PatchOperationKind.DeleteSection)
                {
                    throw new WorkspaceMutationException(
                        "delete_section requires a heading pointer.");
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
                    if (target.Kind == MarkdownElementKind.ListItem)
                        ValidateListFragment(document.Markdown, target, operation.Markdown);
                    else
                        ValidateElementReplacement(document, target, operation.Markdown);
                    break;

                case PatchOperationKind.InsertBefore:
                case PatchOperationKind.InsertAfter:
                    if (target.Kind == MarkdownElementKind.ListItem)
                        ValidateListFragment(document.Markdown, target, operation.Markdown);
                    else if (target.Kind == MarkdownElementKind.BlockQuote)
                        ValidateQuoteFragment(document, operation.Markdown);
                    break;

                case PatchOperationKind.ReplaceSection:
                    ValidateSectionReplacement(document, target, operation.Markdown);
                    break;

                case PatchOperationKind.DeleteSection:
                    ValidateSectionDeletion(target, operation.Markdown);
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
        if (target.Kind == MarkdownElementKind.BlockQuote)
        {
            ValidateQuoteFragment(document, markdown);
            return;
        }

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

    private void ValidateQuoteFragment(MarkdownSourceDocument document, string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            throw new WorkspaceMutationException("Invalid quote fragment: a complete block quote is required.");

        var elements = ParseReplacement(document, markdown);
        if (elements.Count != 1 || elements[0].Kind != MarkdownElementKind.BlockQuote
            || HasNonWhitespaceOutsideElement(markdown, elements[0]))
            throw new WorkspaceMutationException(
                "Invalid quote fragment: exactly one root BlockQuote is required.");
    }

    private static void ValidateListFragment(string source, MarkdownElement target, string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            throw new WorkspaceMutationException(
                "Invalid list fragment: exactly one item with the original indentation and marker style is required.");

        var map = target.SourceMap ?? throw new WorkspaceMutationException(
            "Unsupported list container: source ownership is unavailable.");
        if (map.MarkerStyle is null || map.Indent < 0)
            throw new WorkspaceMutationException("Unsupported list container: ambiguous marker or indentation.");

        var marker = System.Text.RegularExpressions.Regex.Match(
            markdown.Split(['\r', '\n'], 2)[0],
            @"^(?<indent>[ \t]*)(?<marker>[-+*]|[0-9]+[.)])(?=[ \t]|$)");
        var originalLine = source[map.SubtreeRange.Start..]
            .Split(['\r', '\n'], 2)[0];
        var originalMarker = System.Text.RegularExpressions.Regex.Match(
            originalLine, @"^(?<indent>[ \t]*)(?<marker>[-+*]|[0-9]+[.)])(?=[ \t]|$)");
        if (!marker.Success || !originalMarker.Success
            || marker.Groups["indent"].Value != originalMarker.Groups["indent"].Value
            || GetMarkerStyle(marker.Groups["marker"].Value) != map.MarkerStyle)
            throw new WorkspaceMutationException(
                $"Invalid list fragment at '{target.Pointer.Value}': expected indent " +
                $"'{originalMarker.Groups["indent"].Value}' and marker style '{map.MarkerStyle}'.");
    }

    private static string GetMarkerStyle(string marker)
        => char.IsDigit(marker[0]) ? "ordered:" + marker[^1] : marker;

    /// <summary>Maps existing source origins through edits; never trusts shifted li ordinals.</summary>
    private static void ValidateStructuredPatch(
        IReadOnlyList<MarkdownElement> before,
        IReadOnlyList<MarkdownElement> after,
        IReadOnlyList<PatchOperation> operations,
        IReadOnlyList<MarkdownSourceEdit> edits)
    {
        var oldByPointer = before.ToDictionary(x => x.Pointer.Value, StringComparer.Ordinal);
        var newByStart = after
            .Where(x => x.Kind != MarkdownElementKind.Document)
            .ToDictionary(x => x.SourceStart);
        var orderedEdits = edits.OrderBy(x => x.Start).ToArray();

        int? MapOrigin(int position)
        {
            var delta = 0;
            foreach (var edit in orderedEdits)
            {
                if (edit.Length > 0 && position >= edit.Start
                    && position < edit.Start + edit.Length)
                    return null;
                if (edit.Start + edit.Length <= position)
                    delta += edit.Replacement.Length - edit.Length;
            }
            return position + delta;
        }

        foreach (var old in before.Where(x => x.Kind != MarkdownElementKind.Document))
        {
            var newStart = MapOrigin(old.SourceStart);
            if (newStart is null) continue;
            if (!newByStart.TryGetValue(newStart.Value, out var current)
                || current.Kind != old.Kind || current.SelfHash != old.SelfHash)
                throw new WorkspaceMutationException(
                    $"Unsupported list container: patch would alter surviving element '{old.Pointer.Value}'.");

            if (old.Kind != MarkdownElementKind.ListItem) continue;
            if (old.SourceMap is null || current.SourceMap is null
                || old.SourceMap.Depth != current.SourceMap.Depth
                || old.SourceMap.MarkerStyle != current.SourceMap.MarkerStyle)
                throw new WorkspaceMutationException(
                    $"Unsupported list container: patch would change the depth or marker of '{old.Pointer.Value}'.");

            var oldParent = old.SourceMap.ParentPointer is null
                ? null : oldByPointer[old.SourceMap.ParentPointer];
            var expectedParentStart = oldParent is null
                ? null : MapOrigin(oldParent.SourceStart);
            var actualParentStart = current.SourceMap.ParentPointer is null
                ? (int?)null
                : after.First(x => x.Pointer.Value == current.SourceMap.ParentPointer).SourceStart;
            if (expectedParentStart != actualParentStart)
                throw new WorkspaceMutationException(
                    $"Unsupported list container: patch would reparent '{old.Pointer.Value}'.");
        }

        foreach (var operation in operations)
        {
            if (!oldByPointer.TryGetValue(operation.Pointer, out var target)
                || target.Kind is not (MarkdownElementKind.ListItem or MarkdownElementKind.BlockQuote)
                || operation.Kind == PatchOperationKind.Delete)
                continue;

            var edit = edits.Single(x => x.Pointer == operation.Pointer);
            var start = edit.Start + orderedEdits
                .Where(x => x.Start < edit.Start)
                .Sum(x => x.Replacement.Length - x.Length);
            var end = start + edit.Replacement.Length;
            var newItems = after.Where(x => x.SourceStart >= start && x.SourceStart < end
                && x.Kind == target.Kind).ToArray();

            if (target.Kind == MarkdownElementKind.BlockQuote)
            {
                if (newItems.Length != 1)
                    throw new WorkspaceMutationException(
                        "Invalid quote fragment: exactly one opaque quote must be created.");
                continue;
            }

            var root = newItems.FirstOrDefault(x => x.SourceMap is not null
                && x.SourceMap.Depth == target.SourceMap!.Depth
                && x.SourceMap.MarkerStyle == target.SourceMap.MarkerStyle);
            if (root is null || newItems.Count(x => x.SourceMap!.Depth == target.SourceMap!.Depth) != 1)
                throw new WorkspaceMutationException(
                    "Invalid list fragment: exactly one sibling item at the original level is required.");

            foreach (var child in after.Where(x => x.Kind == MarkdownElementKind.ListItem
                && x.SourceStart >= start && x.SourceStart < end && x != root))
            {
                var ancestor = child.SourceMap?.ParentPointer;
                while (ancestor is not null && ancestor != root.Pointer.Value)
                    ancestor = after.First(x => x.Pointer.Value == ancestor).SourceMap?.ParentPointer;
                if (ancestor != root.Pointer.Value)
                    throw new WorkspaceMutationException(
                        "Invalid list fragment: an additional sibling or unrelated list item was introduced.");
            }
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

    private static void ValidateSectionDeletion(
        MarkdownElement target,
        string? markdown)
    {
        if (target.Kind != MarkdownElementKind.Heading)
        {
            throw new WorkspaceMutationException(
                "delete_section requires a heading pointer.");
        }

        if (markdown is not null)
        {
            throw new WorkspaceMutationException(
                "delete_section does not accept markdown content.");
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
            "Additional headings must be nested below the replacement heading. " +
            "To remove the section, use delete_section.");

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

    private static Task<MutationResponse> RunMutationAsync(
        Func<Task<MutationResponse>> mutation,
        CancellationToken cancellationToken)
        => WorkspaceMutationGate.RunAsync(
            mutation,
            cancellationToken);
}
