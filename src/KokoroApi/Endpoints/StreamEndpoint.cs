using KokoroApi.Services;
using Microsoft.Extensions.Options;

namespace KokoroApi.Endpoints;

public static class StreamEndpoint
{
    public static IEndpointConventionBuilder MapStream(this IEndpointRouteBuilder app)
    {
        return app.MapGet("/tts/stream", async (
            HttpContext ctx,
            IKokoroSynthesizer synth,
            IOptions<KokoroOptions> kopts,
            ILoggerFactory lf,
            CancellationToken ct) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                return;
            }

            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            var session = new StreamSession(ws, synth, kopts.Value, lf.CreateLogger("KokoroApi.Stream"));
            await session.RunAsync(ct);
        })
        .WithTags("Stream")
        .WithSummary("WebSocket: bidirectional TTS stream.")
        .WithDescription(
            """
            Open with `wss://host/tts/stream?api_key=<key>` (or send `X-API-Key` on the upgrade).

            Client → server (text frames, JSON):
              `{type:"config", voice?, speed?}`  — once, optional
              `{type:"text",   delta}`           — append to per-connection buffer
              `{type:"flush"}`                   — synthesize what's buffered now
              `{type:"cancel"}`                  — drop pending segments + clear buffer

            Server → client:
              Binary frames — int16 LE PCM @ 24 kHz mono
              Text frames   — `{type:"segment_start", id, text}`,
                              `{type:"segment_end",   id}`,
                              `{type:"error",         message}`
            """);
    }
}
