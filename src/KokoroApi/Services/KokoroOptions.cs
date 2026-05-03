namespace KokoroApi.Services;

public sealed class KokoroOptions
{
    public string DefaultVoice { get; set; } = "af_heart";

    public float DefaultSpeed { get; set; } = 1.0f;

    public float SpeedMin { get; set; } = 0.5f;

    public float SpeedMax { get; set; } = 2.0f;

    public int MinSegmentChars { get; set; } = 30;

    public int MaxBufferChars { get; set; } = 400;

    public int MaxTextLength { get; set; } = 4_000;
}
