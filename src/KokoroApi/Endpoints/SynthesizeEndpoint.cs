using KokoroApi.Models;
using KokoroApi.Services;

namespace KokoroApi.Endpoints;

public static class SynthesizeEndpoint
{
    public static IEndpointConventionBuilder MapSynthesize(this IEndpointRouteBuilder app)
    {
        return app.MapPost("/tts", async (
            SynthesizeRequest req,
            IKokoroSynthesizer synth,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Text))
                return Results.BadRequest(new { error = "text is required" });

            try
            {
                var wav = await synth.SynthesizeWavAsync(req.Text, req.Voice, req.Speed, ct);
                return Results.File(wav, "audio/wav");
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}
