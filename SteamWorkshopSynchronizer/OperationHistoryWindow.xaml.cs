using SteamWorkshopSynchronizer.Core.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace SteamWorkshopSynchronizer;

public partial class OperationHistoryWindow : Window
{
    public OperationHistoryWindow(IReadOnlyList<OperationBatchSummary> history)
    {
        InitializeComponent();
        History = new ObservableCollection<OperationBatchSummary>(history);
        DataContext = this;
    }

    public ObservableCollection<OperationBatchSummary> History { get; }

    public OperationBatchSummary? SelectedBatch { get; set; }

    private void UndoSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBatch is null)
        {
            MessageBox.Show("请选择一个操作批次。", "撤回", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
