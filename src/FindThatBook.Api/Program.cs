using System.Text.Json;
using System.Text.Json.Serialization;
using FindThatBook.Api.Contracts;
using FindThatBook.Api.Endpoints;
using FindThatBook.Api.Errors;
using FindThatBook.Api.Hosting;
using FindThatBook.Api.OpenApi;
using FindThatBook.Infrastructure;
using FindThatBook.Infrastructure.Configuration;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// appsettings < appsettings.{Env} < user secrets < repo-root .env < GEMINI_API_KEY-style names < AI__… env vars < args.
builder.Configuration.AddFindThatBookSources(builder.Environment.ContentRootPath);
builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; });
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
    builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
else
    builder.Logging.AddJsonConsole(o => { o.IncludeScopes = true; o.UseUtcTimestamp = true; });
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});
builder.Services.AddValidation();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx => ApiProblems.Enrich(ctx.HttpContext, ctx.ProblemDetails));
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddApiOpenApi();

builder.Services.AddOptions<RateLimitOptions>().BindConfiguration("RateLimit").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<SuggestOptions>().BindConfiguration("Suggest").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddApiRateLimiting();
builder.Services.AddTrustedForwardedHeaders();

builder.Services.AddFindThatBook(builder.Configuration); // the shared composition root (also used by the eval)
builder.Services.AddScoped<IBookSearch, PipelineBookSearch>();
builder.Services.AddSingleton<SearchResponseMapper>();

var app = builder.Build();

app.UseRequestContext();              // X-Request-Id = meta.request_id = trace_id; remembers the socket peer
app.UseTrustedForwardedHeaders();     // only when proxies are configured (SY-07)
app.UseExceptionHandler();            // Open Library failures → 502/503/504 ProblemDetails; never a stack trace outside Development
app.UseStatusCodePages();
app.UseRouting();
app.UseLoopbackOnlyEndpoints();       // admin: direct loopback callers only
app.UseRateLimiter();                 // per-IP + global search windows, search concurrency gate

var spa = app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists;
if (spa)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapOpenApi();                                                   // /openapi/v1.json, every environment
app.MapScalarApiReference(o => o.WithTitle("Find That Book API"));  // /scalar, every environment
app.MapSearchEndpoints();
app.MapSuggestEndpoints();
app.MapHealthEndpoints();
if (app.Environment.IsDevelopment()) app.MapAdminEndpoints();
app.MapApiFallback();                                               // unknown /api/* → JSON 404/405, never the SPA
if (spa) app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Entry point; public so WebApplicationFactory&lt;Program&gt; can host the API in tests.</summary>
public partial class Program;
