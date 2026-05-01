using KokoroApi.Models;

namespace KokoroApi.Services;

public interface IKokoroSynthesizer
{
    Task<bool> WaitReadyAsync(CancellationToken ct);

    Task<byte[]> SynthesizeWavAsync(string text, string? voice, float? speed, CancellationToken ct);

    Task<float[]> SynthesizeSegmentAsync(string segment, string? voice, float? speed, CancellationToken ct);

    IReadOnlyList<VoiceInfo> GetVoices();
}
