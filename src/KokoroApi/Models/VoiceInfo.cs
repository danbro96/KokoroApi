using KokoroSharp.Core;

namespace KokoroApi.Models;

public sealed class VoiceInfo
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required KokoroLanguage Language { get; set; }

    public required KokoroGender Gender { get; set; }
}
