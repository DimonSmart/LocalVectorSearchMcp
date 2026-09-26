using System.Security.Cryptography;
using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Configuration;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using DimonSmart.LocalVectorSearchMcp.Infrastructure.Security;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Workspaces;

public sealed class WorkspaceImageMoveService : IWorkspaceImageMoveService
{
    private const int MaxAttempts = 3;
    private const int PrefixLength = 16;

    private readonly LocalVectorSearchMcpConfig config;
    private readonly KnowledgeBasePathGuard pathGuard;
    private readonly IMarkdownDocumentLoader loader;
    private readonly IMarkdownImageReferenceUpdater referenceUpdater;
    private readonly IWorkspaceIndexSynchronizationScheduler synchronizationScheduler;
    private readonly IWorkspaceImageMoveFileOperations fileOperations;

    public WorkspaceImageMoveService(
        LocalVectorSearchMcpConfig config,
        KnowledgeBasePathGuard pathGuard,
        IMarkdownDocumentLoader loader,
        IMarkdownImageReferenceUpdater referenceUpdater,
        IWorkspaceIndexSynchronizationScheduler synchronizationScheduler)
        : this(
            config,
            pathGuard,
            loader,
            referenceUpdater,
            synchronizationScheduler,
            PhysicalWorkspaceImageMoveFileOperations.Instance)
    {
    }

    internal WorkspaceImageMoveService(
        LocalVectorSearchMcpConfig config,
        KnowledgeBasePathGuard pathGuard,
        IMarkdownDocumentLoader loader,
        IMarkdownImageReferenceUpdater referenceUpdater,
        IWorkspaceIndexSynchronizationScheduler synchronizationScheduler,
        IWorkspaceImageMoveFileOperations fileOperations)
    {
        this.config = config;
        this.pathGuard = pathGuard;
        this.loader = loader;
        this.referenceUpdater = referenceUpdater;
        this.synchronizationScheduler = synchronizationScheduler;
        this.fileOperations = fileOperations;
    }

    public Task<ImageMoveResponse> MoveAsync(
        MoveImageRequest request,
        CancellationToken cancellationToken)
        => WorkspaceMutationGate.RunAsync(
            () => MoveWithRetriesAsync(
                request,
                cancellationToken),
            cancellationToken);

    private async Task<ImageMoveResponse> MoveWithRetriesAsync(
        MoveImageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureWritesEnabled();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            MovePlan? plan = null;
            try
            {
                plan = await PrepareAsync(
                    request,
                    cancellationToken);
                return await CommitAsync(
                    plan,
                    cancellationToken);
            }
            catch (DocumentConflictException) when (attempt < MaxAttempts)
            {
            }
            catch (DocumentConflictException)
            {
                throw new DocumentConflictException(
                    "The image or affected Markdown documents kept changing while the move was being applied.");
            }
            finally
            {
                if (plan is not null)
                {
                    CleanupPreparedFiles(plan.Documents);
                }
            }
        }

