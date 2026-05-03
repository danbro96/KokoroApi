using KokoroApi.Models;
using KokoroApi.Services;
using KokoroSharp.Core;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace KokoroApi.Handlers;

public sealed class SynthesisHandler
{
    readonly IKokoroSynthesizer _synth;
    readonly KokoroOptions _opts;
    readonly ILogger<SynthesisHandler> _log;

    public SynthesisHandler(
        IKokoroSynthesizer synth,
        IOptions<KokoroOptions> opts,
        ILogger<SynthesisHandler> log)
    {
        _synth = synth;
        _opts = opts.Value;
        _log = log;
    }

    public async Task<Results<Ok<OptionsResponse>, ProblemHttpResult>>
        GetOptionsAsync(CancellationToken ct)
    {
        if (!await _synth.WaitReadyAsync(ct))
            return TypedResults.Problem(detail: "Model not ready.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var voices = _synth.GetVoices();
        var languages = voices.Select(v => v.Language).Distinct().OrderBy(l => l).ToList();
        var genders = Enum.GetValues<KokoroGender>().Where(g => g != KokoroGender.Both).ToList();

        return TypedResults.Ok(new OptionsResponse
        {
            DefaultVoice = _opts.DefaultVoice,
            Speed = new SpeedRange { Min = _opts.SpeedMin, Max = _opts.SpeedMax, Default = _opts.DefaultSpeed },
            MaxTextLength = _opts.MaxTextLength,
            Voices = voices,
            Languages = languages,
            Genders = genders,
        });
    }

    public async Task<Results<FileContentHttpResult, ValidationProblem, ProblemHttpResult>>
        SynthesizeAsync(SynthesizeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Text))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["text"] = ["text is required"],
            });

        try
        {
            var wav = await _synth.SynthesizeWavAsync(req.Text, req.Voice, req.Speed, ct);
            return TypedResults.File(wav, "audio/wav");
        }
        catch (ArgumentException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = [ex.Message],
            });
        }
    }
}
