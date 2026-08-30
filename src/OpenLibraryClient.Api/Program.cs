using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Polly;
using Polly.Retry;
using OpenLibraryClient.Api.Endpoints;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Parsing;
using OpenLibraryClient.Core.Ranking;
using OpenLibraryClient.Infrastructure.Extraction;
using OpenLibraryClient.Infrastructure.OpenLibrary;
using OpenLibraryClient.Infrastructure.Search;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Allow the local Vite dev server to call this API during frontend development.
const string FrontendDevCorsPolicy = "FrontendDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendDevCorsPolicy, policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// Core pipeline pieces: deterministic parsing, similarity scoring, and ranking are pure/no-I/O.
builder.Services.AddSingleton<IDeterministicParser, DeterministicParser>();
builder.Services.AddSingleton<ISimilarityScorer, SimilarityScorer>();
builder.Services.AddSingleton<IRankingExplainer, RankingExplainer>();
builder.Services.AddSingleton<IRelevanceRanker, RelevanceRanker>();

// LLM fallback extractor: Google Gemini (free tier, no billing required) via its OpenAI-compatible
// endpoint, consumed through Microsoft.Extensions.AI's provider-agnostic IChatClient. Because the
// extractor only depends on IChatClient, swapping providers is just a matter of pointing the OpenAI
// SDK's ChatClient at Gemini's base URL with a Gemini API key - no extractor code changes needed.
// API key comes from configuration ("Gemini:ApiKey", e.g. via `dotnet user-secrets`) or the
// GEMINI_API_KEY environment variable. If neither is set, a placeholder key is used so DI
// resolution never crashes app startup/requests; LlmBookInfoExtractor degrades gracefully when
// the placeholder inevitably fails at call time.
var configuredApiKey = builder.Configuration["Gemini:ApiKey"];
var geminiApiKey = !string.IsNullOrWhiteSpace(configuredApiKey)
    ? configuredApiKey
    : Environment.GetEnvironmentVariable("GEMINI_API_KEY");
var geminiModel = builder.Configuration["Gemini:Model"] ?? "gemini-3.5-flash-lite";

if (string.IsNullOrWhiteSpace(geminiApiKey))
{
    Console.Error.WriteLine(
        "Warning: no Gemini API key configured (set 'Gemini:ApiKey' via user-secrets or the GEMINI_API_KEY " +
        "environment variable). LLM-based extraction will fail at request time until this is set.");
    geminiApiKey = "unconfigured";
}

builder.Services.AddSingleton<IChatClient>(_ =>
    new OpenAI.Chat.ChatClient(
        geminiModel,
        new ApiKeyCredential(geminiApiKey),
        new OpenAIClientOptions { Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/") })
    .AsIChatClient());

// In-process cache backing both the Open Library and Gemini caching decorators below.
builder.Services.AddMemoryCache();

builder.Services.Configure<OpenLibraryCacheOptions>(builder.Configuration.GetSection("OpenLibrary"));
builder.Services.Configure<LlmCacheOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<GeminiResilienceOptions>(builder.Configuration.GetSection("Gemini:Resilience"));

// Retry/timeout pipeline for Gemini calls: transient failures (timeouts, 429, 5xx) are retried a
// bounded number of times with exponential backoff before LlmBookInfoExtractor's own catch-all
// degrades to a zero-confidence result. Built eagerly from configuration rather than per-request.
var geminiResilienceOptions = builder.Configuration.GetSection("Gemini:Resilience").Get<GeminiResilienceOptions>()
    ?? new GeminiResilienceOptions();
builder.Services.AddSingleton(new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = geminiResilienceOptions.MaxRetryAttempts,
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromMilliseconds(300),
        UseJitter = true,
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TaskCanceledException>()
            .Handle<ClientResultException>(ex => ex.Status == 429 || ex.Status >= 500)
    })
    .AddTimeout(TimeSpan.FromSeconds(geminiResilienceOptions.TimeoutSeconds))
    .Build());

