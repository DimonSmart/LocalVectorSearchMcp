using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Infrastructure.Yaml;

public sealed class ReadOnlyStringListYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
        => type == typeof(IReadOnlyList<string>);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        => rootDeserializer(typeof(List<string>)) ?? Array.Empty<string>();

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => serializer(value);
}
