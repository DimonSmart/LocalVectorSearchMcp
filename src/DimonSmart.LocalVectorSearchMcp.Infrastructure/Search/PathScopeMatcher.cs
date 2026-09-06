using DimonSmart.LocalVectorSearchMcp.Core.Exceptions;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Search;

internal sealed class PathScopeMatcher
{
    private readonly Matcher matcher;

    public PathScopeMatcher(SearchPathScope scope)
    {
        matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        var includes = scope.IncludeGlobs.Count == 0 ? ["**/*.md"] : scope.IncludeGlobs;
        try
        {
            foreach (var include in includes)
            {
                if (string.IsNullOrWhiteSpace(include)) throw new ArgumentException();
                matcher.AddInclude(include);
            }

            foreach (var exclude in scope.ExcludeGlobs)
            {
                if (string.IsNullOrWhiteSpace(exclude)) throw new ArgumentException();
                matcher.AddExclude(exclude);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new ConfigurationException("Search includeGlobs and excludeGlobs must contain valid, non-blank filesystem glob patterns.", exception);
        }
    }

    public bool Matches(string path) => matcher.Match(path.Replace('\\', '/')).HasMatches;
}
