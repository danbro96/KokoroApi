using System.Text.Json;
using System.Text.Json.Serialization;

namespace KokoroApi.Endpoints;

internal static class StreamSerialization
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
