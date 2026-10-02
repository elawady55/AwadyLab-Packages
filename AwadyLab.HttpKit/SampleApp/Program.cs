using AwadyLab.HttpKit.AspNetCore;
using AwadyLab.HttpKit.Authentication;
using AwadyLab.HttpKit.Testing;
using AwadyLab.HttpKit.Testing.Chaos;
using SampleApp;
using SampleApp.HttpKit.Generated;

var builder = WebApplication.CreateBuilder(args);

// Test-only switches, so the same build runs against real endpoints or against fixtures.
var useFixtures = builder.Configuration.GetValue("HttpKit:Testing:UseFixtures", false);

builder.Services.AddHybridCache();
builder.Services.AddHealthChecks().AddHttpKitHealthChecks();

// One call registers every ITypedHttpClient, IStreamedHttpClient and [HttpKitContract] of this assembly:
// the source generator found them at compile time, so there is no assembly scanning here.
var httpKit = builder.Services.AddHttpKit(options =>
{
    // Defaults every client starts from; a definition, a configuration section or Configure<T> layers on top.
    options.Defaults.Logging.LogHeaders = true;
    options.Defaults.Resilience.Timeout.Total = TimeSpan.FromSeconds(30);
    options.Defaults.Limits.MaxResponseBytes = 8 * 1024 * 1024;

    // Authentication providers are registered once and named; clients pick one by name.
    if (useFixtures)
    {
        // Against fixtures there is no token endpoint to call, so the same client name is served by a
        // static credential instead. The client itself is unchanged: it still just asks for "github".
        options.Auth.AddStaticBearer("github", "fixture-token");
    }
    else
    {
        options.Auth.AddOAuth2ClientCredentials("github", oauth =>
        {
            oauth.TokenEndpoint = new Uri("https://github.com/login/oauth/access_token");
            oauth.ClientId = builder.Configuration["GitHub:ClientId"] ?? "sample-client";
            oauth.ClientSecret = builder.Configuration["GitHub:ClientSecret"] ?? "sample-secret";
            oauth.Scope = "repo:read";
        });
    }

    var primaryExpiry = builder.Configuration.GetValue<DateTimeOffset?>("Stripe:PrimaryKeyNotAfter");
    options.Auth.AddRotatingApiKey(
        "stripe",
        key =>
        {
            key.Name = "Authorization";
            key.Prefix = "Bearer ";
        },
        _ => new ValueTask<ApiKey>(new ApiKey(builder.Configuration["Stripe:PrimaryKey"] ?? "sk_test_primary", "primary", primaryExpiry)),
        _ => new ValueTask<ApiKey>(new ApiKey(builder.Configuration["Stripe:SecondaryKey"] ?? "sk_test_secondary", "secondary")));

    options.Configure<StripeClient>(client => client.Authentication.Use("stripe"));

    // Test-only settings are read while the client is registered, so they belong here rather than after the
    // call. Driven by configuration, the same build runs against real endpoints or against fixtures.
    options.Configure<GitHubClient>(client =>
    {
        if (useFixtures)
            client.Testing().UseFixtures(Path.Combine(AppContext.BaseDirectory, "Fixtures", "github.mock.json"));

        if (builder.Configuration.GetValue("HttpKit:Testing:CaptureHar", false))
            client.Testing().CaptureHar(Path.Combine(AppContext.BaseDirectory, "github.har"));

        if (builder.Configuration.GetValue("HttpKit:Testing:Chaos", false))
        {
            client.Testing().InjectChaos(chaos =>
            {
                chaos.Seed = 1234;
                chaos.InjectFaults(0.2).InjectLatency(0.1, TimeSpan.FromSeconds(2));
            });
        }
    });
});

// The hooks themselves are harmless when no client opts in; a production host would guard this with an
// environment check all the same.
httpKit.AddHttpKitTesting();

// ASP.NET Core integration: propagate the incoming request's correlation headers, read the tenant and the
// caller's identity from HttpContext so multi-tenant routing and per-user cache keys just work.
httpKit.AddAspNetCore(propagation =>
{
    propagation.Headers.Add("X-Correlation-Id");
    propagation.Headers.Add("X-Request-Id");
});

var app = builder.Build();
app.UseHeaderPropagation();

app.MapGet("/", () => "AwadyLab.HttpKit sample");

// A typed client: serialization, auth, retries and caching all come from its definition.
app.MapGet("/repos/{owner}/{name}", async (string owner, string name, GitHubClient github, CancellationToken cancellationToken) =>
    await github.GetRepositoryAsync(owner, name, cancellationToken) is { } repository
        ? Results.Ok(repository)
        : Results.NotFound());

// Auto-pagination: the Link header is followed page by page and the items stream out as they arrive.
app.MapGet("/orgs/{organisation}/repos", (string organisation, GitHubClient github, CancellationToken cancellationToken) =>
    github.GetOrganisationRepositoriesAsync(organisation, cancellationToken));

// A configuration-driven client with a rotating API key.
app.MapGet("/charges/{id}", async (string id, StripeClient stripe, CancellationToken cancellationToken) =>
    await stripe.GetChargeAsync(id, cancellationToken) is { } charge ? Results.Ok(charge) : Results.NotFound());

// A declarative contract: no implementation was written by hand.
app.MapGet("/users/{id:int}", async (int id, IUsersApi users, CancellationToken cancellationToken) =>
    await users.GetAsync(id, cancellationToken) is { } user ? Results.Ok(user) : Results.NotFound());

app.MapGet("/users", (IUsersApi users, CancellationToken cancellationToken) => users.ListAllAsync(cancellationToken));

// A streamed client: server-sent events relayed without buffering.
app.MapGet("/events", (EventsClient events, CancellationToken cancellationToken) => events.WatchAsync(cancellationToken));

// Circuit-breaker state as health, so a dashboard shows which dependency is failing.
app.MapHealthChecks("/health");

// Chaos can be switched off at runtime without restarting.
app.MapPost("/chaos/{state}", (string state, IChaosController chaos) =>
{
    if (string.Equals(state, "on", StringComparison.OrdinalIgnoreCase))
        chaos.Enable();
    else
        chaos.Disable();
    return Results.Ok(new { chaos = state });
});

await app.RunAsync();

/// <summary>Exposed so the smoke test can build this app's service graph.</summary>
public partial class Program;
