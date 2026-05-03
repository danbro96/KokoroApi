using KokoroApi.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KokoroApi.Endpoints;

public static class HealthEndpoint
{
    public static IEndpointConventionBuilder MapHealthEndpoint(this IEndpointRouteBuilder app) =>
        app.MapGet("/healthz", Ok<HealthResponse> () => TypedResults.Ok(new HealthResponse { Status = "ok" }))
            .AllowAnonymous()
            .WithTags("Meta")
            .WithSummary("Liveness probe.")
            .WithDescription(
                """
                Returns 200 with `{ "status": "ok" }` as soon as the process is up. Used by the
                TrueNAS / Docker healthcheck. Does *not* wait for the Kokoro model to finish
                loading — readiness is reported by `/options` returning 503 until the model is
                ready. Anonymous — no API key required.
                """);
}
