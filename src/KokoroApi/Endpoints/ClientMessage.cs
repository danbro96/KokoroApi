namespace KokoroApi.Endpoints;

internal sealed class ClientMessage
{
    public string Type { get; set; } = string.Empty;

    public string? Delta { get; set; }

    public string? Voice { get; set; }

    public float? Speed { get; set; }
}
