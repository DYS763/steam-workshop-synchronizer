using System.Diagnostics;
using System.Windows;
using SteamWorkshopSynchronizer.Infrastructure.Diagnostics;

namespace SteamWorkshopSynchronizer;

public partial class LogViewerWindow : Window
{
    private readonly AppLogger logger;

    public LogViewerWindow(AppLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        InitializeComponent();
        PathText.Text = $"当前文件：{logger.CurrentLogPath}";
        Loaded += (_, _) => RefreshLog();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshLog();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(LogTextBox.Text);
        logger.Info("日志", "用户复制了当前日志");
    }

    private void OpenDirectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = logger.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            logger.Error("日志", "打开日志目录失败", exception);
            MessageBox.Show($"打开日志目录失败：{exception.Message}", "日志", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("确定清空当天日志吗？历史日志文件不会删除。", "清空日志", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        logger.ClearCurrentLog();
        logger.Info("日志", "用户清空了当天日志");
        RefreshLog();
    }

    private void RefreshLog()
    {
        LogTextBox.Text = logger.ReadCurrentLog();
        LogTextBox.ScrollToEnd();
    }
}
