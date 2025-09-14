using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Oris.HttpKit.Services;

public class OrisHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly int _maxRetries;
    private readonly string? _bearerToken;

    private readonly ConcurrentDictionary<string, (object data, DateTime expiry)> _cache =
        new ConcurrentDictionary<string, (object, DateTime)>();
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<
        string,
        (int failureCount, DateTime lastFailure)
    > _circuitBreaker = new ConcurrentDictionary<string, (int, DateTime)>();
    private readonly int _failureThreshold = 3;
    private readonly TimeSpan _circuitResetTime = TimeSpan.FromSeconds(30);

    public OrisHttpClient(HttpClient httpClient, string? bearerToken = null, int maxRetries = 3)
    {
        _httpClient = httpClient;
        _bearerToken = bearerToken;
        _maxRetries = maxRetries;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        if (!string.IsNullOrEmpty(_bearerToken))
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);

        // Ensure logs folder exists
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        if (!Directory.Exists(logDir))
            Directory.CreateDirectory(logDir);

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logDir, "api_helper.log"),
                rollingInterval: RollingInterval.Day
            )
            .MinimumLevel.Debug()
            .CreateLogger();
    }

    private string BuildQueryString(Dictionary<string, string>? queryParams)
    {
        if (queryParams == null || queryParams.Count == 0)
            return string.Empty;

        var list = new List<string>();
        foreach (var kvp in queryParams)
            list.Add($"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");

        return "?" + string.Join("&", list);
    }

    private void AddHeaders(Dictionary<string, string>? headers)
    {
        if (headers == null)
            return;

        foreach (var h in headers)
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);
    }

    private bool IsCircuitOpen(string url)
    {
        if (_circuitBreaker.TryGetValue(url, out var entry))
        {
            if (
                entry.failureCount >= _failureThreshold
                && DateTime.UtcNow - entry.lastFailure < _circuitResetTime
            )
            {
                Log.Warning("Circuit is OPEN for {Url}. Skipping request.", url);
                return true;
            }
            else if (DateTime.UtcNow - entry.lastFailure >= _circuitResetTime)
                _circuitBreaker[url] = (0, DateTime.MinValue); // reset after time
        }
        return false;
    }

    private void RegisterFailure(string url)
    {
        _circuitBreaker.AddOrUpdate(
            url,
            (1, DateTime.UtcNow),
            (_, old) => (old.failureCount + 1, DateTime.UtcNow)
        );

        Log.Error(
            "Request failed for {Url}. Failure count: {Count}",
            url,
            _circuitBreaker[url].failureCount
        );
    }

    private async Task<T?> SendRequestAsync<T>(Func<Task<HttpResponseMessage>> action, string url)
    {
        if (IsCircuitOpen(url))
            return default;

        for (int i = 0; i < _maxRetries; i++)
        {
            try
            {
                Log.Information("Sending request to {Url}. Attempt {Attempt}", url, i + 1);
                var response = await action();
                response.EnsureSuccessStatusCode();

                Log.Information("Request SUCCESS for {Url}", url);
                var data = await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
                return data;
            }
            catch (Exception ex) when (i < _maxRetries - 1)
            {
                int delay = (int)Math.Pow(2, i) * 1000;
                Log.Warning(
                    "Retry {Attempt}/{Max} for {Url} due to: {Message}. Waiting {Delay}ms",
                    i + 1,
                    _maxRetries,
                    url,
                    ex.Message,
                    delay
                );
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                RegisterFailure(url);
                Log.Error("Final failure for {Url}: {Message}", url, ex.Message);
            }
        }
        return default;
    }

    // GET with caching
    public async Task<T?> GetAsync<T>(
        string url,
        Dictionary<string, string>? queryParams = null,
        Dictionary<string, string>? headers = null
    )
    {
        AddHeaders(headers);
        url += BuildQueryString(queryParams);

        if (_cache.TryGetValue(url, out var cached) && cached.expiry > DateTime.UtcNow)
        {
            Log.Information("Cache HIT for {Url}", url);
            return (T)cached.data;
        }

        var result = await SendRequestAsync<T>(() => _httpClient.GetAsync(url), url);
        if (result != null)
        {
            _cache[url] = (result!, DateTime.UtcNow + _cacheDuration);
            Log.Information("Cache SET for {Url}", url);
        }
        return result;
    }

    // POST
    public Task<TResponse?> PostAsync<TRequest, TResponse>(
        string url,
        TRequest body,
        Dictionary<string, string>? headers = null
    )
    {
        AddHeaders(headers);
        var json = JsonSerializer.Serialize(body, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return SendRequestAsync<TResponse>(() => _httpClient.PostAsync(url, content), url);
    }

    // PUT
    public Task<TResponse?> PutAsync<TRequest, TResponse>(
        string url,
        TRequest body,
        Dictionary<string, string>? headers = null
    )
    {
        AddHeaders(headers);
        var json = JsonSerializer.Serialize(body, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return SendRequestAsync<TResponse>(() => _httpClient.PutAsync(url, content), url);
    }

    // DELETE
    public Task<T?> DeleteAsync<T>(string url, Dictionary<string, string>? headers = null)
    {
        AddHeaders(headers);
        return SendRequestAsync<T>(() => _httpClient.DeleteAsync(url), url);
    }

    // PATCH
    public Task<TResponse?> PatchAsync<TRequest, TResponse>(
        string url,
        TRequest body,
        Dictionary<string, string>? headers = null
    )
    {
        AddHeaders(headers);
        var json = JsonSerializer.Serialize(body, _jsonOptions);
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return SendRequestAsync<TResponse>(() => _httpClient.SendAsync(request), url);
    }
}
