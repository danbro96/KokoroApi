using KokoroApi.Handlers;

namespace KokoroApi.Endpoints;

public static class OptionsEndpoint
{
    public static IEndpointConventionBuilder MapOptionsEndpoint(this IEndpointRouteBuilder app) =>
        app.MapGet("/options", (SynthesisHandler h, CancellationToken ct) => h.GetOptionsAsync(ct))
            .WithTags("Meta")
            .WithSummary("Server capabilities and runtime defaults.")
            .WithDescription(
                """
                Returns the configured default voice, the speed bounds, the max text length the
                `/tts` endpoint accepts, and the catalogue of voices supported by the loaded
                Kokoro model. 503 while the model is still downloading on first start.
                """);
}
