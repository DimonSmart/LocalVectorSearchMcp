using System.Text.Json.Serialization;

namespace DimonSmart.LocalVectorSearchMcp.Core.Serialization;

/// <summary>
/// Uses the built-in string enum converter while rejecting numeric JSON values.
/// Keeping a converter attribute on the enum makes this rule apply even when
/// callers do not supply the server's JsonSerializerOptions.
/// </summary>
public sealed class StrictJsonStringEnumConverter<TEnum>
    : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    public StrictJsonStringEnumConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}