        throw new DocumentConflictException(
            "The image or affected Markdown documents kept changing while the move was being applied.");
    }

    private async Task<MovePlan> PrepareAsync(
        MoveImageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePath = pathGuard.ValidateWorkspaceImagePath(
            request.SourcePath);
        var targetPath = pathGuard.ValidateWorkspaceImagePath(
            request.TargetPath);
        var sourceAbsolute = pathGuard.ResolveWorkspaceImagePath(
            sourcePath);
        var targetAbsolute = pathGuard.ResolveWorkspaceImagePath(
            targetPath);

        if (sourceAbsolute.Equals(
                targetAbsolute,
                PathComparison()))
        {
            throw new WorkspaceImageException(
                "Source and target image paths must be different.");
        }

        ValidateSourceExists(
            sourcePath,
            sourceAbsolute);
        ValidateTargetAvailable(
            targetPath,
            targetAbsolute);

        if (!WorkspaceImageFormats.TryFromExtension(
                Path.GetExtension(sourcePath),
                out var sourceDeclared))
        {
            throw new WorkspaceImageException(
                $"Image '{sourcePath}' has an unsupported file extension.");
        }

        if (!WorkspaceImageFormats.TryFromExtension(
                Path.GetExtension(targetPath),
                out var targetDeclared))
        {
            throw new WorkspaceImageException(
                $"Image '{targetPath}' has an unsupported file extension.");
        }

        var imageState = await ReadImageStateAsync(
            sourceAbsolute,
            sourcePath,
            cancellationToken);
        var detected = WorkspaceImageFormats.Detect(
                imageState.Prefix)
            ?? throw new WorkspaceImageException(
                $"Image '{sourcePath}' has an unsupported or invalid image signature.");

        if (detected.Format != sourceDeclared.Format)
        {
            throw new WorkspaceImageException(
                $"Image '{sourcePath}' extension does not match its detected {detected.MimeType} format.");
        }

        if (detected.Format != targetDeclared.Format)
        {
            throw new WorkspaceImageException(
                $"Target extension for '{targetPath}' does not match the source {detected.MimeType} format.");
        }

        EnsureExpectedSha256(
            request.ExpectedSha256,
            imageState.Sha256);

        var documents = request.UpdateReferences
            ? await PrepareDocumentsAsync(
                sourcePath,
                targetPath,
                cancellationToken)
            : [];

        return new MovePlan(
            sourcePath,
            targetPath,
            sourceAbsolute,
            targetAbsolute,
            imageState.Sha256,
            documents);
    }

    private async Task<List<DocumentPlan>> PrepareDocumentsAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var result = new List<DocumentPlan>();
        try
        {
            foreach (var relativePath in EnumerateConfiguredMarkdownPaths())
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = pathGuard.ResolveMarkdownPath(relativePath);

                MarkdownSourceDocument document;
                try
                {
                    document = await loader.LoadFileAsync(
                        config.KnowledgeBase,
                        relativePath,
                        cancellationToken);
                }
                catch (Exception exception) when (
                    exception is FileNotFoundException
                        or DirectoryNotFoundException)
                {
                    throw new DocumentConflictException(
                        $"Markdown document '{relativePath}' changed while image references were being prepared.");
                }

                var update = referenceUpdater.Update(
                    document.Markdown,
                    relativePath,
                    sourcePath,
                    targetPath);
                if (update.ReferencesUpdated == 0)
                {
                    continue;
                }

                var payload = BuildMarkdownBytes(
                    update.Markdown,
                    document.HasUtf8Bom);
                var directory = Path.GetDirectoryName(
                    document.AbsolutePath)!;
                var temporaryPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(document.AbsolutePath)}.{Guid.NewGuid():N}.move-image.tmp");
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous))
                {
                    await stream.WriteAsync(
                        payload,
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                result.Add(new DocumentPlan(
                    relativePath,
                    document.AbsolutePath,
                    document.SourceHash,
                    StableHash.HashBytes(payload),
                    update.ReferencesUpdated,
                    temporaryPath));
            }

            return result;
        }
        catch
        {
            CleanupPreparedFiles(result);
            throw;
        }
    }

    private async Task<ImageMoveResponse> CommitAsync(
        MovePlan plan,
        CancellationToken cancellationToken)
    {
        var createdDirectories = new List<string>();
        var journal = new List<DocumentJournal>();
        var imageMoved = false;
        try
        {
            await ValidatePlanAsync(
                plan,
                cancellationToken);

            createdDirectories = CreateTargetDirectories(
                plan.TargetAbsolute);
            plan.TargetAbsolute = pathGuard.ResolveWorkspaceImagePath(
                plan.TargetPath);
            ValidateTargetAvailable(
                plan.TargetPath,
                plan.TargetAbsolute);

            await EnsureImageHashAsync(
                plan.SourceAbsolute,
                plan.SourcePath,
                plan.Sha256,
                cancellationToken);

            try
            {
                fileOperations.Move(
                    plan.SourceAbsolute,
                    plan.TargetAbsolute,
                    overwrite: false);
                imageMoved = true;
            }
            catch (IOException) when (
                File.Exists(plan.TargetAbsolute)
                || Directory.Exists(plan.TargetAbsolute))
            {
                throw new WorkspaceImageException(
                    $"Destination '{plan.TargetPath}' already exists.");
            }

            await EnsureImageHashAsync(
                plan.TargetAbsolute,
                plan.TargetPath,
                plan.Sha256,
                cancellationToken);

            foreach (var document in plan.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EnsureDocumentHashAsync(
                    document.AbsolutePath,
                    document.RelativePath,
                    document.SourceHash,
                    cancellationToken);

                var backupPath = document.AbsolutePath
                    + $".{Guid.NewGuid():N}.move-image.bak";
                var entry = new DocumentJournal(
                    document,
                    backupPath);
                journal.Add(entry);

                fileOperations.Move(
                    document.AbsolutePath,
                    backupPath,
                    overwrite: false);

                await EnsureDocumentHashAsync(
                    backupPath,
                    document.RelativePath,
                    document.SourceHash,
                    cancellationToken);

                if (document.TemporaryPath is null)
                {
                    throw new WorkspaceImageException(
                        "Prepared Markdown update is missing.");
                }

                fileOperations.Move(
                    document.TemporaryPath,
                    document.AbsolutePath,
                    overwrite: false);
                document.TemporaryPath = null;
                entry.InstalledNew = true;

                await EnsureDocumentHashAsync(
                    document.AbsolutePath,
                    document.RelativePath,
                    document.UpdatedHash,
                    cancellationToken);
            }

            foreach (var entry in journal)
            {
                TryDeleteFile(entry.BackupPath);
            }

            var indexError = ScheduleSynchronization(
                plan.Documents);
            return new ImageMoveResponse(
                plan.SourcePath,
                plan.TargetPath,
                plan.Sha256,
                plan.Documents.Sum(
                    item => item.ReferencesUpdated),
                plan.Documents
                    .Select(item => item.RelativePath)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                plan.Documents.Count == 0,
                indexError);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            var rollbackSucceeded = await RollbackAsync(
                plan,
                journal,
                imageMoved,
                createdDirectories,
                cancellationToken);
            if (!rollbackSucceeded)
            {
                throw new WorkspaceImageException(
                    "The image move failed and the original workspace state could not be fully restored.");
            }

            throw new WorkspaceImageException(
                "The image move could not be committed safely.");
        }
        catch
        {
            var rollbackSucceeded = await RollbackAsync(
                plan,
                journal,
                imageMoved,
                createdDirectories,
                cancellationToken);
            if (!rollbackSucceeded)
            {
                throw new WorkspaceImageException(
                    "The image move failed and the original workspace state could not be fully restored.");
            }

            throw;
        }
    }

    private async Task ValidatePlanAsync(
        MovePlan plan,
        CancellationToken cancellationToken)
    {
        plan.SourceAbsolute = pathGuard.ResolveWorkspaceImagePath(
            plan.SourcePath);
        plan.TargetAbsolute = pathGuard.ResolveWorkspaceImagePath(
            plan.TargetPath);
        ValidateSourceExists(
            plan.SourcePath,
            plan.SourceAbsolute);
        ValidateTargetAvailable(
            plan.TargetPath,
            plan.TargetAbsolute);
        await EnsureImageHashAsync(
            plan.SourceAbsolute,
            plan.SourcePath,
            plan.Sha256,
            cancellationToken);

        foreach (var document in plan.Documents)
        {
            _ = pathGuard.ResolveMarkdownPath(
                document.RelativePath);
            await EnsureDocumentHashAsync(
                document.AbsolutePath,
                document.RelativePath,
                document.SourceHash,
                cancellationToken);
        }
    }

    private List<string> CreateTargetDirectories(
        string targetAbsolute)
    {
        var root = Path.GetFullPath(config.KnowledgeBase.Root)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(targetAbsolute)!;
        var missing = new Stack<string>();
        var current = parent;

        while (!current.Equals(root, PathComparison())
               && !Directory.Exists(current))
        {
            missing.Push(current);
            current = Path.GetDirectoryName(current)
                ?? throw new WorkspaceImageException(
                    "Target image directory is outside the configured knowledge base root.");
        }

        var created = new List<string>();
        while (missing.Count > 0)
        {
            var directory = missing.Pop();
            fileOperations.CreateDirectory(directory);
            created.Add(directory);
            _ = pathGuard.ResolveWorkspaceImagePath(
                Path.GetRelativePath(
                        root,
                        targetAbsolute)
                    .Replace('\\', '/'));
        }

        return created;
    }

    private async Task<bool> RollbackAsync(
        MovePlan plan,
        IReadOnlyList<DocumentJournal> journal,
        bool imageMoved,
        IReadOnlyList<string> createdDirectories,
        CancellationToken cancellationToken)
    {
        var succeeded = true;

        foreach (var entry in journal.Reverse())
        {
            try
            {
                if (File.Exists(entry.Document.AbsolutePath))
                {
                    var currentHash = await ReadStableHashAsync(
                        entry.Document.AbsolutePath,
                        cancellationToken);
                    if (entry.InstalledNew
                        && currentHash.Equals(
                            entry.Document.UpdatedHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        fileOperations.DeleteFile(
                            entry.Document.AbsolutePath);
                    }
                    else
                    {
                        TryDeleteFile(entry.BackupPath);
                        continue;
                    }
                }

                if (File.Exists(entry.BackupPath))
                {
                    fileOperations.Move(
                        entry.BackupPath,
                        entry.Document.AbsolutePath,
                        overwrite: false);
                }
            }
            catch
            {
                succeeded = false;
            }
        }

        if (imageMoved)
        {
            try
            {
                if (!File.Exists(plan.SourceAbsolute)
                    && File.Exists(plan.TargetAbsolute))
                {
                    var targetHash = await ReadSha256Async(
                        plan.TargetAbsolute,
                        cancellationToken);
                    if (targetHash.Equals(
                        plan.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        fileOperations.Move(
                            plan.TargetAbsolute,
                            plan.SourceAbsolute,
                            overwrite: false);
                    }
                    else
                    {
                        succeeded = false;
                    }
                }
            }
            catch
            {
                succeeded = false;
            }
        }

        foreach (var directory in createdDirectories.Reverse())
        {
            try
            {
                if (Directory.Exists(directory)
                    && !Directory.EnumerateFileSystemEntries(
                            directory)
                        .Any())
                {
                    fileOperations.DeleteDirectory(directory);
                }
            }
            catch
            {
                succeeded = false;
            }
        }

        return succeeded;
    }

    private IEnumerable<string> EnumerateConfiguredMarkdownPaths()
    {
        var matcher = new Matcher(
            StringComparison.OrdinalIgnoreCase);
        foreach (var include in config.KnowledgeBase.Include)
        {
            matcher.AddInclude(include);
        }

        foreach (var exclude in config.KnowledgeBase.Exclude)
        {
            matcher.AddExclude(exclude);
        }

        var root = new DirectoryInfo(
            Path.GetFullPath(config.KnowledgeBase.Root));
        var stack = new Stack<DirectoryInfo>();
        stack.Push(root);
        var paths = new List<string>();

        while (stack.Count > 0)
        {
            var directory = stack.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = directory.GetFileSystemInfos(
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = false,
                        IgnoreInaccessible = false,
                        AttributesToSkip =
                            FileAttributes.ReparsePoint,
                        ReturnSpecialDirectories = false
                    });
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException
                    or DirectoryNotFoundException
                    or IOException)
            {
                throw new DocumentConflictException(
                    $"Markdown source set changed while image references were being prepared: {exception.Message}");
            }

            foreach (var entry in entries)
            {
                if (entry is DirectoryInfo child)
                {
                    stack.Push(child);
                    continue;
                }

                if (entry is not FileInfo file
                    || !file.Extension.Equals(
                        ".md",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(
                        root.FullName,
                        file.FullName)
                    .Replace('\\', '/');
                if (matcher.Match(relative).HasMatches)
                {
                    paths.Add(relative);
                }
            }
        }

        return paths
            .OrderBy(
                item => item,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item,
                StringComparer.Ordinal);
    }

    private static async Task<ImageState> ReadImageStateAsync(
        string absolutePath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                absolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous
                    | FileOptions.SequentialScan);
            var prefix = new byte[PrefixLength];
            var read = await stream.ReadAsync(
                prefix,
                cancellationToken);
            stream.Position = 0;
            var hash = await SHA256.HashDataAsync(
                stream,
                cancellationToken);
            return new ImageState(
                prefix[..read],
                Convert.ToHexString(hash)
                    .ToLowerInvariant());
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
                or DirectoryNotFoundException)
        {
            throw new WorkspaceImageException(
                $"Image '{relativePath}' does not exist.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            throw new WorkspaceImageException(
                $"Image '{relativePath}' could not be read safely.");
        }
    }

    private static async Task EnsureImageHashAsync(
        string absolutePath,
        string relativePath,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        string current;
        try
        {
            current = await ReadSha256Async(
                absolutePath,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
                or DirectoryNotFoundException)
        {
            throw new DocumentConflictException(
                $"Image '{relativePath}' changed while the move was being applied.");
        }

        if (!current.Equals(
                expectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentConflictException(
                $"Image '{relativePath}' changed while the move was being applied.");
        }
    }

    private static async Task EnsureDocumentHashAsync(
        string absolutePath,
        string relativePath,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        string current;
        try
        {
            current = await ReadStableHashAsync(
                absolutePath,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
                or DirectoryNotFoundException)
        {
            throw new DocumentConflictException(
                $"Markdown document '{relativePath}' changed while the image move was being applied.");
        }

        if (!current.Equals(
                expectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentConflictException(
                $"Markdown document '{relativePath}' changed while the image move was being applied.");
        }
    }

    private static async Task<string> ReadSha256Async(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous
                | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);
        return Convert.ToHexString(hash)
            .ToLowerInvariant();
    }

    private static async Task<string> ReadStableHashAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(
            absolutePath,
            cancellationToken);
        return StableHash.HashBytes(bytes);
    }

    private static byte[] BuildMarkdownBytes(
        string markdown,
        bool includeBom)
    {
        var body = new UTF8Encoding(false)
            .GetBytes(markdown);
        if (!includeBom)
        {
            return body;
        }

        return Encoding.UTF8.GetPreamble()
            .Concat(body)
            .ToArray();
    }

    private string? ScheduleSynchronization(
        IReadOnlyList<DocumentPlan> documents)
    {
        if (documents.Count == 0)
        {
            return null;
        }

        try
        {
            foreach (var document in documents)
            {
                synchronizationScheduler.Schedule(
                    document.RelativePath);
            }

            return null;
        }
        catch
        {
            return "Index synchronization could not be scheduled for one or more updated documents.";
        }
    }

    private void EnsureWritesEnabled()
    {
        if (!config.KnowledgeBase.AllowWrites)
        {
            throw new WorkspaceImageException(
                "Workspace writes are disabled. Set knowledgeBase.allowWrites to true to enable mutation tools.");
        }
    }

    private static void ValidateSourceExists(
        string relativePath,
        string absolutePath)
    {
        if (Directory.Exists(absolutePath))
        {
            throw new WorkspaceImageException(
                $"Image path '{relativePath}' is a directory.");
        }

        if (!File.Exists(absolutePath))
        {
            throw new WorkspaceImageException(
                $"Image '{relativePath}' does not exist.");
        }
    }

    private static void ValidateTargetAvailable(
        string relativePath,
        string absolutePath)
    {
        if (Directory.Exists(absolutePath))
        {
            throw new WorkspaceImageException(
                $"Destination '{relativePath}' is a directory.");
        }

        if (File.Exists(absolutePath))
        {
            throw new WorkspaceImageException(
                $"Destination '{relativePath}' already exists.");
        }
    }

    private static void EnsureExpectedSha256(
        string? expected,
        string actual)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return;
        }

        if (!expected.Equals(
                actual,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentConflictException(
                "Image has changed since it was read. Read the image again and retry the move.");
        }
    }

    private static void CleanupPreparedFiles(
        IEnumerable<DocumentPlan> documents)
    {
        foreach (var document in documents)
        {
            if (document.TemporaryPath is not null)
            {
                TryDeleteFile(
                    document.TemporaryPath);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed record ImageState(
        byte[] Prefix,
        string Sha256);

    private sealed class MovePlan(
        string sourcePath,
        string targetPath,
        string sourceAbsolute,
        string targetAbsolute,
        string sha256,
        List<DocumentPlan> documents)
    {
        public string SourcePath { get; } = sourcePath;
        public string TargetPath { get; } = targetPath;
        public string SourceAbsolute { get; set; } =
            sourceAbsolute;
        public string TargetAbsolute { get; set; } =
            targetAbsolute;
        public string Sha256 { get; } = sha256;
        public List<DocumentPlan> Documents { get; } =
            documents;
    }

    private sealed class DocumentPlan(
        string relativePath,
        string absolutePath,
        string sourceHash,
        string updatedHash,
        int referencesUpdated,
        string temporaryPath)
    {
        public string RelativePath { get; } = relativePath;
        public string AbsolutePath { get; } = absolutePath;
        public string SourceHash { get; } = sourceHash;
        public string UpdatedHash { get; } = updatedHash;
        public int ReferencesUpdated { get; } =
            referencesUpdated;
        public string? TemporaryPath { get; set; } =
            temporaryPath;
    }

    private sealed class DocumentJournal(
        DocumentPlan document,
        string backupPath)
    {
        public DocumentPlan Document { get; } = document;
        public string BackupPath { get; } = backupPath;
        public bool InstalledNew { get; set; }
    }
}

internal interface IWorkspaceImageMoveFileOperations
{
    void Move(
        string sourcePath,
        string destinationPath,
        bool overwrite);

    void CreateDirectory(string path);

    void DeleteDirectory(string path);

    void DeleteFile(string path);
}

internal sealed class PhysicalWorkspaceImageMoveFileOperations :
    IWorkspaceImageMoveFileOperations
{
    public static PhysicalWorkspaceImageMoveFileOperations Instance { get; } =
        new();

    private PhysicalWorkspaceImageMoveFileOperations()
    {
    }

    public void Move(
        string sourcePath,
        string destinationPath,
        bool overwrite)
        => File.Move(
            sourcePath,
            destinationPath,
            overwrite);

    public void CreateDirectory(string path)
        => Directory.CreateDirectory(path);

    public void DeleteDirectory(string path)
        => Directory.Delete(path);

    public void DeleteFile(string path)
        => File.Delete(path);
}
