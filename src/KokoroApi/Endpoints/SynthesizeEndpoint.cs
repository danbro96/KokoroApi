using KokoroApi.Handlers;
using KokoroApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace KokoroApi.Endpoints;

public static class SynthesizeEndpoint
{
    public static IEndpointConventionBuilder MapSynthesize(this IEndpointRouteBuilder app) =>
        app.MapPost("/tts", (
                [FromBody] SynthesizeRequest req,
                SynthesisHandler h,
                CancellationToken ct) => h.SynthesizeAsync(req, ct))
            .WithTags("Synthesis")
            .WithSummary("Discrete text-to-speech synthesis.")
            .WithDescription(
                """
                Body: `{ "text": "...", "voice"?: "<voice-id>", "speed"?: 0.5..2.0 }`.
                Returns `audio/wav` (PCM 24 kHz mono 16-bit). Validation errors come back as
                RFC 7807 ProblemDetails with status 400.
                """)
            .Accepts<SynthesizeRequest>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: "audio/wav")
            .ProducesValidationProblem();
}
