using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace SteamWorkshopSynchronizer;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText = ResolveVersion();
        BuildTimeText = ResolveBuildTime();
        DataContext = this;
    }

    public string VersionText { get; }

    public string BuildTimeText { get; }

    private static string ResolveVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null ? "未知" : version.ToString(3);
    }

    private static string ResolveBuildTime()
    {
        var value = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "BuildTimeUtc")
            ?.Value;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture)
            : "未知";
    }

    private void ProjectLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show($"无法打开项目地址：{exception.Message}", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
