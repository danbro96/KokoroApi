using KokoroApi.Models;
using KokoroApi.Services;
using KokoroSharp.Core;
using Microsoft.Extensions.Options;

namespace KokoroApi.Endpoints;

public static class OptionsEndpoint
{
    public static IEndpointConventionBuilder MapOptionsEndpoint(this IEndpointRouteBuilder app)
    {
        return app.MapGet("/options", async (
            IKokoroSynthesizer synth,
            IOptions<KokoroOptions> kopts,
            CancellationToken ct) =>
        {
            if (!await synth.WaitReadyAsync(ct))
                return Results.Problem("Model not ready.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var opts = kopts.Value;
            var voices = synth.GetVoices();
            var languages = voices.Select(v => v.Language).Distinct().OrderBy(l => l).ToList();
            var genders = Enum.GetValues<KokoroGender>().Where(g => g != KokoroGender.Both).ToList();

            return Results.Ok(new OptionsResponse(
                DefaultVoice: opts.DefaultVoice,
                Speed: new SpeedRange(opts.SpeedMin, opts.SpeedMax, opts.DefaultSpeed),
                MaxTextLength: opts.MaxTextLength,
                Voices: voices,
                Languages: languages,
                Genders: genders));
        });
    }
}
