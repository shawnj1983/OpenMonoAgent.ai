using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenMono.Config;

namespace OpenMono.Captain;

public sealed class CaptainOpenSearchIndexStore
{
    private readonly HttpClient _http;
    private readonly string _indexName;

    public CaptainOpenSearchIndexStore(AppConfig config)
    {
        if (!config.OpenSearch.Enabled)
            throw new InvalidOperationException("OpenSearch is not enabled (missing OpenSearch.Url).");

        _indexName = $"{config.OpenSearch.IndexPrefix}_captain_files_v1";
        _http = new HttpClient
        {
            BaseAddress = new Uri(config.OpenSearch.Url!.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(10),
        };

        if (!string.IsNullOrWhiteSpace(config.OpenSearch.Username) &&
            !string.IsNullOrWhiteSpace(config.OpenSearch.Password))
        {
            var bytes = Encoding.UTF8.GetBytes($"{config.OpenSearch.Username}:{config.OpenSearch.Password}");
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        }
    }

    public void EnsureIndex()
    {
        var head = new HttpRequestMessage(HttpMethod.Head, _indexName);
        var headResp = _http.SendAsync(head).GetAwaiter().GetResult();
        if (headResp.StatusCode == HttpStatusCode.OK)
            return;

        if (headResp.StatusCode is not HttpStatusCode.NotFound)
            throw new InvalidOperationException($"OpenSearch HEAD index failed: {(int)headResp.StatusCode} {headResp.ReasonPhrase}");

        var body = new
        {
            settings = new { index = new { number_of_shards = 1, number_of_replicas = 0 } },
            mappings = new
            {
                properties = new Dictionary<string, object>
                {
                    ["path"] = new { type = "keyword" },
                    ["ext"] = new { type = "keyword" },
                    ["bucket"] = new { type = "keyword" },
                    ["size"] = new { type = "long" },
                    ["mtime_utc"] = new { type = "date" },
                    ["sha256"] = new { type = "keyword" },
                    ["content"] = new { type = "text" },
                }
            }
        };

        var resp = _http.PutAsJson(_indexName, body);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenSearch create index failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
    }

    public void UpsertFile(string path, long size, string mtimeUtc, string? sha256, string? content)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var bucket = InferBucket(ext);
        var id = StableId(path);

        var doc = new
        {
            path,
            ext,
            bucket,
            size,
            mtime_utc = mtimeUtc,
            sha256,
            content = content ?? "",
        };

        var resp = _http.PutAsJson($"{_indexName}/_doc/{id}", doc);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenSearch upsert failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
    }

    public IReadOnlyList<CaptainSearchHit> Search(string query, int limit = 20)
    {
        var body = new
        {
            size = limit,
            query = new
            {
                multi_match = new
                {
                    query,
                    fields = new[] { "path^2", "content" }
                }
            },
            highlight = new
            {
                fields = new Dictionary<string, object>
                {
                    ["content"] = new { }
                }
            }
        };

        var resp = _http.PostAsJson($"{_indexName}/_search", body);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenSearch search failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");

        using var docJson = JsonDocument.Parse(resp.Content.ReadAsString());
        var hits = new List<CaptainSearchHit>();
        var root = docJson.RootElement;
        if (!root.TryGetProperty("hits", out var hitsObj)) return hits;
        if (!hitsObj.TryGetProperty("hits", out var arr) || arr.ValueKind != JsonValueKind.Array) return hits;

        foreach (var hit in arr.EnumerateArray())
        {
            if (!hit.TryGetProperty("_source", out var src)) continue;
            var path = src.TryGetProperty("path", out var p) ? p.GetString() : null;
            if (string.IsNullOrWhiteSpace(path)) continue;
            var sizeVal = src.TryGetProperty("size", out var s) && s.TryGetInt64(out var l) ? l : 0;
            var mtime = src.TryGetProperty("mtime_utc", out var m) ? m.GetString() ?? "" : "";

            string? snippet = null;
            if (hit.TryGetProperty("highlight", out var hl) &&
                hl.TryGetProperty("content", out var c) &&
                c.ValueKind == JsonValueKind.Array)
            {
                snippet = string.Join(" … ", c.EnumerateArray().Select(x => x.GetString() ?? ""));
            }

            hits.Add(new CaptainSearchHit(path!, snippet, sizeVal, mtime));
        }

        return hits;
    }

    private static string StableId(string path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string InferBucket(string ext) => ext switch
    {
        "jpg" or "jpeg" or "png" or "gif" or "webp" or "svg" => "images",
        "pdf" or "doc" or "docx" or "ppt" or "pptx" or "xls" or "xlsx" => "docs",
        "zip" or "rar" or "7z" or "tar" or "gz" => "archives",
        "cs" or "ts" or "tsx" or "js" or "py" or "go" or "rs" or "java" => "code",
        _ => "other",
    };
}

internal static class HttpClientJsonExtensions
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static HttpResponseMessage PutAsJson(this HttpClient http, string path, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return http.Put(path, content);
    }

    public static HttpResponseMessage PostAsJson(this HttpClient http, string path, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return http.Post(path, content);
    }

    public static string ReadAsString(this HttpContent content) =>
        content.ReadAsStringAsync().GetAwaiter().GetResult();

    private static HttpResponseMessage Put(this HttpClient http, string path, HttpContent content) =>
        http.SendAsync(new HttpRequestMessage(HttpMethod.Put, path) { Content = content }).GetAwaiter().GetResult();

    private static HttpResponseMessage Post(this HttpClient http, string path, HttpContent content) =>
        http.SendAsync(new HttpRequestMessage(HttpMethod.Post, path) { Content = content }).GetAwaiter().GetResult();
}

