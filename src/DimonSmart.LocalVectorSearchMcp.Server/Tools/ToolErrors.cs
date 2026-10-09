using System.Text.Json;
using DimonSmart.LocalVectorSearchMcp.Core.KnowledgeBases;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Workspaces;
using ModelContextProtocol.Protocol;

namespace DimonSmart.LocalVectorSearchMcp.Server.Tools;

internal static class ToolErrors
{
    public static CallToolResult FromException(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            throw exception;
        }

        var (code, message) = exception switch
        {
            WorkspaceImageException image => (image.Code, image.Message),
            KnowledgeBaseAccessException => (
                "INVALID_PATH", "The requested path is not permitted."),
            DocumentNotFoundException => (
                "NOT_FOUND", "The requested Markdown file was not found."),
            DocumentConflictException or SemanticAnchorConflictException => (
                "CONFLICT", "Source content changed; reread before retrying."),
            IndexNotReadyException => (
                "INDEX_NOT_READY", "The search index is not ready."),
            UnauthorizedAccessException => (
                "PERMISSION_DENIED", "Access was denied."),
            SemanticPointerFormatException => (
                "INVALID_ARGUMENT", exception.Message),
            SemanticPointerNotFoundException => (
                "INVALID_ARGUMENT", exception.Message),
            ArgumentException => (
                "INVALID_ARGUMENT", "Invalid argument."),
            WorkspaceMutationException mutation => (
                "INVALID_ARGUMENT", mutation.Message),
            _ => ("INTERNAL_ERROR", "The operation failed unexpectedly.")
        };
        return Create(code, message);
    }

    public static CallToolResult Create(string code, string message)
        => new()
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(new { code, message })
                }
            ],
            IsError = true
        };
}
