using Microsoft.Win32;
using SteamWorkshopSynchronizer.Application;
using SteamWorkshopSynchronizer.Core.Models;
using SteamWorkshopSynchronizer.Core.Operations;
using SteamWorkshopSynchronizer.Core.Serialization;
using SteamWorkshopSynchronizer.Infrastructure.Persistence;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SteamWorkshopSynchronizer;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AppServices services;
    private IReadOnlyList<GameInfo> allGames = [];
    private GameInfo? selectedGame;
    private PresetSummary? selectedPreset;
    private bool isLoading;
    private string? lastFailureText;
    private string statusText = "正在准备扫描...";
    private string selectionSummary = "未选择操作";

    public MainWindow()
    {
        InitializeComponent();
        services = App.Services;
        DeveloperModeCheckBox.IsChecked = services.Logger.DeveloperMode;
        DataContext = this;
        Loaded += MainWindow_Loaded;
        services.Logger.Info("界面", "主窗口已创建");
    }

    public ObservableCollection<GameInfo> Games { get; } = [];

    public ObservableCollection<PresetSummary> Presets { get; } = [];

    public ObservableCollection<DiffRow> DiffRows { get; } = [];

    public string StatusText
    {
        get => statusText;
        private set
        {
            if (statusText == value)
            {
                return;
            }

            statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string SelectionSummary
    {
        get => selectionSummary;
        private set
        {
            if (selectionSummary == value)
            {
                return;
            }

            selectionSummary = value;
            OnPropertyChanged(nameof(SelectionSummary));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        services.Logger.Info("界面", "主窗口加载完成，开始首次扫描");
        await RefreshScanAsync();
    }

    private void DeveloperMode_Click(object sender, RoutedEventArgs e)
    {
        var enabled = DeveloperModeCheckBox.IsChecked == true;
        try
        {
            services.Settings.SetBool(AppSettingsStore.DeveloperModeKey, enabled);
            services.Logger.DeveloperMode = enabled;
            services.Logger.Info("设置", $"开发者模式已{(enabled ? "开启" : "关闭")}");
            StatusText = enabled ? "开发者模式已开启，详细日志将写入本地日志文件。" : "开发者模式已关闭，仅保留警告和错误日志。";
        }
        catch (Exception exception)
        {
            services.Logger.Error("设置", "保存开发者模式失败", exception);
            DeveloperModeCheckBox.IsChecked = services.Logger.DeveloperMode;
            MessageBox.Show($"保存开发者模式失败：{exception.Message}", "设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        services.Logger.Info("界面", "打开日志查看窗口");
        new LogViewerWindow(services.Logger) { Owner = this }.ShowDialog();
    }

    private async void RefreshScan_Click(object sender, RoutedEventArgs e) => await RefreshScanAsync();

    private async Task RefreshScanAsync()
    {
        if (isLoading)
        {
            return;
        }

        isLoading = true;
        try
        {
            services.Logger.Info("扫描", "用户触发 Steam 重新扫描");
            var previousAppId = selectedGame?.AppId;
            StatusText = "正在扫描 Steam 库和 Workshop...";
            var snapshot = await services.Discovery.ScanAsync();
            services.Scans.SaveSnapshot(snapshot);
            LoadGames(previousAppId);
            StatusText = snapshot.Warnings.Count == 0
                ? $"扫描完成：发现 {snapshot.Games.Count} 个游戏、{snapshot.WorkshopItems.Count} 个 Workshop 项"
                : $"扫描完成，但有 {snapshot.Warnings.Count} 条提示：{snapshot.Warnings[0]}";
            services.Logger.Info("扫描", StatusText);
        }
        catch (OperationCanceledException)
        {
            StatusText = "扫描已取消";
            services.Logger.Warning("扫描", StatusText);
        }
        catch (Exception exception)
        {
            StatusText = $"扫描失败：{exception.Message}";
            services.Logger.Error("扫描", "主窗口扫描失败", exception);
            MessageBox.Show(StatusText, "扫描失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            isLoading = false;
        }
    }

    private void LoadGames(int? preferredAppId)
    {
        allGames = services.Scans.GetGames();
        ApplyGameFilter(preferredAppId);
    }

    private void GameSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyGameFilter();

    private void OnlyGamesWithMods_Click(object sender, RoutedEventArgs e) => ApplyGameFilter();

    private void ApplyGameFilter(int? preferredAppId = null)
    {
        var query = GameSearchBox?.Text.Trim() ?? string.Empty;
        var onlyGamesWithMods = OnlyGamesWithModsCheckBox?.IsChecked == true;
        var games = allGames
            .Where(game => !onlyGamesWithMods || game.HasMods)
            .Where(game => query.Length == 0
                || game.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || game.AppId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Games.Clear();
        foreach (var game in games)
        {
            Games.Add(game);
        }

        var next = games.FirstOrDefault(game => game.AppId == preferredAppId)
            ?? games.FirstOrDefault(game => game.AppId == selectedGame?.AppId)
            ?? games.FirstOrDefault();
        GamesList.SelectedItem = next;
        if (next is null)
        {
            selectedPreset = null;
            selectedGame = null;
            Presets.Clear();
            DiffRows.Clear();
            SelectionSummary = allGames.Count == 0 ? "未发现已安装游戏" : "没有匹配的游戏";
        }
    }

    private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameInfo game)
        {
            return;
        }

        selectedGame = game;
        LoadPresets(game.AppId);
    }

    private void LoadPresets(int appId, string? preferredPresetId = null)
    {
        var presets = services.Presets.GetPresets(appId);
        Presets.Clear();
        foreach (var preset in presets)
        {
            Presets.Add(preset);
        }

        var next = presets.FirstOrDefault(preset => preset.Id == preferredPresetId) ?? presets.FirstOrDefault();
        PresetsList.SelectedItem = next;
        selectedPreset = next;
        RefreshDiffRows();
    }

    private void PresetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoading || PresetsList.SelectedItem is not PresetSummary preset)
        {
            return;
        }

        selectedPreset = preset;
        RefreshDiffRows();
    }

    private void CreatePreset_Click(object sender, RoutedEventArgs e)
    {
        if (selectedGame is null)
        {
            MessageBox.Show("请先选择游戏。", "新建预设", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new PresetEditorWindow(selectedGame) { Owner = this };
        if (window.ShowDialog() == true)
        {
            LoadPresets(selectedGame.AppId, window.CreatedPresetId);
            StatusText = "预设已创建。";
        }
    }

    private void RefreshDiffRows()
    {
        DiffRows.Clear();
        if (selectedGame is null)
        {
            SelectionSummary = "未选择游戏";
            return;
        }

        var currentItems = services.Scans.GetWorkshopItems(selectedGame.AppId);
        if (selectedPreset is null)
        {
            foreach (var item in currentItems.Where(item => item.IsSubscribed || item.IsLocalResidual || item.DownloadState == WorkshopDownloadState.Downloaded))
            {
                DiffRows.Add(DiffRow.FromCurrent(item));
            }

            UpdateSelectionSummary();
            _ = EnrichMissingDiffTitlesAsync(selectedGame.AppId, DiffRows.ToArray());
            return;
        }

        var version = services.Presets.GetCurrentVersion(selectedPreset.Id);
        if (version is null)
        {
            StatusText = "当前预设没有可用版本";
            return;
        }

        var target = new ManifestDocument(
            ManifestSerializer.Format,
            ManifestSerializer.CurrentFormatVersion,
            new(selectedGame.AppId, selectedGame.Name),
            new(selectedPreset.Name, selectedPreset.Note),
            version.Items.Select(item => new ManifestItem(item.PublishedFileId, item.Title, item.WorkshopUrl, item.IsIgnored, item.Source)).ToArray(),
            version.CreatedAt,
            "preset");
        foreach (var entry in services.Diffs.Calculate(selectedGame.AppId, currentItems, target))
        {
            DiffRows.Add(DiffRow.FromDiff(entry));
        }

        UpdateSelectionSummary();
        _ = EnrichMissingDiffTitlesAsync(selectedGame.AppId, DiffRows.ToArray());
    }

    private async Task EnrichMissingDiffTitlesAsync(int appId, IReadOnlyList<DiffRow> rows)
    {
        var missing = rows
            .Where(row => row.DisplayTitle == GetFallbackTitle(row.PublishedFileId))
            .Select(row => row.PublishedFileId)
            .ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        try
        {
            var titles = await services.Metadata.ResolveTitlesAsync(missing).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (selectedGame?.AppId != appId)
                {
                    return;
                }

                foreach (var row in rows)
                {
                    if (titles.TryGetValue(row.PublishedFileId, out var title))
                    {
                        row.SetDisplayTitle(title);
                    }
                }
            });
        }
        catch (Exception exception)
        {
            services.Logger.Warning("Mod名称", "更新差异列表名称失败", exception);
        }
    }

    private static string GetFallbackTitle(string publishedFileId) => $"Workshop Mod {publishedFileId}";

    private void UpdateSelectionSummary()
    {
        var selected = DiffRows.Count(row => row.IsSelected && row.Action is not null);
        var actionable = DiffRows.Count(row => row.Action is not null);
        SelectionSummary = actionable == 0 ? "没有待执行动作" : $"已选择 {selected}/{actionable} 项";
    }

    private void DiffGrid_CurrentCellChanged(object? sender, EventArgs e) => UpdateSelectionSummary();

    private async void SyncSelected_Click(object sender, RoutedEventArgs e)
    {
        DiffGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        DiffGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var requests = DiffRows
            .Where(row => row.IsSelected && row.Action is not null && selectedGame is not null)
            .Select(row => new WorkshopOperationRequest(selectedGame!.AppId, row.PublishedFileId, row.Action!.Value, row.DisplayTitle, row.WorkshopUrl))
            .ToArray();
        if (requests.Length == 0)
        {
            MessageBox.Show("请先选择要订阅或取消订阅的 Mod。", "没有选中项目", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var subscribeCount = requests.Count(request => request.Action == WorkshopOperationAction.Subscribe);
        var unsubscribeCount = requests.Length - subscribeCount;
        var confirmation = MessageBox.Show(
            $"将提交 {subscribeCount} 个订阅、{unsubscribeCount} 个取消订阅请求。是否继续？\n\nSteam 处理结果需要重新扫描确认。",
            "确认同步",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        StatusText = $"正在提交 {requests.Length} 个 Steam 请求...";
        var progress = new Progress<WorkshopOperationProgress>(value =>
        {
            StatusText = $"已处理 {value.Completed}/{value.Total}：{value.Result.Request.PublishedFileId} {GetOperationStateText(value.Result.State)}";
        });
        try
        {
            services.Logger.Info("同步", $"用户准备同步 {requests.Length} 项，订阅 {subscribeCount} 项，取消订阅 {unsubscribeCount} 项");
            var batchId = services.Operations.CreateBatch(
                "sync",
                requests,
                selectedPreset?.Id,
                selectedPreset?.CurrentVersionId);
            var results = await services.OperationQueue.RunAsync(requests, progress);
            foreach (var result in results)
            {
                services.Operations.RecordResult(batchId, result);
            }
            var failed = results.Count(result => result.State == WorkshopOperationResultState.Failed);
            var submitted = results.Count(result => result.State == WorkshopOperationResultState.Submitted);
            services.Operations.FinishBatch(batchId, failed == results.Count ? "failed" : failed > 0 ? "partial" : "completed");
            StatusText = $"已提交 {submitted} 项，失败 {failed} 项；请等待 Steam 完成后重新扫描。";
            services.Logger.Info("同步", StatusText);
            if (failed > 0)
            {
                lastFailureText = BuildFailureText(results.Where(result => result.State == WorkshopOperationResultState.Failed));
                Clipboard.SetText(lastFailureText);
                MessageBox.Show("失败清单已复制到剪贴板，包含 Mod 名称、Workshop 地址、ID 和失败原因。也可以使用顶部按钮导出为 TXT。", "部分 Steam 请求失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "操作已取消";
            services.Logger.Warning("同步", StatusText);
        }
        catch (Exception exception)
        {
            StatusText = $"同步失败：{exception.Message}";
            services.Logger.Error("同步", "主窗口同步失败", exception);
        }
    }

    private void ToggleIgnore_Click(object sender, RoutedEventArgs e)
    {
        if (selectedGame is null || selectedPreset is null)
        {
            MessageBox.Show("请先选择游戏和预设。", "忽略同步", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedIds = DiffGrid.SelectedItems
            .OfType<DiffRow>()
            .Where(row => row.IsPresetItem)
            .Select(row => row.PublishedFileId)
            .ToHashSet(StringComparer.Ordinal);
        if (selectedIds.Count == 0)
        {
            MessageBox.Show("请在差异列表中选中要切换忽略状态的清单项。", "忽略同步", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var version = services.Presets.GetCurrentVersion(selectedPreset.Id);
        if (version is null)
        {
            return;
        }

        var items = version.Items.Select(item => selectedIds.Contains(item.PublishedFileId)
            ? item with { IsIgnored = !item.IsIgnored }
            : item).ToArray();
        services.Presets.CreateVersion(selectedPreset.Id, selectedGame.AppId, items, "edit");
        LoadPresets(selectedGame.AppId, selectedPreset.Id);
        StatusText = $"已切换 {selectedIds.Count} 个 Mod 的忽略状态，并保存为新版本。";
    }

    private void PauseQueue_Click(object sender, RoutedEventArgs e)
    {
        services.OperationQueue.Pause();
        StatusText = "队列已暂停";
    }

    private void ResumeQueue_Click(object sender, RoutedEventArgs e)
    {
        services.OperationQueue.Resume();
        StatusText = "队列继续提交";
    }

    private void StopQueue_Click(object sender, RoutedEventArgs e)
    {
        services.OperationQueue.Stop();
        StatusText = "已停止提交后续请求，已完成项保持不变";
    }

    private void ImportFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Workshop 清单|*.json;*.txt|所有文件|*.*",
            Title = "导入 Workshop 清单"
        };
        if (dialog.ShowDialog(this) == true)
        {
            ImportText(File.ReadAllText(dialog.FileName), Path.GetFileName(dialog.FileName));
        }
    }

    private void PasteImport_Click(object sender, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsText())
        {
            MessageBox.Show("剪贴板中没有文本。", "无法导入", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ImportText(Clipboard.GetText(), "剪贴板");
    }

    private void ImportText(string text, string sourceName)
    {
        try
        {
            services.Logger.Info("导入", $"开始导入清单，来源：{sourceName}，长度：{text.Length}");
            var json = text.TrimStart().StartsWith(CompressedTextCodec.ManifestPrefix, StringComparison.Ordinal)
                ? services.CompressedText.DecodeManifest(text.Trim())
                : text;
            var parse = ParseManifestOrIdList(json);
            if (parse.Document is null)
            {
                ShowManifestIssues(parse, sourceName);
                return;
            }

            if (parse.IsFutureVersion)
            {
                var result = MessageBox.Show(
                    "该清单版本高于当前工具，强制导入可能丢失未知字段。是否继续？",
                    "版本兼容提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                parse = services.Manifests.Parse(json, forceFutureVersion: true);
                if (parse.Document is null)
                {
                    ShowManifestIssues(parse, sourceName);
                    return;
                }
            }

            services.Scans.EnsureGame(new GameInfo(parse.Document.Game.AppId, parse.Document.Game.Name, null, false));
            var preset = services.Presets.CreateFromManifest(parse.Document);
            EnsureGameExists(parse.Document.Game);
            LoadGames(parse.Document.Game.AppId);
            LoadPresets(parse.Document.Game.AppId, preset.Id);
            StatusText = parse.Issues.Count == 0
                ? $"已导入预设“{preset.Name}”，请选择差异后同步"
                : $"已导入预设“{preset.Name}”，另有 {parse.Issues.Count} 条清单提示";
            services.Logger.Info("导入", StatusText);
            if (parse.Issues.Count > 0)
            {
                ShowManifestIssues(parse, sourceName, showOnlyErrors: false);
            }
        }
        catch (Exception exception)
        {
            services.Logger.Error("导入", $"导入清单失败，来源：{sourceName}", exception);
            MessageBox.Show($"导入失败：{exception.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private ManifestParseResult ParseManifestOrIdList(string text)
    {
        if (text.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return services.Manifests.Parse(text);
        }

        if (selectedGame is null)
        {
            return new(null, [new(ManifestIssueSeverity.Error, "每行 ID 的简易文本没有游戏信息，请先选择目标游戏")], false);
        }

        var items = text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        var issues = new List<ManifestIssue>();
        var validItems = new List<ManifestItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < items.Length; index++)
        {
            var line = items[index];
            if (line.Length is < 1 or > 20 || line.Any(character => !char.IsDigit(character)))
            {
                issues.Add(new(ManifestIssueSeverity.Error, "Workshop ID 必须是 1 到 20 位数字", index));
                continue;
            }

            if (!seen.Add(line))
            {
                issues.Add(new(ManifestIssueSeverity.Warning, $"重复的 Workshop ID 已跳过：{line}", index));
                continue;
            }

            validItems.Add(new ManifestItem(line, null, $"https://steamcommunity.com/sharedfiles/filedetails/?id={line}", false, "text"));
        }

        var manifest = services.Manifests.Create(selectedGame, "剪贴板导入", null, validItems, "import");
        return new(manifest, issues, false);
    }

    private void EnsureGameExists(ManifestGame game)
    {
        if (Games.Any(existing => existing.AppId == game.AppId))
        {
            return;
        }

        Games.Add(new GameInfo(game.AppId, game.Name, null, false));
    }

    private void ShowManifestIssues(ManifestParseResult parse, string sourceName, bool showOnlyErrors = true)
    {
        var issues = parse.Issues.Where(issue => !showOnlyErrors || issue.Severity == ManifestIssueSeverity.Error).ToArray();
        if (issues.Length == 0)
        {
            return;
        }

        var message = string.Join(Environment.NewLine, issues.Take(12).Select(issue =>
            issue.ItemIndex is null ? issue.Message : $"第 {issue.ItemIndex.Value + 1} 项：{issue.Message}"));
        if (issues.Length > 12)
        {
            message += Environment.NewLine + $"还有 {issues.Length - 12} 条提示。";
        }

        MessageBox.Show($"来源：{sourceName}\n\n{message}", "清单校验提示", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e) => ExportManifest(usePreset: selectedPreset is not null, compressed: false, copyToClipboard: false);

    private void ExportCurrent_Click(object sender, RoutedEventArgs e) => ExportManifest(usePreset: false, compressed: false, copyToClipboard: false);

    private void CopyCompressed_Click(object sender, RoutedEventArgs e) => ExportManifest(usePreset: selectedPreset is not null, compressed: true, copyToClipboard: true);

    private void ExportManifest(bool usePreset, bool compressed, bool copyToClipboard)
    {
        services.Logger.Info("导出", $"开始导出清单，预设={usePreset}，压缩={compressed}，复制={copyToClipboard}");
        var manifest = BuildManifest(usePreset);
        if (manifest is null)
        {
            return;
        }

        var json = services.Manifests.Serialize(manifest);
        var output = compressed ? services.CompressedText.EncodeManifest(json) : json;
        if (copyToClipboard)
        {
            Clipboard.SetText(output);
            StatusText = $"已复制压缩清单（{output.Length:N0} 个字符）";
            services.Logger.Info("导出", StatusText);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = compressed ? "压缩 Workshop 清单|*.sws.txt;*.txt|所有文件|*.*" : "Workshop JSON 清单|*.json|所有文件|*.*",
            FileName = SanitizeFileName($"{manifest.Preset.Name}{(compressed ? ".sws.txt" : ".json")}"),
            Title = compressed ? "导出压缩 Workshop 清单" : "导出 Workshop JSON 清单"
        };
        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            StatusText = $"已导出：{dialog.FileName}";
            services.Logger.Info("导出", StatusText);
        }
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            services.Logger.Info("备份", "用户开始导出工具数据备份");
            var dialog = new SaveFileDialog
            {
                Filter = "Steam Workshop Synchronizer 备份|*.swsb.txt;*.txt|所有文件|*.*",
                FileName = $"sws-backup-{DateTime.Now:yyyyMMdd-HHmmss}.swsb.txt",
                Title = "备份工具数据"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var backup = services.Backups.ExportCompressed("0.1.0");
            File.WriteAllText(dialog.FileName, backup, new UTF8Encoding(false));
            StatusText = $"已备份工具数据：{dialog.FileName}";
            services.Logger.Info("备份", StatusText);
            MessageBox.Show(
                "备份包含：游戏绑定和库路径、全部预设及版本、回收站、操作历史和工具设置。\n\n不包含：Steam 登录信息、Workshop 文件本体和临时执行队列。",
                "备份完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            services.Logger.Error("备份", "导出工具数据备份失败", exception);
            MessageBox.Show($"备份失败：{exception.Message}", "备份失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Steam Workshop Synchronizer 备份|*.swsb.txt;*.txt|所有文件|*.*",
            Title = "恢复工具数据"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var modeChoice = MessageBox.Show(
            "选择恢复方式：\n\n是：增量恢复，追加并保留当前数据\n否：覆盖恢复，替换工具管理的数据\n取消：放弃恢复",
            "选择恢复方式",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (modeChoice == MessageBoxResult.Cancel)
        {
            return;
        }

        try
        {
            services.Logger.Info("恢复", $"用户开始恢复工具数据，文件：{dialog.FileName}，模式：{modeChoice}");
            Directory.CreateDirectory(AppDataPaths.RootDirectory);
            var safetyPath = Path.Combine(AppDataPaths.RootDirectory, $"pre-restore-{DateTime.Now:yyyyMMdd-HHmmss}.swsb.txt");
            File.WriteAllText(safetyPath, services.Backups.ExportCompressed("0.1.0"), new UTF8Encoding(false));
            var mode = modeChoice == MessageBoxResult.Yes ? BackupRestoreMode.Incremental : BackupRestoreMode.Overwrite;
            var result = services.Backups.RestoreCompressed(File.ReadAllText(dialog.FileName), mode, safetyPath);
            StatusText = $"恢复完成：导入 {result.ImportedRows} 行，跳过 {result.SkippedRows} 行；请重新扫描 Steam。";
            services.Logger.Info("恢复", StatusText);
            MessageBox.Show(
                $"恢复完成。\n\n导入：{result.ImportedRows} 行\n跳过：{result.SkippedRows} 行\n恢复前快照：{result.SafetySnapshotPath}\n\nSteam 当前订阅没有被改变，请重新扫描后自行选择同步。",
                "恢复完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            LoadGames(selectedGame?.AppId);
        }
        catch (Exception exception)
        {
            services.Logger.Error("恢复", "恢复工具数据失败", exception);
            MessageBox.Show($"恢复失败：{exception.Message}", "恢复失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyFailures_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(lastFailureText))
        {
            MessageBox.Show("当前没有失败的 Steam 请求。", "失败清单", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(lastFailureText);
        StatusText = "失败清单已复制";
    }

    private void ExportFailures_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(lastFailureText))
        {
            MessageBox.Show("当前没有失败的 Steam 请求。", "失败清单", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "失败清单文本|*.txt|所有文件|*.*",
            FileName = $"steam-workshop-failures-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            Title = "导出失败清单"
        };
        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, lastFailureText, new UTF8Encoding(false));
            StatusText = $"已导出失败清单：{dialog.FileName}";
        }
    }

    private static string BuildFailureText(IEnumerable<WorkshopOperationResult> failures)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Steam Workshop Synchronizer 失败清单");
        builder.AppendLine($"生成时间：{DateTimeOffset.Now:O}");
        builder.AppendLine();
        foreach (var failure in failures)
        {
            var request = failure.Request;
            builder.AppendLine($"名称：{request.Title ?? "未获取标题"}");
            builder.AppendLine($"Workshop ID：{request.PublishedFileId}");
            builder.AppendLine($"Workshop 地址：{request.WorkshopUrl ?? $"https://steamcommunity.com/sharedfiles/filedetails/?id={request.PublishedFileId}"}");
            builder.AppendLine($"动作：{(request.Action == WorkshopOperationAction.Subscribe ? "订阅" : "取消订阅")}");
            builder.AppendLine($"失败原因：{failure.ErrorMessage ?? "未知错误"}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private ManifestDocument? BuildManifest(bool usePreset)
    {
        if (selectedGame is null)
        {
            MessageBox.Show("请先选择游戏。", "无法导出", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        if (usePreset && selectedPreset is not null)
        {
            var version = services.Presets.GetCurrentVersion(selectedPreset.Id);
            if (version is null)
            {
                MessageBox.Show("当前预设没有可导出的版本。", "无法导出", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            return new(
                ManifestSerializer.Format,
                ManifestSerializer.CurrentFormatVersion,
                new(selectedGame.AppId, selectedGame.Name),
                new(selectedPreset.Name, selectedPreset.Note),
                version.Items.Select(item => new ManifestItem(item.PublishedFileId, item.Title, item.WorkshopUrl, item.IsIgnored, item.Source)).ToArray(),
                DateTimeOffset.UtcNow,
                "preset");
        }

        var items = services.Scans.GetWorkshopItems(selectedGame.AppId)
            .Where(item => item.IsSubscribed)
            .Select(item => new ManifestItem(item.PublishedFileId, item.Title, item.WorkshopUrl, false, "current-subscriptions"))
            .ToArray();
        return services.Manifests.Create(selectedGame, "当前订阅", null, items, "current-subscriptions");
    }

    private void RecycleBin_Click(object sender, RoutedEventArgs e)
    {
        var items = services.Operations.GetRecycleBin(selectedGame?.AppId);
        var message = items.Count == 0
            ? "回收站为空。"
            : string.Join(Environment.NewLine, items.Select(item => $"{item.Title ?? item.PublishedFileId} [{item.PublishedFileId}]"));
        var result = MessageBox.Show(
            $"{message}\n\n确定重新订阅回收站中的全部项目吗？",
            "回收站",
            items.Count == 0 ? MessageBoxButton.OK : MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (items.Count == 0 || result != MessageBoxResult.Yes)
        {
            return;
        }

        var requests = items.Select(item => new WorkshopOperationRequest(item.AppId, item.PublishedFileId, WorkshopOperationAction.Subscribe, item.Title, item.WorkshopUrl)).ToArray();
        _ = RestoreRecycleBinAsync(items, requests);
    }

    private async Task RestoreRecycleBinAsync(IReadOnlyList<RecycleBinItem> items, IReadOnlyList<WorkshopOperationRequest> requests)
    {
        services.Logger.Info("回收站", $"开始恢复回收站项目，共 {requests.Count} 项");
        var batchId = services.Operations.CreateBatch("restore", requests);
        var results = await services.OperationQueue.RunAsync(requests);
        for (var index = 0; index < results.Count; index++)
        {
            services.Operations.RecordResult(batchId, results[index]);
            if (results[index].State == WorkshopOperationResultState.Submitted)
            {
                services.Operations.MarkRecycleBinRestored(items[index].Id);
            }
            else if (results[index].State == WorkshopOperationResultState.Failed)
            {
                services.Operations.MarkRecycleBinFailed(items[index].Id, results[index].ErrorMessage ?? "Steam 请求失败");
            }
        }

        services.Operations.FinishBatch(batchId, results.Any(result => result.State == WorkshopOperationResultState.Failed) ? "partial" : "completed");
        StatusText = $"已提交 {results.Count(result => result.State == WorkshopOperationResultState.Submitted)} 个回收站恢复请求，请重新扫描确认。";
        services.Logger.Info("回收站", StatusText);
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        var history = services.Operations.GetHistory(30);
        if (history.Count == 0)
        {
            MessageBox.Show("暂无操作历史。", "操作历史", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new OperationHistoryWindow(history) { Owner = this };
        if (window.ShowDialog() == true && window.SelectedBatch is not null)
        {
            var requests = services.Operations.GetUndoRequests(window.SelectedBatch.Id);
            if (requests.Count == 0)
            {
                MessageBox.Show("该批次没有已成功提交的项目，无法撤回。", "撤回", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _ = UndoBatchAsync(window.SelectedBatch.Id, requests);
        }
    }

    private void PresetVersions_Click(object sender, RoutedEventArgs e)
    {
        if (selectedGame is null || selectedPreset is null)
        {
            MessageBox.Show("请先选择游戏和预设。", "预设版本", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new PresetVersionsWindow(selectedGame, selectedPreset) { Owner = this };
        if (window.ShowDialog() == true)
        {
            LoadPresets(selectedGame.AppId, selectedPreset.Id);
        }
    }

    private void EditPreset_Click(object sender, RoutedEventArgs e)
    {
        if (selectedGame is null || selectedPreset is null)
        {
            MessageBox.Show("请先选择游戏和预设。", "编辑预设", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new PresetEditorWindow(selectedGame, selectedPreset) { Owner = this };
        if (window.ShowDialog() == true)
        {
            LoadPresets(selectedGame.AppId, selectedPreset.Id);
            StatusText = "预设已保存为新版本。";
        }
    }

    private void UndoLatest_Click(object sender, RoutedEventArgs e)
    {
        var latest = services.Operations.GetHistory(1).FirstOrDefault();
        if (latest is null)
        {
            MessageBox.Show("暂无可撤回的操作批次。", "撤回", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var requests = services.Operations.GetUndoRequests(latest.Id);
        if (requests.Count == 0)
        {
            MessageBox.Show("最近批次没有已成功提交的项目，无法撤回。", "撤回", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmation = MessageBox.Show(
            $"最近批次包含 {requests.Count} 个已提交项目。撤回会创建新的反向操作批次。\n\n请先重新扫描确认当前 Steam 状态，再继续。是否提交？",
            "撤回最近批次",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _ = UndoBatchAsync(latest.Id, requests);
    }

    private async Task UndoBatchAsync(string parentBatchId, IReadOnlyList<WorkshopOperationRequest> requests)
    {
        try
        {
            services.Logger.Info("撤回", $"开始撤回批次 {parentBatchId}，共 {requests.Count} 项");
            var batchId = services.Operations.CreateBatch("undo", requests, parentBatchId: parentBatchId);
            var results = await services.OperationQueue.RunAsync(requests);
            foreach (var result in results)
            {
                services.Operations.RecordResult(batchId, result);
            }

            var failed = results.Count(result => result.State == WorkshopOperationResultState.Failed);
            services.Operations.FinishBatch(batchId, failed == results.Count ? "failed" : failed > 0 ? "partial" : "completed");
            StatusText = $"已提交 {results.Count(result => result.State == WorkshopOperationResultState.Submitted)} 个撤回请求，请重新扫描确认。";
            services.Logger.Info("撤回", StatusText);
        }
        catch (Exception exception)
        {
            services.Logger.Error("撤回", "撤回操作失败", exception);
            MessageBox.Show($"撤回失败：{exception.Message}", "撤回失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return fileName;
    }

    private static string GetOperationStateText(WorkshopOperationResultState state) => state switch
    {
        WorkshopOperationResultState.Submitted => "已提交",
        WorkshopOperationResultState.Failed => "失败",
        WorkshopOperationResultState.Stopped => "已停止",
        _ => "已跳过"
    };

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new(propertyName));

    public sealed class DiffRow : INotifyPropertyChanged
    {
        private bool isSelected;
        private string displayTitle = string.Empty;

        private DiffRow(
            string publishedFileId,
            string displayTitle,
            string? workshopUrl,
            string statusText,
            WorkshopOperationAction? action,
            bool isSelected,
            bool isPresetItem,
            bool isIgnored)
        {
            PublishedFileId = publishedFileId;
            this.displayTitle = displayTitle;
            WorkshopUrl = workshopUrl;
            StatusText = statusText;
            Action = action;
            this.isSelected = isSelected;
            IsPresetItem = isPresetItem;
            IsIgnored = isIgnored;
        }

        public string PublishedFileId { get; }
        public string DisplayTitle => displayTitle;
        public string? WorkshopUrl { get; }
        public string StatusText { get; }
        public WorkshopOperationAction? Action { get; }
        public bool IsPresetItem { get; }
        public bool IsIgnored { get; }
        public string ActionText => Action switch
        {
            WorkshopOperationAction.Subscribe => "订阅",
            WorkshopOperationAction.Unsubscribe => "取消订阅",
            _ => ""
        };

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                {
                    return;
                }

                isSelected = value;
                PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetDisplayTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title) || string.Equals(displayTitle, title, StringComparison.Ordinal))
            {
                return;
            }

            displayTitle = title;
            PropertyChanged?.Invoke(this, new(nameof(DisplayTitle)));
        }

        public static DiffRow FromDiff(Core.Comparison.WorkshopDiffEntry entry) => new(
            entry.PublishedFileId,
            string.IsNullOrWhiteSpace(entry.Title) ? GetFallbackTitle(entry.PublishedFileId) : entry.Title,
            entry.WorkshopUrl,
            GetDiffStatusText(entry.Kind, entry.IsIgnored),
            entry.Kind switch
            {
                Core.Comparison.WorkshopDiffKind.Missing when !entry.IsIgnored => WorkshopOperationAction.Subscribe,
                Core.Comparison.WorkshopDiffKind.Extra => WorkshopOperationAction.Unsubscribe,
                _ => null
            },
            entry.DefaultSelected,
            true,
            entry.IsIgnored);

        public static DiffRow FromCurrent(WorkshopItemSnapshot item) => new(
            item.PublishedFileId,
            string.IsNullOrWhiteSpace(item.Title) ? GetFallbackTitle(item.PublishedFileId) : item.Title,
            item.WorkshopUrl,
            item.IsLocalResidual ? "本地残留" : item.DownloadState switch
            {
                WorkshopDownloadState.Downloaded => "已订阅",
                WorkshopDownloadState.Pending => "订阅中",
                _ => "状态未知"
            },
            null,
            false,
            false,
            false);

        private static string GetDiffStatusText(Core.Comparison.WorkshopDiffKind kind, bool ignored) => kind switch
        {
            Core.Comparison.WorkshopDiffKind.Missing when ignored => "清单缺少（已忽略）",
            Core.Comparison.WorkshopDiffKind.Missing => "清单缺少",
            Core.Comparison.WorkshopDiffKind.Extra => "本地多出",
            Core.Comparison.WorkshopDiffKind.UpToDate => "一致",
            Core.Comparison.WorkshopDiffKind.PendingDownload => "下载中",
            Core.Comparison.WorkshopDiffKind.LocalResidual => "本地残留",
            _ => "状态未知"
        };
    }
}
