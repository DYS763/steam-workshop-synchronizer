using SteamWorkshopSynchronizer.Core.Models;
using System.Text;
using System.Windows;

namespace SteamWorkshopSynchronizer;

public partial class PresetEditorWindow : Window
{
    private readonly GameInfo game;
    private readonly PresetSummary preset;
    private readonly PresetVersionSnapshot version;
    private readonly Application.AppServices services;

    public PresetEditorWindow(GameInfo game, PresetSummary preset)
    {
        InitializeComponent();
        this.game = game;
        this.preset = preset;
        services = App.Services;
        version = services.Presets.GetCurrentVersion(preset.Id) ?? throw new InvalidOperationException("当前预设没有版本");
        NameBox.Text = preset.Name;
        NoteBox.Text = preset.Note ?? string.Empty;
        ItemsBox.Text = string.Join(Environment.NewLine, version.Items.Select(item => item.PublishedFileId));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("预设名称不能为空。", "编辑预设", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var existing = version.Items.ToDictionary(item => item.PublishedFileId, StringComparer.Ordinal);
        var issues = new List<string>();
        var items = new List<PresetItemRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = ItemsBox.Text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            var input = lines[index].Trim();
            var id = ExtractWorkshopId(input);
            if (id is null)
            {
                issues.Add($"第 {index + 1} 行不是有效的 Workshop ID 或 URL：{input}");
                continue;
            }

            if (!seen.Add(id))
            {
                issues.Add($"第 {index + 1} 行重复，已跳过：{id}");
                continue;
            }

            if (existing.TryGetValue(id, out var oldItem))
            {
                items.Add(oldItem);
            }
            else
            {
                items.Add(new(game.AppId, id, null, $"https://steamcommunity.com/sharedfiles/filedetails/?id={id}", false, "manual"));
            }
        }

        if (issues.Count > 0)
        {
            var preview = string.Join(Environment.NewLine, issues.Take(12));
            if (MessageBox.Show($"发现 {issues.Count} 条输入提示：\n\n{preview}\n\n仍然保存有效项吗？", "编辑预设", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        services.Presets.UpdateMetadata(preset.Id, name, string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text);
        services.Presets.CreateVersion(preset.Id, game.AppId, items, "edit");
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private static string? ExtractWorkshopId(string input)
    {
        if (input.Length is >= 1 and <= 20 && input.All(char.IsDigit))
        {
            return input;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("steamcommunity.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        var pair = query.FirstOrDefault(part => part.StartsWith("id=", StringComparison.OrdinalIgnoreCase));
        var value = pair?[(pair.IndexOf('=') + 1)..];
        return value is { Length: >= 1 and <= 20 } && value.All(char.IsDigit) ? value : null;
    }
}
