using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Bleak;
using Libjector.Core;
using Libjector.Models;
using Libjector.Views;
using Lunar;
using Microsoft.Win32;
using MessageBox = AdonisUI.Controls.MessageBox;
using MessageBoxButton = AdonisUI.Controls.MessageBoxButton;
using MessageBoxResult = AdonisUI.Controls.MessageBoxResult;

namespace Libjector;

public partial class MainWindow
{
    private int? _targetProcessId;
    private Injector? _injectorService; // api from Bleak
    private LibraryMapper? _libraryMapper; // api from Lunar
    private BackgroundWorker? _processHandler;

    private MainViewModel Context => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void ToggleInjectionMode(bool state)
    {
        InjectButton.Content = state ? "Inject" : "Eject";
        Context.InjectionMode = state;
    }

    private void OnInitialized(object sender, EventArgs args)
    {
        foreach (var libraryPath in App.Settings.DllPaths)
            if (File.Exists(libraryPath))
                Context.DllList.Add(new DllItemModel(Path.GetFileName(libraryPath), Utilities.GetDllArchitecture(libraryPath), libraryPath));
        Context.SelectedMethodIndex = App.Settings.MethodIndex;
        Context.HideDllFlag = App.Settings.IsHideDllFlagChecked;
        Context.RandomizeHeadersFlag = App.Settings.IsRandomizeHeadersFlagChecked;
        Context.RandomizeNameFlag = App.Settings.IsRandomizeNameFlagChecked;
        Context.DiscardHeadersFlag = App.Settings.IsDiscardHeadersChecked;
        // Context.SkipInitializationRoutinesFlag = App.Settings.IsSkipInitializationRoutinesChecked;
    }

