using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Libjector.Core;
using Libjector.Models;

namespace Libjector.Views;

public partial class SelectProcessWindow
{
    public ObservableCollection<ProcessItemModel> Items { get; } = new();

    public KeyValuePair<int, string> Result { get; private set; }

    public SelectProcessWindow()
    {
        InitializeComponent();
        ((CollectionView)CollectionViewSource.GetDefaultView(ProcessList.ItemsSource)).Filter = FilterProcess;
    }

    private bool FilterProcess(object item)
    {
        var filter = FilterInput.Text;
        if (string.IsNullOrEmpty(filter))
            return true; // does not filter item (keeps it)
        if (item is not ProcessItemModel processItem)
            return false; // filter item (hides it)
        return processItem.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) // checks process id
               || processItem.Name.Contains(filter, StringComparison.OrdinalIgnoreCase); // checks process name
    }

    private void OnInitialized(object sender, EventArgs args)
    {
        var processes = Process.GetProcesses();
        foreach (var process in processes)
        {
            if (process.MainWindowHandle == nint.Zero)
                continue; // continues the loop; if the process doesn't have a window or it is a background process
            Items.Add(new ProcessItemModel(process.Id, Path.GetFileName(process.MainModule?.FileName ?? "Unidentified Process"), Utilities.GetProcessArchitecture(process), process.MainModule?.FileName ?? string.Empty));
        }
    }

    private void OnProcessFilter(object sender, TextChangedEventArgs args)
    {
        CollectionViewSource.GetDefaultView(ProcessList.ItemsSource).Refresh();
    }

    private void OnProcessSelect(object sender, RoutedEventArgs args)
    {
        if (ProcessList.SelectedItem is ProcessItemModel item)
            ProcessInput.Text = $"{item.Name} ({item.Id})";
    }

    private void OnProcessSelected(object sender, MouseButtonEventArgs args)
    {
        if (ProcessList.SelectedItem is ProcessItemModel)
            OnContinue(null, null);
    }

    private void OnContinue(object sender, RoutedEventArgs args)
    {
        if (ProcessList.SelectedItem is not ProcessItemModel item)
            return;
        Result = new KeyValuePair<int, string>(item.Id, item.Name);
        DialogResult = true;
        Close();
    }
}