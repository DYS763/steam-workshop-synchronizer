using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SteamWorkshopSynchronizer.Core.Models;

namespace SteamWorkshopSynchronizer.Core.Serialization;

public sealed class ManifestSerializer
{
    public const string Format = "sws-manifest";
    public const int CurrentFormatVersion = 1;
    public const int MaxItems = 5_000;
    public const int MaxJsonCharacters = 10 * 1024 * 1024;

    private static readonly Regex WorkshopIdPattern = new("^[0-9]{1,20}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize(ManifestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public ManifestParseResult Parse(string json, bool forceFutureVersion = false)
    {
        var issues = new List<ManifestIssue>();
        var hasStructuralError = false;
        if (string.IsNullOrWhiteSpace(json))
        {
            return new(null, [new(ManifestIssueSeverity.Error, "清单内容为空")], false);
        }

        if (json.Length > MaxJsonCharacters)
        {
            return new(null, [new(ManifestIssueSeverity.Error, $"清单超过 {MaxJsonCharacters:N0} 个字符的大小限制")], false);
        }

        ManifestDocument? raw;
        try
        {
            raw = JsonSerializer.Deserialize<ManifestDocument>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            return new(null, [new(ManifestIssueSeverity.Error, $"JSON 格式无效：{exception.Message}")], false);
        }

        if (raw is null)
        {
            return new(null, [new(ManifestIssueSeverity.Error, "清单内容无法解析")], false);
        }

        var isFutureVersion = raw.FormatVersion > CurrentFormatVersion;
        if (!string.Equals(raw.Format, Format, StringComparison.Ordinal))
        {
            issues.Add(new(ManifestIssueSeverity.Error, $"不支持的清单格式：{raw.Format ?? "(空)"}"));
            hasStructuralError = true;
        }

        if (raw.FormatVersion <= 0)
        {
            issues.Add(new(ManifestIssueSeverity.Error, "清单格式版本必须为正整数"));
            hasStructuralError = true;
        }
        else if (isFutureVersion)
        {
            var severity = forceFutureVersion ? ManifestIssueSeverity.Warning : ManifestIssueSeverity.Error;
            issues.Add(new(severity, $"清单版本 {raw.FormatVersion} 高于当前支持的版本 {CurrentFormatVersion}"));
        }

        if (raw.Game is null || raw.Game.AppId <= 0)
        {
            issues.Add(new(ManifestIssueSeverity.Error, "清单缺少有效的游戏 AppID"));
            hasStructuralError = true;
        }

        if (raw.Preset is null || string.IsNullOrWhiteSpace(raw.Preset.Name))
        {
            issues.Add(new(ManifestIssueSeverity.Error, "清单缺少预设名称"));
            hasStructuralError = true;
        }

        if (raw.Items is null)
        {
            issues.Add(new(ManifestIssueSeverity.Error, "清单缺少 Mod 列表"));
            hasStructuralError = true;
            return new(null, issues, isFutureVersion);
        }

        if (raw.Items.Count > MaxItems)
        {
            issues.Add(new(ManifestIssueSeverity.Error, $"清单最多允许 {MaxItems:N0} 个 Mod"));
        }

        var validItems = new List<ManifestItem>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < raw.Items.Count && validItems.Count < MaxItems; index++)
        {
            var item = raw.Items[index];
            if (item is null || !WorkshopIdPattern.IsMatch(item.PublishedFileId ?? string.Empty))
            {
                issues.Add(new(ManifestIssueSeverity.Error, "Workshop ID 必须是 1 到 20 位数字", index));
                continue;
            }

            var publishedFileId = item.PublishedFileId!;
            if (!seenIds.Add(publishedFileId))
            {
                issues.Add(new(ManifestIssueSeverity.Warning, $"重复的 Workshop ID 已跳过：{publishedFileId}", index));
                continue;
            }

            if (item.Title is { Length: > 200 })
            {
                issues.Add(new(ManifestIssueSeverity.Warning, "Mod 标题超过 200 个字符，已截断", index));
            }

            if (item.WorkshopUrl is { Length: > 2_000 })
            {
                issues.Add(new(ManifestIssueSeverity.Warning, "Workshop 地址过长，已忽略", index));
            }

            validItems.Add(item with
            {
                Title = item.Title is { Length: > 200 } title ? title[..200] : item.Title,
                WorkshopUrl = item.WorkshopUrl is { Length: > 2_000 } ? null : item.WorkshopUrl
            });
        }

        if (hasStructuralError || (isFutureVersion && !forceFutureVersion && raw.FormatVersion <= 0))
        {
            return new(null, issues, isFutureVersion);
        }

        var preset = raw.Preset!;
        var document = raw with
        {
            Format = Format,
            Items = validItems,
            Preset = preset with
            {
                Name = preset.Name.Trim(),
                Note = string.IsNullOrWhiteSpace(preset.Note) ? null : preset.Note.Trim()
            }
        };
        return new(document, issues, isFutureVersion);
    }

    public ManifestDocument Create(
        GameInfo game,
        string presetName,
        string? note,
        IEnumerable<ManifestItem> items,
        string source = "local")
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(presetName);
        ArgumentNullException.ThrowIfNull(items);

        var deduplicated = items
            .Where(item => WorkshopIdPattern.IsMatch(item.PublishedFileId ?? string.Empty))
            .GroupBy(item => item.PublishedFileId, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(MaxItems)
            .ToArray();

        return new(
            Format,
            CurrentFormatVersion,
            new(game.AppId, game.Name),
            new(presetName.Trim(), string.IsNullOrWhiteSpace(note) ? null : note.Trim()),
            deduplicated,
            DateTimeOffset.UtcNow,
            source);
    }
}
