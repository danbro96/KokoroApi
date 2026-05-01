namespace KokoroApi.Models;

public sealed record SynthesizeRequest(string Text, string? Voice = null, float? Speed = null);