// Real LLM extractor registered under its concrete type so the caching decorator below can wrap
// it; ILlmBookInfoExtractor (what BookSearchService/BookInfoExtractor depend on) resolves to the
// cached decorator.
builder.Services.AddSingleton<LlmBookInfoExtractor>();
builder.Services.AddSingleton<ILlmBookInfoExtractor>(sp => new CachingLlmBookInfoExtractor(
    sp.GetRequiredService<LlmBookInfoExtractor>(),
    sp.GetRequiredService<IMemoryCache>(),
    sp.GetRequiredService<IOptions<LlmCacheOptions>>()));

// Composes deterministic + LLM behind the confidence gate.
builder.Services.AddSingleton<IBookInfoExtractor, BookInfoExtractor>();

// Orchestrates extraction -> Open Library query (with zero-result relaxation/retry) -> ranking.
builder.Services.AddSingleton<IBookSearchService, BookSearchService>();

// Open Library HTTP client, registered under its concrete type so the caching decorator below
// can wrap it. AddStandardResilienceHandler adds retry (transient 5xx/408/429/network errors,
// exponential backoff+jitter), per-attempt timeout, total-request timeout, and a circuit breaker,
// all configurable via the "OpenLibrary:Resilience" configuration section.
builder.Services.AddHttpClient<OpenLibraryApiClient>(client =>
{
    client.BaseAddress = new Uri("https://openlibrary.org/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenLibraryClient/1.0 (https://github.com/openlibraryclient)");
})
    .AddStandardResilienceHandler(options => builder.Configuration.GetSection("OpenLibrary:Resilience").Bind(options));

// IOpenLibraryClient (what BookSearchService depends on) resolves to the caching decorator.
builder.Services.AddSingleton<IOpenLibraryClient>(sp => new CachingOpenLibraryClient(
    sp.GetRequiredService<OpenLibraryApiClient>(),
    sp.GetRequiredService<IMemoryCache>(),
    sp.GetRequiredService<IOptions<OpenLibraryCacheOptions>>()));

builder.Services.AddProblemDetails();

// Custom domain metrics for the search pipeline (request outcomes, duration, extraction source,
// LLM fallback reasons, retry depth, result counts) - see BookSearchMetrics for details.
builder.Services.AddSingleton<BookSearchMetrics>();

// OpenTelemetry metrics: ASP.NET Core (request duration/count), HttpClient (outbound calls to
// Open Library and Gemini), .NET runtime (GC/thread pool/exceptions), and our own
// "OpenLibraryClient.BookSearch" meter. Exported via OTLP to whatever collector/dashboard is
// configured through the standard OTEL_EXPORTER_OTLP_ENDPOINT environment variable (defaults to
// http://localhost:4317, matching the .NET Aspire Dashboard container - see README.md).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "OpenLibraryClient.Api"))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(BookSearchMetrics.MeterName)
        .AddOtlpExporter());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Defense-in-depth: BookSearchEndpoint already catches and translates search-pipeline failures
// into a clean 502, but this ensures any other unhandled exception (e.g. from middleware, model
// binding, or a future endpoint) still returns a ProblemDetails response instead of leaking a
// stack trace to the client.
app.UseExceptionHandler(exceptionApp => exceptionApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(new
    {
        title = "An unexpected error occurred.",
        status = StatusCodes.Status500InternalServerError
    });
}));

app.UseCors(FrontendDevCorsPolicy);

// Skip the http->https redirect in Development: it forces a cross-port redirect to the ASP.NET
// dev certificate, which browsers other than the one used for `dotnet dev-certs https --trust`
// (e.g. Firefox, which has its own certificate store) will fail to establish TLS with - and
// that failure surfaces confusingly as a CORS error rather than a certificate error. The
// frontend just talks to the http endpoint directly during local development.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapBookSearch();

app.Run();

// Exposed so WebApplicationFactory<Program> can be used from integration tests.
public partial class Program;
