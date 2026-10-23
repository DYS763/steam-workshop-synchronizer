using System.Configuration;
using System.Data;
using System.Windows;

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
        }
        catch (Exception exception)
        {
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

