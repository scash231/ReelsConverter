using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ReelsConverterUI.Models;

namespace ReelsConverterUI.Services;

public sealed class BackendService : IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _jso = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public BackendService(string baseUrl = "http://127.0.0.1:8765")
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(30),
        };
    }

    public async Task<bool> WaitForHealthAsync(CancellationToken ct, int maxWait = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(maxWait);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var r = await _http.GetAsync("/api/health", ct);
                if (r.IsSuccessStatusCode) return true;
            }
            catch { /* server not ready */ }
            await Task.Delay(500, ct);
        }
        return false;
    }

    public async Task<MetadataResponse?> FetchMetadataAsync(string url, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { url });
        var resp = await _http.PostAsync("/api/metadata",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<MetadataResponse>(json, _jso);
    }

    public async Task<string> CreateJobAsync(
        string url, string mode, string platform,
        string? title, string? description, List<string>? tags,
        string? outputDir, string? privacy, bool fingerprint,
        string fingerprintMethod = "standard",
        CancellationToken ct = default)
    {
        var payload = new
        {
            url, mode, platform, title, description,
            tags = tags ?? [],
            output_dir = outputDir,
            privacy = privacy ?? "public",
            fingerprint,
            fingerprint_method = fingerprintMethod,
        };
        var body = JsonSerializer.Serialize(payload);
        var resp = await _http.PostAsync("/api/jobs",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<JobCreateResponse>(json, _jso);
        return result?.JobId ?? throw new Exception("No job_id returned");
    }

    public async IAsyncEnumerable<JobStatus> StreamJobAsync(
        string jobId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/jobs/{jobId}/stream");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;

            var json = line[6..];
            var status = JsonSerializer.Deserialize<JobStatus>(json, _jso);
            if (status is not null) yield return status;
            if (status?.Status is "completed" or "error") break;
        }
    }

    public void Dispose() => _http.Dispose();
}
