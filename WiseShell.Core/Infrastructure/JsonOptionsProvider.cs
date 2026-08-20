using System.Text.Json;
using System.Text.Json.Serialization;

namespace WiseShell.Core.Infrastructure;

internal static class JsonOptionsProvider
{
    public static JsonSerializerOptions Json { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };
}
