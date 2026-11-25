using System.Net;
using Toucan.Core.Contracts;

namespace Toucan.Core.Tests.Sources;

internal sealed class MemorySecretService : ISecretService
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    public int Writes { get; private set; }
    public string? GetSecret(string key) { lock (_values) return _values.GetValueOrDefault(key); }
    public void SetSecret(string key, string? value)
    {
        lock (_values)
        {
            Writes++;
            if (string.IsNullOrEmpty(value)) _values.Remove(key); else _values[key] = value;
        }
    }
    public bool Remove(string key) { lock (_values) return _values.Remove(key); }
    public IReadOnlyList<string> Keys(string prefix = "") { lock (_values) return [.. _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Order()]; }
    public int RemoveAll(string prefix) { lock (_values) return _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList().Count(_values.Remove); }
}

/// <summary>A scripted HTTP server: each request is answered by the function, which also sees what was sent.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> answer) : HttpMessageHandler
{
    private int _count;
    public List<(HttpMethod Method, Uri Url, string? Authorization, string Body, IReadOnlyDictionary<string, string> Headers)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        lock (Requests) Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body, headers));
        var response = answer(request, Interlocked.Increment(ref _count) - 1);
        response.RequestMessage ??= request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
