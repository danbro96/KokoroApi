namespace KokoroApi.Models;

public sealed class SynthesizeRequest
{
    public required string Text { get; set; }
    public string? Voice { get; set; }
    public float? Speed { get; set; }
}
