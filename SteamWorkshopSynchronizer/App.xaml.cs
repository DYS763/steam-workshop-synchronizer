using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;
using SteamWorkshopSynchronizer.Infrastructure.Diagnostics;

namespace SteamWorkshopSynchronizer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public static SteamWorkshopSynchronizer.Application.AppServices Services { get; private set; } = null!;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            Services = SteamWorkshopSynchronizer.Application.AppServices.CreateDefault();
            Services.Logger.Info("启动", "应用服务初始化完成");
        }
        catch (Exception exception)
        {
            try
            {
                new AppLogger(SteamWorkshopSynchronizer.Application.AppDataPaths.LogDirectory, developerMode: true)
                    .Error("启动", "应用初始化失败", exception);
            }
            catch
            {
                // Preserve the initialization error dialog if diagnostics are unavailable.
            }
            MessageBox.Show(
                $"工具初始化失败：{exception.Message}",
                "Steam Workshop Synchronizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        base.OnStartup(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            if (Services is not null)
            {
                Services.Logger.Error("未处理异常", "WPF UI 线程发生未处理异常", e.Exception);
            }
        }
        catch
        {
            // Keep the fallback error dialog available even if the logger itself is unavailable.
        }

        MessageBox.Show(
            $"程序遇到未处理错误：{e.Exception.Message}\n\n详细信息已写入日志（如果日志目录可用）。",
            "运行错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}