    private void OnSelectProcess(object sender, RoutedEventArgs args)
    {
        var dialog = new SelectProcessWindow { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        _targetProcessId = dialog.Result.Key;
        ProcessInput.Text = $"{dialog.Result.Value} ({_targetProcessId})";
    }

    private void OnFileDrop(object sender, DragEventArgs args)
    {
        if (!args.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        var filePaths = (string[])args.Data.GetData(DataFormats.FileDrop);
        foreach (var filePath in filePaths)
        {
            if (!Path.GetExtension(filePath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            var item = new DllItemModel(Path.GetFileName(filePath), Utilities.GetDllArchitecture(filePath), filePath);
            if (!Context.DllList.Contains(item))
                Context.DllList.Add(item);
        }
    }

    private void OnAddDlls(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog { Filter = "Dynamic Link Library (*.dll)|*.dll", Multiselect = true };
        if (dialog.ShowDialog() != true)
            return;
        foreach (var dllPath in dialog.FileNames)
        {
            var item = new DllItemModel(Path.GetFileName(dllPath), Utilities.GetDllArchitecture(dllPath), dllPath);
            if (!Context.DllList.Contains(item))
                Context.DllList.Add(item);
        }
    }

    private void OnRemoveDll(object sender, RoutedEventArgs args)
    {
        if (DllList.SelectedItem is not DllItemModel item)
            return;
        if (MessageBox.Show("Are you sure you want to remove this library?", "Libjector", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            Context.DllList.Remove(item);
    }

    private void OnRemoveAllDlls(object sender, RoutedEventArgs args)
    {
        if (!(DllList.Items.Count > 0))
            return;
        if (MessageBox.Show("Are you sure you want to remove all libraries?", "Libjector", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            Context.DllList.Clear();
    }

    private void OnOpenDll(object sender, MouseButtonEventArgs args)
    {
        if (DllList.SelectedItem is DllItemModel item)
            Utilities.ShowFileInExplorer(item.Path);
    }

    private void OnInject(object sender, RoutedEventArgs args)
    {
        if (Context.InjectionMode)
        {
            if (!Utilities.IsRunningAsAdministrator()) // checks whether the app is running as administrator
            {
                MessageBox.Show("Administrative privileges is required in order to inject a DLL into a process!", "Libjector");
                return;
            }
            if (_targetProcessId is null) // checks whether the target process is selected yet
            {
                MessageBox.Show("Select a target process before continuing!", "Libjector");
                return;
            }
            if (DllList.SelectedItem is not DllItemModel dllItem) // checks whether the dll is selected yet
            {
                MessageBox.Show("Select a DLL before continuing!", "Libjector");
                return;
            }
            try
            {
                if (Context.SelectedMethodIndex == 3)
                {
                    var mappingFlags = MappingFlags.None;
                    if (Context.DiscardHeadersFlag)
                        mappingFlags |= MappingFlags.DiscardHeaders;
                    /*
                    if (Context.SkipInitializationRoutinesFlag)
                        mappingFlags |= MappingFlags.SkipInitialisationRoutines;
                    */
                    _libraryMapper = new LibraryMapper(Process.GetProcessById(_targetProcessId.Value), dllItem.Path, mappingFlags);
                    _libraryMapper.MapLibrary();
                }
                else
                {
                    var injectionFlags = InjectionFlags.None;
                    if (Context.HideDllFlag) // adds hide from peb flag
                        injectionFlags |= InjectionFlags.HideDllFromPeb;
                    if (Context.RandomizeHeadersFlag) // adds randomize headers flag
                        injectionFlags |= InjectionFlags.RandomiseDllHeaders;
                    if (Context.RandomizeNameFlag) // adds randomize name flag
                        injectionFlags |= InjectionFlags.RandomiseDllName;
                    var injectionMethod = Context.SelectedMethodIndex switch
                    {
                        1 => InjectionMethod.HijackThread,
                        2 => InjectionMethod.ManualMap,
                        _ => InjectionMethod.CreateThread
                    };
                    _injectorService?.Dispose(); // disposes any existing injector service
                    _injectorService = new Injector(_targetProcessId.Value, dllItem.Path, injectionMethod, injectionFlags);
                    _injectorService.InjectDll(); // injects dll into the target process
                    if (injectionFlags.HasFlag(InjectionFlags.HideDllFromPeb))
                    {
                        _injectorService.Dispose(); // disposes the injector service; if the specific flag is used
                        goto InjectionCompleted;
                    }
                }
                _processHandler?.Dispose(); // disposes any existing process handler
                _processHandler = new BackgroundWorker { WorkerSupportsCancellation = true };
                _processHandler.DoWork += delegate
                {
                    try
                    {
                        using var process = Process.GetProcessById(_targetProcessId.Value);
                        process.WaitForExit(); // waits for the target process to exit
                    }
                    catch
                    {
                        // do nothing
                    }
                };
                _processHandler.RunWorkerCompleted += delegate
                {
                    // disposes any existing services; as the target process has been closed
                    _injectorService?.Dispose();
                    _libraryMapper?.UnmapLibrary();
                    ToggleInjectionMode(true);
                };
                _processHandler.RunWorkerAsync(); // runs until the dll is ejected or the target process is closed
                ToggleInjectionMode(false);
                InjectionCompleted:
                MessageBox.Show("The DLL has been injected into the process!", "Libjector");
            }
            catch (Exception exception)
            {
                MessageBox.Show("An error occurred while injecting! " + exception.Message, "Libjector");
            }
        }
        else
        {
            try
            {
                _injectorService?.EjectDll();
                _injectorService?.Dispose();
                _libraryMapper?.UnmapLibrary();
            }
            catch (Exception exception)
            {
                MessageBox.Show("An error occurred while ejecting! " + exception.Message, "Libjector");
            }
            if (_processHandler?.IsBusy == true)
                _processHandler?.CancelAsync(); // cancels the process handler; as the dll has been ejected
            _processHandler?.Dispose();
            ToggleInjectionMode(true);
            MessageBox.Show("The DLL has been ejected from the process!", "Libjector");
        }
    }

    private void OnClosing(object sender, CancelEventArgs args)
    {
        App.Settings.MethodIndex = Context.SelectedMethodIndex;
        App.Settings.IsHideDllFlagChecked = Context.HideDllFlag;
        App.Settings.IsRandomizeHeadersFlagChecked = Context.RandomizeHeadersFlag;
        App.Settings.IsRandomizeNameFlagChecked = Context.RandomizeNameFlag;
        App.Settings.IsDiscardHeadersChecked = Context.DiscardHeadersFlag;
        // App.Settings.IsSkipInitializationRoutinesChecked = Context.SkipInitializationRoutinesFlag;
        App.Settings.DllPaths = Context.DllList.Select(libraryItem => libraryItem.Path).ToArray();
    }
}