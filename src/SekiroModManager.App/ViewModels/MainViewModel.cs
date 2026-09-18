using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using SekiroModManager.App.Common;
using SekiroModManager.App.Theme;
using SekiroModManager.App.Views;
using SekiroModManager.Core;
using SekiroModManager.Core.Models;
using SekiroModManager.Core.Services;

namespace SekiroModManager.App.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly ModManager _manager;
    private string _searchText = "";
    private ModItemViewModel? _selectedMod;
    private string _statusMessage = "就绪";
    private string _statusSeverity = "Info";
    private bool _isBusy;
    private string _busyText = "";
    private bool _isDarkMode = true;
    private ICollectionView _filteredModsView;

    public Func<string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult> MessageBoxAction { get; set; } =
        (msg, title, btn, img) => MessageBox.Show(msg, title, btn, img);

    public Action<DeployPlan, Action>? ShowPlanWindowAction { get; set; }

    public MainViewModel() : this(null)
    {
    }

    public MainViewModel(ModManager? manager)
    {
        _manager = manager ?? new ModManager();
        _isDarkMode = ThemeManager.IsDark;
        ThemeManager.ThemeChanged += theme =>
        {
            IsDarkMode = theme == AppTheme.Dark;
        };

        Mods = new ObservableCollection<ModItemViewModel>();
        _filteredModsView = CollectionViewSource.GetDefaultView(Mods);
        _filteredModsView.Filter = FilterModItem;
        _filteredModsView.SortDescriptions.Add(new SortDescription(nameof(ModItemViewModel.Priority), ListSortDirection.Descending));

        // Commands
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        LocateGameCommand = new RelayCommand(LocateGame);
        BrowseGameCommand = new RelayCommand(BrowseGame);
        LaunchGameCommand = new RelayCommand(LaunchGame);
        ImportFilesCommand = new RelayCommand(ImportFiles);
        ImportFolderCommand = new RelayCommand(ImportFolder);
        PlanCommand = new RelayCommand(ShowPlanWindow);
        DeployCommand = new RelayCommand(DeployMods);
        CleanCommand = new RelayCommand(CleanMods);
        OpenGameFolderCommand = new RelayCommand(OpenGameFolder);
        OpenModsFolderCommand = new RelayCommand(OpenModsFolder);
        OpenStorageFolderCommand = new RelayCommand(OpenStorageFolder);
        RefreshStatusCommand = new RelayCommand(RefreshStatus);

        ReloadMods();
        RefreshStatus();
    }

    public ObservableCollection<ModItemViewModel> Mods { get; }
    public ICollectionView FilteredMods => _filteredModsView;

    public string GamePath => _manager.Config.GamePath;
    public bool IsGamePathValid => GameLocator.IsValidGamePath(_manager.Config.GamePath);

    public bool IsModEngineInstalled { get; private set; }
    public bool ModEngineDinput8 { get; private set; }
    public bool ModEngineIni { get; private set; }
    public string ModEngineStatusDescription { get; private set; } = "";

    public bool IsGameRunning => _manager.IsGameRunning();

    public string StorageRoot => _manager.StorageRoot;
    public string StorageTotalSizeFormatted { get; private set; } = "0 B";
    public string DeployStrategyText { get; private set; } = "检测中...";

    public int TotalModsCount => Mods.Count;
    public int EnabledModsCount => Mods.Count(m => m.Enabled);

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _filteredModsView.Refresh();
            }
        }
    }

    public ModItemViewModel? SelectedMod
    {
        get => _selectedMod;
        set => SetProperty(ref _selectedMod, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string StatusSeverity
    {
        get => _statusSeverity;
        set => SetProperty(ref _statusSeverity, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string BusyText
    {
        get => _busyText;
        set => SetProperty(ref _busyText, value);
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set => SetProperty(ref _isDarkMode, value);
    }

    // Commands
    public ICommand ToggleThemeCommand { get; }
    public ICommand LocateGameCommand { get; }
    public ICommand BrowseGameCommand { get; }
    public ICommand LaunchGameCommand { get; }
    public ICommand ImportFilesCommand { get; }
    public ICommand ImportFolderCommand { get; }
    public ICommand PlanCommand { get; }
    public ICommand DeployCommand { get; }
    public ICommand CleanCommand { get; }
    public ICommand OpenGameFolderCommand { get; }
    public ICommand OpenModsFolderCommand { get; }
    public ICommand OpenStorageFolderCommand { get; }
    public ICommand RefreshStatusCommand { get; }

    public void ToggleTheme()
    {
        ThemeManager.ToggleTheme();
    }

    private bool FilterModItem(object item)
    {
        if (item is not ModItemViewModel mod)
            return false;

        if (string.IsNullOrWhiteSpace(_searchText))
            return true;

        var term = _searchText.Trim();
        return mod.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || mod.Id.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (mod.SourceFileName != null && mod.SourceFileName.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public void ReloadMods()
    {
        Mods.Clear();
        // Display ordered by priority descending (highest first)
        foreach (var mod in _manager.Config.Mods.OrderByDescending(m => m.Priority))
        {
            Mods.Add(new ModItemViewModel(mod, _manager.StorageRoot, OnModDataChanged, DeleteMod));
        }

        UpdateCounts();
        CalculateStorageSize();
    }

    private void OnModDataChanged()
    {
        _manager.SaveConfig();
        UpdateCounts();
        _filteredModsView.Refresh();
    }

    public void UpdateCounts()
    {
        OnPropertyChanged(nameof(TotalModsCount));
        OnPropertyChanged(nameof(EnabledModsCount));
    }

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(IsGamePathValid));
        OnPropertyChanged(nameof(IsGameRunning));
        OnPropertyChanged(nameof(StorageRoot));

        if (IsGamePathValid)
        {
            var me = _manager.CheckModEngine();
            IsModEngineInstalled = me.Installed;
            ModEngineDinput8 = me.Dinput8Present;
            ModEngineIni = me.IniPresent;

            if (me.Installed)
            {
                ModEngineStatusDescription = "Mod Engine 正常";
            }
            else
            {
                var missing = new List<string>();
                if (!me.Dinput8Present) missing.Add("缺少 dinput8.dll");
                if (!me.IniPresent) missing.Add("缺少 modengine.ini");
                ModEngineStatusDescription = string.Join(", ", missing);
            }

            var storageRoot = _manager.StorageRoot;
            var sameVolume = string.Equals(
                Path.GetPathRoot(Path.GetFullPath(storageRoot)),
                Path.GetPathRoot(Path.GetFullPath(GamePath)),
                StringComparison.OrdinalIgnoreCase);

            DeployStrategyText = sameVolume
                ? "硬链接（同卷，免管理员权限，极速）"
                : "符号链接（跨卷，需权限）/ 物理复制";
        }
        else
        {
            IsModEngineInstalled = false;
            ModEngineDinput8 = false;
            ModEngineIni = false;
            ModEngineStatusDescription = "未配置有效游戏路径";
            DeployStrategyText = "请先配置游戏目录";
        }

        OnPropertyChanged(nameof(IsModEngineInstalled));
        OnPropertyChanged(nameof(ModEngineDinput8));
        OnPropertyChanged(nameof(ModEngineIni));
        OnPropertyChanged(nameof(ModEngineStatusDescription));
        OnPropertyChanged(nameof(DeployStrategyText));
        CalculateStorageSize();
    }

    private void CalculateStorageSize()
    {
        try
        {
            var root = _manager.StorageRoot;
            if (Directory.Exists(root))
            {
                var bytes = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
                StorageTotalSizeFormatted = FormatBytes(bytes);
            }
            else
            {
                StorageTotalSizeFormatted = "0 B";
            }
        }
        catch
        {
            StorageTotalSizeFormatted = "未知";
        }
        OnPropertyChanged(nameof(StorageTotalSizeFormatted));
    }

    public void LocateGame()
    {
        var path = _manager.LocateGame();
        if (!string.IsNullOrEmpty(path))
        {
            RefreshStatus();
            SetStatus($"已自动定位游戏路径：{path}", "Success");
        }
        else
        {
            SetStatus("未自动检索到《只狼》，请使用手动浏览选择游戏目录（包含 sekiro.exe 的文件夹）。", "Warning");
        }
    }

    public void BrowseGame()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择《只狼》游戏安装目录（需包含 sekiro.exe）",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            var selectedPath = dialog.FolderName;
            if (_manager.SetGamePath(selectedPath))
            {
                RefreshStatus();
                SetStatus($"成功设置游戏路径：{selectedPath}", "Success");
            }
            else
            {
                SetStatus("选定的目录下未找到 sekiro.exe，设置失败。", "Danger");
                MessageBoxAction("所选目录未包含 sekiro.exe，请确保选择正确的游戏安装目录！", "游戏路径无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    public void LaunchGame()
    {
        if (!IsGamePathValid)
        {
            SetStatus("游戏路径未配置或无效，无法启动！", "Danger");
            MessageBoxAction("请先配置有效的《只狼》游戏目录！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (_manager.LaunchGame())
            {
                SetStatus("正在唤起《只狼：影逝二度》...", "Success");
            }
            else
            {
                SetStatus("启动游戏失败，请检查 sekiro.exe 是否存在。", "Danger");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"启动异常：{ex.Message}", "Danger");
            MessageBoxAction($"启动失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ImportFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 MOD 压缩包",
            Filter = "MOD 压缩包 (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|全部文件 (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            ImportMultiple(dialog.FileNames);
        }
    }

    public void ImportFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含 MOD 内容的文件夹",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            ImportSingle(dialog.FolderName);
        }
    }

    public void ImportPath(string path)
    {
        _ = ImportSingleAsync(path);
    }

    public void ImportMultiple(IEnumerable<string> paths)
    {
        _ = ImportMultipleAsync(paths);
    }

    public async Task ImportMultipleAsync(IEnumerable<string> paths)
    {
        var pathList = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (pathList.Count == 0)
            return;

        try
        {
            IsBusy = true;
            BusyText = $"正在批量导入 {pathList.Count} 个 MOD...";

            var count = 0;
            var errors = new List<string>();

            await Task.Run(() =>
            {
                foreach (var path in pathList)
                {
                    try
                    {
                        _manager.Import(path);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"[{Path.GetFileName(path)}]: {ex.Message}");
                    }
                }
            });

            if (count > 0)
            {
                ReloadMods();
                SetStatus($"成功导入 {count} 个 MOD", "Success");
            }

            if (errors.Count > 0)
            {
                MessageBoxAction(string.Join("\n", errors), "部分 MOD 导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ImportSingle(string path)
    {
        _ = ImportSingleAsync(path);
    }

    public async Task ImportSingleAsync(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;

        try
        {
            IsBusy = true;
            BusyText = $"正在解压与归一化导入 [{Path.GetFileName(path)}]...";

            var result = await Task.Run(() => _manager.Import(path));
            ReloadMods();

            var warnText = result.Warnings.Count > 0 ? " (" + string.Join("; ", result.Warnings) + ")" : "";
            SetStatus($"成功导入 MOD：{result.ModId}{warnText}", result.Warnings.Count > 0 ? "Warning" : "Success");
        }
        catch (Exception ex)
        {
            SetStatus($"导入失败：{ex.Message}", "Danger");
            MessageBoxAction($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void DeleteMod(ModItemViewModel mod)
    {
        var result = MessageBoxAction(
            $"确定要移除 MOD [{mod.Name}] 吗？\n这将清空该 MOD 存储在仓库（storage/{mod.Id}）内的文件。",
            "移除确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _manager.RemoveMod(mod.Id);
            ReloadMods();
            SetStatus($"已移除 MOD：{mod.Name}。已部署的链接将在下次部署时按清单清理。", "Info");
        }
    }

    public void ShowPlanWindow()
    {
        if (!IsGamePathValid)
        {
            SetStatus("请先设置游戏目录再查看部署计划！", "Warning");
            MessageBoxAction("请先设置有效的游戏目录！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var plan = _manager.Plan();
            if (ShowPlanWindowAction != null)
            {
                ShowPlanWindowAction(plan, () => DeployMods());
            }
            else
            {
                var planWin = new PlanWindow(plan, () => DeployMods());
                if (Application.Current?.MainWindow != null)
                {
                    planWin.Owner = Application.Current.MainWindow;
                }
                planWin.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"计划生成失败：{ex.Message}", "Danger");
            MessageBoxAction($"生成计划时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void DeployMods()
    {
        _ = DeployModsAsync();
    }

    public async Task DeployModsAsync()
    {
        if (!IsGamePathValid)
        {
            SetStatus("游戏路径无效，无法部署！", "Danger");
            MessageBoxAction("请先配置有效的游戏目录！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_manager.IsGameRunning())
        {
            SetStatus("《只狼》当前正在运行，部署前请先退出游戏！", "Danger");
            MessageBoxAction("《只狼》进程正在运行，请先关闭游戏再执行部署！", "游戏运行中", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            BusyText = "正在执行清单驱动挂载与部署...";

            var result = await Task.Run(() => _manager.Deploy());
            if (!result.Success)
            {
                SetStatus($"部署失败：{result.Error}", "Danger");
                MessageBoxAction($"部署失败：{result.Error}", "部署错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var counts = string.Join(", ", result.DeployedCounts.Select(kv => $"{kv.Key} {kv.Value}"));
            var msg = $"部署成功！共部署 {result.DeployedCounts.Values.Sum()} 个文件 ({counts})，清理失效条目 {result.RemovedStale} 个，耗时 {result.Duration.TotalMilliseconds:F0} ms。";
            SetStatus(msg, "Success");
            MessageBoxAction(msg, "部署完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus($"部署异常：{ex.Message}", "Danger");
            MessageBoxAction($"部署异常：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void CleanMods()
    {
        _ = CleanModsAsync();
    }

    public async Task CleanModsAsync()
    {
        if (!IsGamePathValid)
        {
            SetStatus("游戏路径无效，无法清理！", "Danger");
            return;
        }

        var confirm = MessageBoxAction(
            "确定要清理游戏 mods/ 目录吗？\n管理器将严格按照部署清单清理托管文件，玩家手动放置的其他外来文件不会被误删。",
            "清理确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            IsBusy = true;
            BusyText = "正在按清单清理托管条目...";

            var result = await Task.Run(() => _manager.Clean());
            var msg = $"清理完成：共移除托管条目 {result.RemovedStale} 个。";
            SetStatus(msg, "Success");
            MessageBoxAction(msg, "清理完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus($"清理失败：{ex.Message}", "Danger");
            MessageBoxAction($"清理失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void OpenGameFolder()
    {
        if (IsGamePathValid && Directory.Exists(GamePath))
            OpenExplorer(GamePath);
    }

    public void OpenModsFolder()
    {
        if (IsGamePathValid)
        {
            var modsDir = DeployEngine.GetModsDir(GamePath);
            if (!Directory.Exists(modsDir))
                Directory.CreateDirectory(modsDir);
            OpenExplorer(modsDir);
        }
    }

    public void OpenStorageFolder()
    {
        if (!Directory.Exists(StorageRoot))
            Directory.CreateDirectory(StorageRoot);
        OpenExplorer(StorageRoot);
    }

    private static void OpenExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    private void SetStatus(string message, string severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }

    private static string FormatBytes(long bytes) => FormatHelper.FormatBytes(bytes);
}
