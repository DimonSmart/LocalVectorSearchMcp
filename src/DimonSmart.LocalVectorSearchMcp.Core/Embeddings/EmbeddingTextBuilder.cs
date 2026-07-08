using System.Text;

namespace DimonSmart.LocalVectorSearchMcp.Core;

public sealed class EmbeddingTextBuilder
{
    public const string Version = "1";

    public string Build(string path, string? headingPath, string chunkText)
    {
        var builder = new StringBuilder();
        builder.Append("Path: ").AppendLine(path);
        if (!string.IsNullOrWhiteSpace(headingPath))
        {
            builder.Append("Heading: ").AppendLine(headingPath);
        }

        builder.AppendLine();
        builder.Append(chunkText.Trim());
        return builder.ToString();
    }
}
