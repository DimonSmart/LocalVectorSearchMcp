using System.Text;
using DimonSmart.LocalVectorSearchMcp.Core.Markdown;
using DimonSmart.LocalVectorSearchMcp.Core.SemanticPointers;
using DimonSmart.LocalVectorSearchMcp.Core.Storage;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Storage;

public sealed class SqliteMarkdownSliceReader(SqliteConnectionFactory factory) : IIndexedMarkdownSliceReader
{
    public async Task<MarkdownSlice> ReadSliceAsync(
        string path,
        SemanticPointer pointer,
        int maxElements,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        maxElements = Math.Clamp(maxElements, 1, 100);
        maxBytes = Math.Clamp(maxBytes, 1, 100_000);
        await using var db = factory.Open();
        var command = db.CreateCommand();
        command.CommandText = """
            select e.pointer, e.kind, e.text, e.heading_path
            from elements e
            join documents d on d.id = e.document_id
            where d.path = $path
              and e.ordinal >= (select ordinal from elements e2 where e2.document_id = d.id and e2.pointer = $ptr)
            order by e.ordinal
            limit $max
            """;
        command.AddParameter("$path", path);
        command.AddParameter("$ptr", pointer.Value);
        command.AddParameter("$max", maxElements + 1);
        var elements = new List<MarkdownSliceElement>();
        string? nextPointer = null;
        var bytes = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var currentPointer = reader.GetString(0);
            var text = reader.GetString(2);
            if (elements.Count >= maxElements)
            {
                nextPointer = currentPointer;
                break;
            }

            var separatorBytes = elements.Count == 0 ? 0 : Encoding.UTF8.GetByteCount("\n\n");
            var projectedBytes = bytes + separatorBytes + Encoding.UTF8.GetByteCount(text);
            if (elements.Count > 0 && projectedBytes > maxBytes)
            {
                nextPointer = currentPointer;
                break;
            }

            elements.Add(new MarkdownSliceElement(
                currentPointer,
                Enum.Parse<MarkdownElementKind>(reader.GetString(1)),
                text,
                reader.IsDBNull(3) ? null : reader.GetString(3)));
            bytes = projectedBytes;
        }

        if (elements.Count == 0)
        {
            throw new SemanticPointerNotFoundException($"Pointer '{pointer.Value}' was not found in '{path}'.");
        }

        var markdown = string.Join("\n\n", elements.Select(element => element.Text));
        return new MarkdownSlice(path, pointer.Value, elements, markdown, nextPointer);
    }
}
