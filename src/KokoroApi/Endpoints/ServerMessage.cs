namespace KokoroApi.Endpoints;

internal sealed class ServerMessage
{
    public string Type { get; set; } = string.Empty;

    public int? Id { get; set; }

    public string? Text { get; set; }

    public string? Message { get; set; }
}
