using KokoroSharp.Core;

namespace KokoroApi.Models;

public sealed class VoiceInfo
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required KokoroLanguage Language { get; set; }
    public required KokoroGender Gender { get; set; }
}

public sealed class SpeedRange
{
    public required float Min { get; set; }
    public required float Max { get; set; }
    public required float Default { get; set; }
}

public sealed class OptionsResponse
{
    public required string DefaultVoice { get; set; }
    public required SpeedRange Speed { get; set; }
    public required int MaxTextLength { get; set; }
    public required IReadOnlyList<VoiceInfo> Voices { get; set; }
    public required IReadOnlyList<KokoroLanguage> Languages { get; set; }
    public required IReadOnlyList<KokoroGender> Genders { get; set; }
}
