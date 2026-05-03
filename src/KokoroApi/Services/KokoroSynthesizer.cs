using KokoroApi.Models;
using KokoroApi.Streaming;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace KokoroApi.Services;

public sealed class KokoroSynthesizer : IKokoroSynthesizer, IHostedService, IAsyncDisposable
{
    private static readonly ActivitySource _activitySource = new("KokoroApi.Synthesis");

    private readonly ILogger<KokoroSynthesizer> _log;
    private readonly KokoroOptions _opts;
    private readonly TaskCompletionSource<KokoroTTS> _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private KokoroTTS? _tts;
    private int _lastReportedPercent = -1;

    public KokoroSynthesizer(ILogger<KokoroSynthesizer> log, IOptions<KokoroOptions> opts)
    {
        _log = log;
        _opts = opts.Value;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _ = Task.Run(
            async () =>
        {
            try
            {
                var voicesDir = Path.Combine(AppContext.BaseDirectory, "voices");
                if (Directory.Exists(voicesDir))
                {
                    KokoroVoiceManager.LoadVoicesFromPath(voicesDir);
                    _log.LogInformation("Loaded {Count} voices from {Path}.", KokoroVoiceManager.Voices.Count, voicesDir);
                }
                else
                {
                    _log.LogWarning("Voices directory not found at {Path}.", voicesDir);
                }

                _log.LogInformation("Loading Kokoro model (float32) — first run will download ~320 MB.");
                var tts = await KokoroTTS.LoadModelAsync(
                    KModel.float32,
                    OnDownloadProgress: p =>
                    {
                        var pct = (int) (p * 100);
                        if (pct != _lastReportedPercent && pct % 10 == 0)
                        {
                            _lastReportedPercent = pct;
                            _log.LogInformation("Model download {Percent}%", pct);
                        }
                    },
                    sessionOptions: null);
                _tts = tts;
                _readyTcs.TrySetResult(tts);
                _log.LogInformation("Kokoro model loaded.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Kokoro model load failed.");
                _readyTcs.TrySetException(ex);
            }
        }, ct);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> WaitReadyAsync(CancellationToken ct)
    {
        using var reg = ct.Register(() => _readyTcs.TrySetCanceled(ct));
        try
        {
            await _readyTcs.Task;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<byte[]> SynthesizeWavAsync(string text, string? voice, float? speed, CancellationToken ct)
    {
        var samples = await SynthesizeFloatsAsync(text, voice, speed, ct);
        return PcmEncoder.FloatToWav(samples);
    }

    public async Task<float[]> SynthesizeSegmentAsync(string segment, string? voice, float? speed, CancellationToken ct)
    {
        return await SynthesizeFloatsAsync(segment, voice, speed, ct);
    }

    private async Task<float[]> SynthesizeFloatsAsync(string text, string? voiceName, float? speed, CancellationToken ct)
    {
        using var activity = _activitySource.StartActivity("synthesize");

        var tts = _tts ?? await _readyTcs.Task.WaitAsync(ct);
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return Array.Empty<float>();
        if (trimmed.Length > _opts.MaxTextLength)
            throw new ArgumentException($"Text exceeds {_opts.MaxTextLength} characters.");

        // KokoroSharp's preprocessor drops input that doesn't end in sentence punctuation.
        if (!EndsWithTerminator(trimmed)) trimmed += ".";

        var resolvedVoice = ResolveVoice(voiceName ?? _opts.DefaultVoice);
        var resolvedSpeed = speed ?? _opts.DefaultSpeed;
        if (resolvedSpeed < _opts.SpeedMin || resolvedSpeed > _opts.SpeedMax)
            throw new ArgumentException($"Speed must be between {_opts.SpeedMin} and {_opts.SpeedMax}.");
        var langCode = KokoroLangCodeHelper.GetLangCode(resolvedVoice);

        var tokens = Tokenizer.Tokenize(trimmed, langCode, preprocess: true);
        var segments = SplitTokensIfNeeded(tokens);

        activity?.SetTag("voice", resolvedVoice.Name);
        activity?.SetTag("text.length", trimmed.Length);
        activity?.SetTag("speed", resolvedSpeed);
        activity?.SetTag("segments.count", segments.Count);
        activity?.SetTag("lang", langCode);

        var tcs = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var collected = new List<float>(segments.Sum(s => s.Length) * 600);
        var pending = segments.Count;
        var steps = new List<KokoroJob.KokoroJobStep>(segments.Count);
        foreach (var seg in segments)
        {
            steps.Add(new KokoroJob.KokoroJobStep(seg, resolvedVoice.Features, resolvedSpeed, samples =>
            {
                if (samples is { Length: > 0 })
                {
                    lock (collected) collected.AddRange(samples);
                }

                if (Interlocked.Decrement(ref pending) == 0)
                {
                    float[] arr;
                    lock (collected) arr = collected.ToArray();
                    tcs.TrySetResult(arr);
                }
            }));
        }

        var job = new KokoroJob { Steps = steps };
        ((KokoroEngine) tts).EnqueueJob(job);

        using var reg = ct.Register(() =>
        {
            try
            {
                job.Cancel();
            }
            catch
            {
            }

            tcs.TrySetCanceled(ct);
        });

        return await tcs.Task;
    }

    public IReadOnlyList<VoiceInfo> GetVoices()
    {
        return KokoroVoiceManager.Voices
            .Select(v => new VoiceInfo
            {
                Id = v.Name,
                Name = FriendlyName(v.Name),
                Language = v.Language,
                Gender = v.Gender,
            })
            .OrderBy(v => v.Language)
            .ThenBy(v => v.Gender)
            .ThenBy(v => v.Id)
            .ToList();
    }

    private static string FriendlyName(string voiceId)
    {
        // Voice IDs follow Kokoro's "<lang><gender>_<name>" convention; strip the prefix.
        var i = voiceId.IndexOf('_');
        if (i < 0 || i + 1 >= voiceId.Length) return voiceId;
        var n = voiceId[(i + 1)..];
        return char.ToUpperInvariant(n[0]) + n[1..];
    }

    private static bool EndsWithTerminator(string s)
    {
        var c = s[^1];
        return c is '.' or '!' or '?' or ';' or ':' or '—' or ',' or '"' or '\'';
    }

    private static KokoroVoice ResolveVoice(string name)
    {
        var v = KokoroVoiceManager.GetVoice(name);
        if (v is null) throw new ArgumentException($"Unknown voice '{name}'.");
        return v;
    }

    private static List<int[]> SplitTokensIfNeeded(int[] tokens)
    {
        // KokoroSharp's segmentation system handles the per-step token-count cap.
        // For short inputs SplitToSegments returns a single-element list.
        var segments = SegmentationSystem.SplitToSegments(tokens, new DefaultSegmentationConfig());
        return segments.Count == 0 ? new List<int[]> { tokens } : segments;
    }

    public ValueTask DisposeAsync()
    {
        _tts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
