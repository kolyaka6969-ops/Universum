using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Universum.Core;

public sealed record ReleaseAsset(string Name, long Size, string Url);

public sealed record ReleaseInfo(string Tag, string Version, DateTimeOffset PublishedAt, string HtmlUrl, string Notes, IReadOnlyList<ReleaseAsset> Assets);

public sealed class GitHubService
{
    static readonly HttpClient Http = CreateClient();

    readonly string _cachePath = Path.Combine(AppPaths.Cache, "releases.json");
    readonly object _lock = new();
    readonly Dictionary<string, CacheEntry> _cache;

    sealed class CacheEntry
    {
        public string? ETag { get; set; }
        public string Json { get; set; } = "";
    }

    public GitHubService()
    {
        try
        {
            _cache = File.Exists(_cachePath)
                ? JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(_cachePath)) ?? []
                : [];
        }
        catch
        {
            _cache = [];
        }
    }

    static HttpClient CreateClient()
    {
        // Без общего таймаута: установщики весят десятки мегабайт. Таймауты API задаются токеном.
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Universum-Hub/1.0");
        return http;
    }

    /// <summary>
    /// Последний релиз репозитория. Запросы с ETag: ответ 304 не тратит лимит GitHub API.
    /// Без сети возвращает последний сохранённый ответ (FromCache = true).
    /// </summary>
    public async Task<(ReleaseInfo? Release, bool FromCache)> GetLatestAsync(string repo, CancellationToken ct)
    {
        CacheEntry? entry;
        lock (_lock) _cache.TryGetValue(repo, out entry);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            if (entry?.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var tag))
                req.Headers.IfNoneMatch.Add(tag);

            using var resp = await Http.SendAsync(req, timeout.Token);
            if (resp.StatusCode == HttpStatusCode.NotModified && entry != null)
                return (Parse(entry.Json), false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return (null, false);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(timeout.Token);
            lock (_lock)
            {
                _cache[repo] = new CacheEntry { ETag = resp.Headers.ETag?.ToString(), Json = json };
                SaveCache();
            }
            return (Parse(json), false);
        }
        catch (Exception) when (entry != null && !ct.IsCancellationRequested)
        {
            return (Parse(entry.Json), true);
        }
    }

    void SaveCache()
    {
        try { File.WriteAllText(_cachePath, JsonSerializer.Serialize(_cache)); }
        catch { /* кэш необязателен */ }
    }

    static ReleaseInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(a => new ReleaseAsset(
                a.GetProperty("name").GetString() ?? "",
                a.GetProperty("size").GetInt64(),
                a.GetProperty("browser_download_url").GetString() ?? ""))
            .ToList();
        return new ReleaseInfo(
            tag,
            tag.TrimStart('v', 'V'),
            root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTimeOffset() : DateTimeOffset.MinValue,
            root.GetProperty("html_url").GetString() ?? "",
            root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
            assets);
    }

    public static async Task DownloadAsync(string url, string path, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        var part = path + ".part";
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;

            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(part))
            {
                var buffer = new byte[81920];
                long done = 0, reported = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (done - reported >= 256 * 1024 || done == total)
                    {
                        reported = done;
                        progress.Report((done, total));
                    }
                }
            }
            File.Move(part, path, overwrite: true);
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    /// <summary>Сверяет файл с записью в SHA256SUMS.txt. Если записи для файла нет — проверка пропускается.</summary>
    public static async Task VerifySha256Async(string sumsUrl, string file, string assetName, CancellationToken ct)
    {
        var text = await Http.GetStringAsync(sumsUrl, ct);
        var line = text.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.EndsWith(assetName, StringComparison.OrdinalIgnoreCase));
        if (line == null) return;

        var expected = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
        string actual;
        await using (var fs = File.OpenRead(file))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(file);
            throw new InvalidDataException("Контрольная сумма не совпала — файл повреждён при загрузке. Попробуйте ещё раз.");
        }
    }

    public static async Task<byte[]?> TryGetBytesAsync(string url, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            return await Http.GetByteArrayAsync(url, timeout.Token);
        }
        catch
        {
            return null;
        }
    }

    public static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
