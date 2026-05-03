using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using KokoroApi.Auth;
using KokoroApi.Endpoints;
using KokoroApi.Handlers;
using KokoroApi.Models;
using KokoroApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KokoroOptions>(builder.Configuration.GetSection("Kokoro"));
builder.Services.Configure<ApiKeyAuthOptions>(builder.Configuration.GetSection("Auth"));

builder.Services.AddSingleton<KokoroSynthesizer>();
builder.Services.AddSingleton<IKokoroSynthesizer>(sp => sp.GetRequiredService<KokoroSynthesizer>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<KokoroSynthesizer>());

builder.Services.AddScoped<SynthesisHandler>();

builder.Services.AddOpenApi(opts =>
{
    opts.AddDocumentTransformer((doc, _, _) =>
    {
        doc.Components ??= new();
        doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        doc.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-API-Key",
            Description = "API key issued for the calling client.",
        };
        doc.Security ??= new List<OpenApiSecurityRequirement>();
        doc.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("ApiKey", doc)] = []
        });
        return Task.CompletedTask;
    });
});

builder.Services
    .AddAuthentication(ApiKeyAuthOptions.SchemeName)
    .AddScheme<ApiKeyAuthOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthOptions.SchemeName, opts =>
    {
        var section = builder.Configuration.GetSection("Auth");
        section.Bind(opts);
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var permitsPerMinute = builder.Configuration.GetValue("RateLimit:RequestsPerMinute", 60);
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var key = ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = permitsPerMinute,
            TokensPerPeriod = permitsPerMinute,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var allowedOrigins = builder.Configuration.GetSection("Auth:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
}

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 16_384);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64_000);

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(
            serviceName: "kokoro-api",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"))
        .WithTracing(t => t
            .AddSource("KokoroApi.Synthesis")
            .AddAspNetCoreInstrumentation(o => o.RecordException = true)
            .AddHttpClientInstrumentation()
            .AddOtlpExporter())
        .WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter());

    builder.Logging.AddOpenTelemetry(o =>
    {
        o.IncludeFormattedMessage = true;
        o.IncludeScopes = true;
        o.AddOtlpExporter();
    });
}

var app = builder.Build();

if (allowedOrigins.Length > 0) app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference("/docs", opts =>
{
    opts.WithTitle("KokoroApi");
    opts.WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Fetch);
}).AllowAnonymous();

app.MapGet("/", () => TypedResults.Redirect("/demo/"))
   .ExcludeFromDescription()
   .AllowAnonymous();

app.MapGet("/healthz", () => TypedResults.Ok(new HealthResponse { Status = "ok" }))
   .AllowAnonymous()
   .WithTags("Meta")
   .WithSummary("Liveness probe.")
   .WithDescription("Returns `{ \"status\": \"ok\" }` when the process is up. Used by the TrueNAS healthcheck. No auth.");

app.MapOptionsEndpoint().RequireAuthorization();
app.MapSynthesize().RequireAuthorization();
app.MapStream().RequireAuthorization();

app.Run();
