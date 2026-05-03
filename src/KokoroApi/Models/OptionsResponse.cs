using KokoroSharp.Core;

namespace KokoroApi.Models;

public sealed class OptionsResponse
{
    public required string DefaultVoice { get; set; }

    public required SpeedRange Speed { get; set; }

    public required int MaxTextLength { get; set; }

    public required IReadOnlyList<VoiceInfo> Voices { get; set; }

    public required IReadOnlyList<KokoroLanguage> Languages { get; set; }

    public required IReadOnlyList<KokoroGender> Genders { get; set; }
}
