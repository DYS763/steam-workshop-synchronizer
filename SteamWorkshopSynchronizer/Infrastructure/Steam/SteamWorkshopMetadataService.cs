using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using SteamWorkshopSynchronizer.Core.Models;
using SteamWorkshopSynchronizer.Infrastructure.Diagnostics;

namespace SteamWorkshopSynchronizer.Infrastructure.Steam;

public sealed class SteamWorkshopMetadataService
{
    private const string DetailsEndpoint = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
    private const int BatchSize = 50;
    private static readonly string[] JsonMetadataNames = ["mod.json", "modinfo.json", "info.json", "workshop.json"];
    private static readonly Regex TextNamePattern = new(
        "^\\s*(?:name|displayName|title|modName|mod_name)\\s*[:=]\\s*(?:\\\"(?<quoted>[^\\\"]+)\\\"|'(?<single>[^']+)'|(?<plain>.+?))\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient httpClient;
    private readonly AppLogger? logger;
    private readonly ConcurrentDictionary<string, string> titleCache = new(StringComparer.Ordinal);

    public SteamWorkshopMetadataService(AppLogger? logger = null, HttpClient? httpClient = null)
    {
        this.logger = logger;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        if (!this.httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SteamWorkshopSynchronizer/0.1");
        }
    }

    public async Task<SteamScanSnapshot> EnrichSnapshotAsync(
        SteamScanSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var items = snapshot.WorkshopItems.ToArray();
        var unresolvedIds = new List<string>();
        var localTitles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.Title))
            {
                titleCache[item.PublishedFileId] = item.Title;
                continue;
            }

            var localTitle = ReadLocalTitle(item.LocalPath);
            if (!string.IsNullOrWhiteSpace(localTitle))
            {
                localTitles[item.PublishedFileId] = localTitle;
                titleCache[item.PublishedFileId] = localTitle;
            }
            else
            {
                unresolvedIds.Add(item.PublishedFileId);
            }
        }

        var remoteTitles = await ResolveTitlesAsync(unresolvedIds, cancellationToken).ConfigureAwait(false);
        var enriched = items.Select(item =>
        {
            if (localTitles.TryGetValue(item.PublishedFileId, out var localTitle))
            {
                return item with { Title = localTitle };
            }

            return remoteTitles.TryGetValue(item.PublishedFileId, out var remoteTitle)
                ? item with { Title = remoteTitle }
                : item;
        }).ToArray();

        return snapshot with { WorkshopItems = enriched };
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveTitlesAsync(
        IEnumerable<string> publishedFileIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publishedFileIds);
        var ids = publishedFileIds
            .Where(IsWorkshopId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        foreach (var id in ids)
        {
            if (titleCache.TryGetValue(id, out var cached))
            {
                result[id] = cached;
            }
            else
            {
                unresolved.Add(id);
            }
        }

        foreach (var batch in unresolved.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var details = await RequestTitlesAsync(batch, cancellationToken).ConfigureAwait(false);
            foreach (var pair in details)
            {
                titleCache[pair.Key] = pair.Value;
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<string, string>> RequestTitlesAsync(
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("itemcount", ids.Count.ToString(CultureInfo.InvariantCulture))
        };
        form.AddRange(ids.Select((id, index) => new KeyValuePair<string, string>($"publishedfileids[{index}]", id)));

        try
        {
            using var response = await httpClient.PostAsync(DetailsEndpoint, new FormUrlEncodedContent(form), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("response", out var responseElement)
                || !responseElement.TryGetProperty("publishedfiledetails", out var details)
                || details.ValueKind != JsonValueKind.Array)
            {
                logger?.Warning("Mod名称", "Steam Workshop 详情响应缺少 publishedfiledetails");
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var titles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var detail in details.EnumerateArray())
            {
                if (!detail.TryGetProperty("result", out var result) || result.GetInt32() != 1
                    || !detail.TryGetProperty("publishedfileid", out var idElement)
                    || !detail.TryGetProperty("title", out var titleElement))
                {
                    continue;
                }

                var id = idElement.GetString();
                var title = CleanTitle(titleElement.GetString());
                if (IsWorkshopId(id) && title is not null)
                {
                    titles[id!] = title;
                }
            }

            return titles;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.Warning("Mod名称", $"请求 Steam Workshop 详情超时，共 {ids.Count} 项");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger?.Warning("Mod名称", $"请求 Steam Workshop 详情失败，共 {ids.Count} 项：{exception.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string? ReadLocalTitle(string? localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
        {
            return null;
        }

        try
        {
            foreach (var name in JsonMetadataNames)
            {
                var path = Path.Combine(localPath, name);
                if (File.Exists(path))
                {
                    var title = ReadJsonTitle(path);
                    if (title is not null)
                    {
                        return title;
                    }
                }
            }

            foreach (var path in Directory.EnumerateFiles(localPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(Path.GetFileName(path), "descriptor.mod", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileName(path), "mod.info", StringComparison.OrdinalIgnoreCase)))
            {
                var title = ReadTextTitle(path);
                if (title is not null)
                {
                    return title;
                }
            }
        }
        catch (Exception)
        {
            // Workshop content can be deleted while Steam is updating it.
        }

        return null;
    }

    private static string? ReadJsonTitle(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (property.Name.Equals("displayName", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("name", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("title", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("modName", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("mod_name", StringComparison.OrdinalIgnoreCase))
                {
                    var title = CleanTitle(property.Value.GetString());
                    if (title is not null)
                    {
                        return title;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // A game may use a file with a familiar name but a non-JSON format.
        }

        return null;
    }

    private static string? ReadTextTitle(string path)
    {
        foreach (var line in File.ReadLines(path).Take(100))
        {
            var match = TextNamePattern.Match(line);
            if (match.Success)
            {
                return CleanTitle(match.Groups["quoted"].Value
                    .IfEmpty(match.Groups["single"].Value)
                    .IfEmpty(match.Groups["plain"].Value));
            }
        }

        return null;
    }

    private static string? CleanTitle(string? title)
    {
        var cleaned = title?.Trim();
        return cleaned is { Length: >= 1 and <= 200 } ? cleaned : null;
    }

    private static bool IsWorkshopId(string? value) => value is { Length: >= 1 and <= 20 } && value.All(char.IsDigit);
}

internal static class StringExtensions
{
    public static string IfEmpty(this string value, string fallback) => value.Length == 0 ? fallback : value;
}
