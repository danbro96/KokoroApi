using KokoroApi.Auth;
using KokoroApi.Endpoints;
using KokoroApi.Handlers;
using KokoroApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KokoroOptions>(builder.Configuration.GetSection("Kokoro"));
builder.Services.Configure<ApiKeyAuthOptions>(builder.Configuration.GetSection("Auth"));

builder.Services.AddSingleton<KokoroSynthesizer>();
builder.Services.AddSingleton<IKokoroSynthesizer>(sp => sp.GetRequiredService<KokoroSynthesizer>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<KokoroSynthesizer>());

builder.Services.AddScoped<SynthesisHandler>();

builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        document.Info = new()
        {
            Title = "KokoroApi",
            Version = "v1",
            Description =
                "Self-hosted streaming text-to-speech API powered by Kokoro 82M (KokoroSharp). " +
                "REST endpoints for discrete synthesis and a WebSocket stream at `/tts/stream`. " +
                "Authenticate by sending your key in the `X-API-Key` header.",
        };
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthOptions.HeaderName,
            Description = "API key. Send in the X-API-Key header.",
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuth = endpointMetadata.OfType<IAuthorizeData>().Any()
                        && !endpointMetadata.OfType<IAllowAnonymous>().Any();
        if (requiresAuth)
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("ApiKey", context.Document)] = new List<string>(),
            });
        }

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
            AutoReplenishment = true,
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

app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapScalarApiReference("/scalar", o => o
        .WithTitle("KokoroApi")
        .WithTheme(ScalarTheme.BluePlanet))
    .AllowAnonymous();

app.MapGet("/", () => TypedResults.Redirect("/demo/"))
   .ExcludeFromDescription()
   .AllowAnonymous();

app.MapHealthEndpoint();
app.MapOptionsEndpoint().RequireAuthorization();
app.MapSynthesize().RequireAuthorization();
app.MapStream().RequireAuthorization();

app.Run();
