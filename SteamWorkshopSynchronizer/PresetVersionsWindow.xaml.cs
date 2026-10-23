using Microsoft.Win32;
using SteamWorkshopSynchronizer.Core.Models;
using SteamWorkshopSynchronizer.Core.Serialization;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;

namespace SteamWorkshopSynchronizer;

public partial class PresetVersionsWindow : Window
{
    private readonly GameInfo game;
    private readonly PresetSummary preset;
    private readonly Application.AppServices services;

    public PresetVersionsWindow(GameInfo game, PresetSummary preset)
    {
        InitializeComponent();
        this.game = game;
        this.preset = preset;
        services = App.Services;
        Versions = new ObservableCollection<PresetVersionSnapshot>(services.Presets.GetVersions(preset.Id));
        SelectedVersion = Versions.FirstOrDefault(version => version.Id == preset.CurrentVersionId) ?? Versions.FirstOrDefault();
        DataContext = this;
    }

    public ObservableCollection<PresetVersionSnapshot> Versions { get; }

    public PresetVersionSnapshot? SelectedVersion { get; set; }

    private void SetCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedVersion is null)
        {
            MessageBox.Show("请选择一个版本。", "预设版本", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        services.Presets.SetCurrentVersion(preset.Id, SelectedVersion.Id);
        DialogResult = true;
    }

    private void ExportSelected_Click(object sender, RoutedEventArgs e) => ExportSelected(compressed: false, copyToClipboard: false);

    private void CopyCompressed_Click(object sender, RoutedEventArgs e) => ExportSelected(compressed: true, copyToClipboard: true);

    private void ExportSelected(bool compressed, bool copyToClipboard)
    {
        if (SelectedVersion is null)
        {
            MessageBox.Show("请选择一个版本。", "预设版本", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var manifest = new ManifestDocument(
            ManifestSerializer.Format,
            ManifestSerializer.CurrentFormatVersion,
            new(game.AppId, game.Name),
            new($"{preset.Name} v{SelectedVersion.VersionNumber}", preset.Note),
            SelectedVersion.Items.Select(item => new ManifestItem(item.PublishedFileId, item.Title, item.WorkshopUrl, item.IsIgnored, item.Source)).ToArray(),
            SelectedVersion.CreatedAt,
            "preset-version");
        var json = services.Manifests.Serialize(manifest);
        var output = compressed ? services.CompressedText.EncodeManifest(json) : json;
        if (copyToClipboard)
        {
            Clipboard.SetText(output);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Workshop JSON 清单|*.json|所有文件|*.*",
            FileName = $"{preset.Name}-v{SelectedVersion.VersionNumber}.json",
            Title = "导出预设版本"
        };
        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, output, new UTF8Encoding(false));
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
