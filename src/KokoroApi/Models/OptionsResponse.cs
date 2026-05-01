using KokoroSharp.Core;

namespace KokoroApi.Models;

public sealed record VoiceInfo(
    string Id,
    string Name,
    KokoroLanguage Language,
    KokoroGender Gender);

public sealed record SpeedRange(float Min, float Max, float Default);

public sealed record OptionsResponse(
    string DefaultVoice,
    SpeedRange Speed,
    int MaxTextLength,
    IReadOnlyList<VoiceInfo> Voices,
    IReadOnlyList<KokoroLanguage> Languages,
    IReadOnlyList<KokoroGender> Genders);
