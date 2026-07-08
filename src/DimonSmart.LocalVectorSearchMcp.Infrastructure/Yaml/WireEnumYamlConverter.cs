using DimonSmart.LocalVectorSearchMcp.Core.Reindexing;
using DimonSmart.LocalVectorSearchMcp.Core.Search;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Yaml;

public sealed class WireEnumYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(SearchMode) || type == typeof(ReindexScope);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalar = parser.Consume<Scalar>();
        if (type == typeof(SearchMode) && SearchModeExtensions.TryParseWireValue(scalar.Value, out var mode))
        {
            return mode;
        }

        if (type == typeof(ReindexScope) && ReindexScopeExtensions.TryParseWireValue(scalar.Value, out var scope))
        {
            return scope;
        }

        var expected = type == typeof(SearchMode) ? "semantic, lexical or hybrid" : "changed or all";
        throw new YamlException(scalar.Start, scalar.End, $"{type.Name} must be {expected}.");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var wireValue = value switch
        {
            SearchMode mode => mode.ToWireValue(),
            ReindexScope scope => scope.ToWireValue(),
            _ => throw new ArgumentException($"Unsupported YAML value type: {value?.GetType().Name ?? "null"}.", nameof(value))
        };

        emitter.Emit(new Scalar(wireValue));
    }
}
