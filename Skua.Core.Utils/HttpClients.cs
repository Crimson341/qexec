using System.Net;
using System.Net.Http.Headers;

namespace Skua.Core.Utils;

/// <summary>
/// HttpClient with connection pooling
/// </summary>
public class WebClient : HttpClient
{
    //private readonly string _authString1 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("726820423be5c752df62:63b2a5b1a55fbeade88deab3b6c8914808bad7a6"));
    private readonly string _authString2 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("449f889db3d655d2ef4a:27863d426bc5bb46c410daf7ed6b479ba4a9f7eb"));

    /// <param name="accJson"></param>
    public WebClient(bool accJson) : base(CreateHttpClientHandler())
    {
        DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _authString2);
        if (accJson)
            DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        DefaultRequestHeaders.UserAgent.ParseAdd("Skua");
        Timeout = TimeSpan.FromSeconds(30);
    }

    /// <param name="token"></param>
    public WebClient(string token) : base(CreateHttpClientHandler())
    {
        DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);
        DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        DefaultRequestHeaders.UserAgent.ParseAdd("Skua/ScriptsUser");
        Timeout = TimeSpan.FromSeconds(30);
    }

    private static HttpClientHandler CreateHttpClientHandler()
    {
        return new HttpClientHandler()
        {
            MaxConnectionsPerServer = 10,
            UseCookies = false
        };
    }
}

/// <summary>
/// All HttpClients
/// </summary>
public static class HttpClients
{
    private static readonly SemaphoreSlim _githubApiSemaphore = new(5, 5);
    private static DateTime _lastGitHubApiCall = DateTime.MinValue;
    private static readonly TimeSpan _minDelayBetweenCalls = TimeSpan.FromMilliseconds(100);

    static HttpClients()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls13 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
    }

    /// <summary>
    /// Gets the GitHub Client
    /// </summary>
    public static WebClient GitHubClient { get; private set; } = new(true);

    /// <summary>
    /// Gets the GitHub User Client
    /// </summary>
    public static WebClient? UserGitHubClient { get; set; } = null;

    /// <summary>
    /// Gets the Map Client
    /// </summary>
    public static WebClient GetAQContent { get; set; } = new(false);

    /// <summary>
    /// Default HttpClient
    /// </summary>
    public static HttpClient Default { get; private set; } = CreateSafeHttpClient();

    /// <summary>
    /// GitHub Raw Content Client - for raw.githubusercontent.com requests
    /// </summary>
    public static HttpClient GitHubRaw { get; private set; } = CreateSafeGitHubRawClient();

    /// <summary>
    /// Creates a new HttpClient that won't cause socket exhaustion - use this instead of 'new HttpClient()'
    /// </summary>
    /// <returns>A properly configured HttpClient</returns>
    public static HttpClient CreateSafeHttpClient()
    {
        return CreateHttpClient("Skua/1.0", TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Creates a new HttpClient for raw.githubusercontent.com that won't cause socket exhaustion - use this instead of 'new HttpClient()' for github raw requests only.
    /// </summary>
    /// <returns>A properly configured HttpClient</returns>
    public static HttpClient CreateSafeGitHubRawClient()
    {
        return CreateHttpClient("Skua/1.0", TimeSpan.FromSeconds(30), true);
    }

    /// <summary>
    /// Makes a GitHub API request with automatic rate limiting and validation
    /// </summary>
    public static async Task<HttpResponseMessage> MakeGitHubApiRequestAsync(string url)
    {
        await _githubApiSemaphore.WaitAsync();
        try
        {
            var timeSinceLastCall = DateTime.UtcNow - _lastGitHubApiCall;
            if (timeSinceLastCall < _minDelayBetweenCalls)
            {
                await Task.Delay(_minDelayBetweenCalls - timeSinceLastCall);
            }
            _lastGitHubApiCall = DateTime.UtcNow;
            var client = GetGHClient();
            var response = await client.GetAsync(url);

            response.EnsureSuccessStatusCode();

            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues))
            {
                if (int.TryParse(remainingValues.FirstOrDefault(), out var remaining) && remaining < 10)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
            return response;
        }
        finally
        {
            _githubApiSemaphore.Release();
        }
    }

    private static HttpClient CreateHttpClient(string userAgent, TimeSpan timeout, bool githubraw = false)
    {
        HttpClientHandler handler = new()
        {
            MaxConnectionsPerServer = 10,
            UseCookies = false
        };
        HttpClient client = new(handler);
        if (githubraw)
        {
            client.BaseAddress = new Uri("https://raw.githubusercontent.com/");
            client.Timeout = timeout;
        }
        else
        {
            client.Timeout = timeout;
        }
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    /// <summary>
    /// Gets either the GitHub or Github User Client
    /// </summary>
    /// <returns>Client Type</returns>
    public static HttpClient GetGHClient()
    {
        return UserGitHubClient is not null ? UserGitHubClient : GitHubClient;
    }
}

/// <summary>
/// Extension methods for validated HTTP requests
/// </summary>
public static class ValidatedHttpExtensions
{
    /// <summary>
    /// Makes a validated GET request that throws on failure
    /// </summary>
    public static async Task<HttpResponseMessage> GetAsync(this HttpClient client, string requestUri)
    {
        var response = await client.GetAsync(requestUri);
        response.EnsureSuccessStatusCode();
        return response;
    }

    /// <summary>
    /// Makes a validated GET request with cancellation token that throws on failure
    /// </summary>
    public static async Task<HttpResponseMessage> GetAsync(this HttpClient client, string requestUri, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return response;
    }

    /// <summary>
    /// Gets string content with validation
    /// </summary>
    public static async Task<string> GetStringAsync(this HttpClient client, string requestUri)
    {
        using var response = await GetAsync(client, requestUri);
        var content = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? throw new InvalidDataException("Response content is empty or null") : content;
    }
}
