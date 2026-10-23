using System.Configuration;
using System.Data;
using System.Windows;
using SteamWorkshopSynchronizer.Infrastructure.Diagnostics;

namespace SteamWorkshopSynchronizer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public static SteamWorkshopSynchronizer.Application.AppServices Services { get; private set; } = null!;

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
}

