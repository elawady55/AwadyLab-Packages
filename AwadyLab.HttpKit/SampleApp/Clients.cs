using AwadyLab.HttpKit.Abstractions;
using AwadyLab.HttpKit.Contracts;
using AwadyLab.HttpKit.Http;
using AwadyLab.HttpKit.Options;
using AwadyLab.HttpKit.Pagination;
using AwadyLab.HttpKit.Streaming;

namespace SampleApp;

/// <summary>A repository as the GitHub API returns it.</summary>
public sealed record Repository(long Id, string Name, string FullName);

/// <summary>A charge as the Stripe API returns it.</summary>
public sealed record Charge(string Id, long Amount, string Currency);

/// <summary>A user of the demo API.</summary>
public sealed record User(int Id, string Name, string Email);

/// <summary>
/// Everything this client needs that is not worth putting in configuration: the base address, the
/// authentication provider it uses, and its resilience shape. A definition is ordinary code, so it can be
/// unit-tested on its own.
/// </summary>
public sealed class GitHubDefinition : ITypedHttpClientDefinition
{
    /// <inheritdoc />
    public void Configure(HttpKitClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.BaseUrl.Address = new Uri("https://api.github.com/");
        options.HeaderPolicy.DefaultHeaders["User-Agent"] = "AwadyLab.HttpKit.Sample";
        options.HeaderPolicy.DefaultHeaders["Accept"] = "application/vnd.github+json";

        // The provider itself is registered once in Program.cs; the client only names it.
        options.Authentication.Use("github");

        options.Resilience.Retry.MaxRetryAttempts = 3;
        options.Resilience.Retry.Delay = TimeSpan.FromMilliseconds(200);
        options.Resilience.CircuitBreaker.Enabled = true;
        options.Caching.Enabled = true;
        options.Caching.DefaultTtl = TimeSpan.FromMinutes(5);
    }
}

/// <summary>Configured by its definition; talks to GitHub with an OAuth2 client-credentials token.</summary>
[ConfigureByDefinition(typeof(GitHubDefinition))]
public sealed class GitHubClient(HttpClient http) : ITypedHttpClient
{
    /// <summary>One repository.</summary>
    public Task<Repository?> GetRepositoryAsync(string owner, string name, CancellationToken cancellationToken = default) =>
        http.GetAsync<Repository>($"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken);

    /// <summary>Every repository of an organisation, page by page, following the RFC 8288 <c>Link</c> header.</summary>
    public IAsyncEnumerable<Repository> GetOrganisationRepositoriesAsync(string organisation, CancellationToken cancellationToken = default) =>
        http.GetPagedAsync(
            $"orgs/{Uri.EscapeDataString(organisation)}/repos?per_page=100",
            new LinkHeaderPagination<Repository>(),
            new PaginationOptions { MaxPages = 10, MaxItems = 1_000 },
            cancellationToken);
}

/// <summary>
/// Configured from <c>HttpKit:Clients:Stripe</c> in appsettings.json, so the base address, timeouts and
/// retry shape are operational settings rather than code.
/// </summary>
[ConfigureByOptions("HttpKit:Clients:Stripe")]
public sealed class StripeClient(HttpClient http) : ITypedHttpClient
{
    /// <summary>One charge.</summary>
    public Task<Charge?> GetChargeAsync(string id, CancellationToken cancellationToken = default) =>
        http.GetAsync<Charge>($"v1/charges/{Uri.EscapeDataString(id)}", cancellationToken: cancellationToken);

    /// <summary>Creates a charge. The idempotency key makes the call safe to retry.</summary>
    public Task<Charge?> CreateChargeAsync(long amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default) =>
        http.PostFormDataAsync<Charge>(
            "v1/charges",
            form => form.Add("amount", amount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Add("currency", currency),
            new RequestOptions { IdempotencyKey = idempotencyKey },
            cancellationToken);
}

/// <summary>
/// A declarative contract: the generator writes the implementation, so there is no client class to maintain
/// for endpoints that are nothing but a URL and a shape.
/// </summary>
[HttpKitContract(BasePath = "api")]
[ConfigureByOptions("HttpKit:Clients:Users")]
public interface IUsersApi
{
    /// <summary>One user.</summary>
    [Get("users/{id}")]
    Task<User?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>A page of users.</summary>
    [Get("users")]
    Task<User[]?> SearchAsync(string? q, int? limit, CancellationToken cancellationToken = default);

    /// <summary>Creates a user.</summary>
    [Post("users")]
    Task<User?> CreateAsync([Body] User user, CancellationToken cancellationToken = default);

    /// <summary>Every user, walking pages by offset and limit.</summary>
    [Get("users")]
    [Paginated(typeof(OffsetLimitPagination<User>))]
    IAsyncEnumerable<User> ListAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A streamed client: no buffering, no retries, no caching. The options type it is handed cannot even
/// express those, so the compiler rules out the mistake rather than a runtime check.
/// </summary>
public sealed class EventsDefinition : IStreamedHttpClientDefinition
{
    /// <inheritdoc />
    public void Configure(HttpKitStreamedClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.BaseUrl.Address = new Uri("https://events.example.test/");
        options.ConnectTimeout = TimeSpan.FromSeconds(10);
    }
}

/// <summary>Consumes server-sent events and NDJSON without ever holding the stream in memory.</summary>
[ConfigureByDefinition(typeof(EventsDefinition))]
public sealed class EventsClient(HttpClient http) : IStreamedHttpClient
{
    /// <summary>The live event feed; reconnects with <c>Last-Event-ID</c> when the connection drops.</summary>
    public IAsyncEnumerable<SseEvent> WatchAsync(CancellationToken cancellationToken = default) =>
        http.GetServerSentEventsAsync("events", new SseOptions { MaxReconnects = 5 }, cancellationToken);

    /// <summary>An NDJSON feed, yielding each line as it arrives.</summary>
    public IAsyncEnumerable<User?> StreamUsersAsync(CancellationToken cancellationToken = default) =>
        http.GetNdjsonAsync<User>("users.ndjson", cancellationToken: cancellationToken);
}
