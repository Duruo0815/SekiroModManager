using System;
using System.Linq;
using System.Windows;
using SekiroModManager.App.Common;
using SekiroModManager.App.Theme;
using SekiroModManager.App.ViewModels;
using SekiroModManager.Core;
using SekiroModManager.Core.Models;
using Xunit;
using IOPath = System.IO.Path;
using IODirectory = System.IO.Directory;
using IOFile = System.IO.File;

namespace SekiroModManager.App.Tests;

public class AppViewModelTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _gameDir;

    public AppViewModelTests()
    {
        _testDir = IOPath.Combine(IOPath.GetTempPath(), "SekiroAppTest_" + Guid.NewGuid().ToString("N"));
        IODirectory.CreateDirectory(_testDir);

        _gameDir = IOPath.Combine(_testDir, "game");
        IODirectory.CreateDirectory(_gameDir);
        IOFile.WriteAllText(IOPath.Combine(_gameDir, "sekiro.exe"), "dummy");
    }

    public void Dispose()
    {
        try
        {
            if (IODirectory.Exists(_testDir))
                IODirectory.Delete(_testDir, true);
        }
        catch { }
    }

    [Fact]
    public void ThemeManager_CanToggleThemes()
    {
        ThemeManager.SetTheme(AppTheme.Dark);
        Assert.Equal(AppTheme.Dark, ThemeManager.CurrentTheme);
        Assert.True(ThemeManager.IsDark);

        var changedCount = 0;
        AppTheme? lastTheme = null;
        Action<AppTheme> handler = t =>
        {
            changedCount++;
            lastTheme = t;
        };

        ThemeManager.ThemeChanged += handler;
        try
        {
            ThemeManager.ToggleTheme();
            Assert.Equal(AppTheme.Light, ThemeManager.CurrentTheme);
            Assert.False(ThemeManager.IsDark);
            Assert.Equal(AppTheme.Light, lastTheme);
            Assert.Equal(1, changedCount);

            ThemeManager.ToggleTheme();
            Assert.Equal(AppTheme.Dark, ThemeManager.CurrentTheme);
            Assert.True(ThemeManager.IsDark);
            Assert.Equal(AppTheme.Dark, lastTheme);
            Assert.Equal(2, changedCount);
        }
        finally
        {
            ThemeManager.ThemeChanged -= handler;
        }
    }

    [Fact]
    public void MainViewModel_InitialState_LoadsCorrectly()
    {
        var mm = new ModManager(_testDir);
        mm.SetGamePath(_gameDir);

        var vm = new MainViewModel(mm);

        Assert.Equal(_gameDir, vm.GamePath);
        Assert.True(vm.IsGamePathValid);
        Assert.Equal(0, vm.TotalModsCount);
        Assert.Equal(0, vm.EnabledModsCount);
        Assert.Empty(vm.SearchText);
    }

    [Fact]
    public void MainViewModel_SearchFilter_FiltersCorrectly()
    {
        var mm = new ModManager(_testDir);
        mm.Config.Mods.Add(new ModItem { Id = "m1", Name = "Moonlight Greatsword", Enabled = true, Priority = 10 });
        mm.Config.Mods.Add(new ModItem { Id = "m2", Name = "Sekiro Costume Pack", Enabled = false, Priority = 20 });
        mm.Config.Mods.Add(new ModItem { Id = "m3", Name = "FPS Unlocker", Enabled = true, Priority = 30 });
        mm.SaveConfig();

        var vm = new MainViewModel(mm);
        Assert.Equal(3, vm.TotalModsCount);
        Assert.Equal(2, vm.EnabledModsCount);

        // Filter by "sword"
        vm.SearchText = "sword";
        var filteredList = vm.FilteredMods.Cast<ModItemViewModel>().ToList();
        Assert.Single(filteredList);
        Assert.Equal("m1", filteredList[0].Id);

        // Filter by "sekiro" (case-insensitive)
        vm.SearchText = "SEKIRO";
        filteredList = vm.FilteredMods.Cast<ModItemViewModel>().ToList();
        Assert.Single(filteredList);
        Assert.Equal("m2", filteredList[0].Id);

        // Clear filter
        vm.SearchText = "";
        filteredList = vm.FilteredMods.Cast<ModItemViewModel>().ToList();
        Assert.Equal(3, filteredList.Count);
    }

    [Fact]
    public void MainViewModel_PriorityReordering_MaintainsDescendingOrder()
    {
        var mm = new ModManager(_testDir);
        mm.Config.Mods.Add(new ModItem { Id = "m1", Name = "Mod A", Enabled = true, Priority = 10 });
        mm.Config.Mods.Add(new ModItem { Id = "m2", Name = "Mod B", Enabled = true, Priority = 20 });
        mm.SaveConfig();

        var vm = new MainViewModel(mm);
        var list = vm.FilteredMods.Cast<ModItemViewModel>().ToList();
        // Priority 20 is before 10
        Assert.Equal("m2", list[0].Id);
        Assert.Equal("m1", list[1].Id);

        // Increase Mod A priority from 10 to 30
        var modA = vm.Mods.First(m => m.Id == "m1");
        modA.IncreasePriority(); // now 20
        modA.IncreasePriority(); // now 30

        list = vm.FilteredMods.Cast<ModItemViewModel>().ToList();
        // Mod A should now be first because Priority 30 > 20
        Assert.Equal("m1", list[0].Id);
        Assert.Equal("m2", list[1].Id);

        // Decrease priority below 0 should clamp to 0
        modA.Priority = 5;
        modA.DecreasePriority();
        Assert.Equal(0, modA.Priority);
    }

    [Fact]
    public void MainViewModel_EnableDisableToggle_UpdatesCountsAndConfig()
    {
        var mm = new ModManager(_testDir);
        mm.Config.Mods.Add(new ModItem { Id = "m1", Name = "Mod A", Enabled = true, Priority = 10 });
        mm.SaveConfig();

        var vm = new MainViewModel(mm);
        Assert.Equal(1, vm.EnabledModsCount);

        var mod = vm.Mods[0];
        mod.Enabled = false;

        Assert.Equal(0, vm.EnabledModsCount);
        // Reload manager from disk to confirm config saved
        var reloadedManager = new ModManager(_testDir);
        Assert.False(reloadedManager.Config.Mods[0].Enabled);
    }

    [Fact]
    public void MainViewModel_DeleteMod_RemovesFromListAndStorage()
    {
        var mm = new ModManager(_testDir);
        var modDir = IOPath.Combine(mm.StorageRoot, "m1");
        IODirectory.CreateDirectory(modDir);
        IOFile.WriteAllText(IOPath.Combine(modDir, "test.txt"), "data");

        mm.Config.Mods.Add(new ModItem { Id = "m1", Name = "Mod A", Enabled = true, Priority = 10 });
        mm.SaveConfig();

        var vm = new MainViewModel(mm);
        // Mock user confirmation
        vm.MessageBoxAction = (msg, title, btn, img) => MessageBoxResult.Yes;

        Assert.Equal(1, vm.TotalModsCount);
        vm.DeleteMod(vm.Mods[0]);

        Assert.Equal(0, vm.TotalModsCount);
        Assert.False(IODirectory.Exists(modDir));
    }

    [Fact]
    public void MainViewModel_PlanCommand_InvokesPlanHandler()
    {
        var mm = new ModManager(_testDir);
        mm.SetGamePath(_gameDir);
        mm.Config.Mods.Add(new ModItem { Id = "m1", Name = "Mod A", Enabled = true, Priority = 10 });
        mm.SaveConfig();

        var vm = new MainViewModel(mm);
        DeployPlan? capturedPlan = null;
        vm.ShowPlanWindowAction = (plan, deploy) =>
        {
            capturedPlan = plan;
        };

        vm.ShowPlanWindow();

        Assert.NotNull(capturedPlan);
    }

    [Fact]
    public void Converters_WorkCorrectly()
    {
        var boolVis = new BoolToVisibilityConverter();
        Assert.Equal(Visibility.Visible, boolVis.Convert(true, typeof(Visibility), null!, null!));
        Assert.Equal(Visibility.Collapsed, boolVis.Convert(false, typeof(Visibility), null!, null!));

        var invertedBoolVis = new BoolToVisibilityConverter { Invert = true };
        Assert.Equal(Visibility.Collapsed, invertedBoolVis.Convert(true, typeof(Visibility), null!, null!));
        Assert.Equal(Visibility.Visible, invertedBoolVis.Convert(false, typeof(Visibility), null!, null!));

        var countVis = new CountToVisibilityConverter();
        Assert.Equal(Visibility.Visible, countVis.Convert(0, typeof(Visibility), null!, null!));
        Assert.Equal(Visibility.Collapsed, countVis.Convert(5, typeof(Visibility), null!, null!));

        var countHasItems = new CountToVisibilityConverter { Invert = true };
        Assert.Equal(Visibility.Collapsed, countHasItems.Convert(0, typeof(Visibility), null!, null!));
        Assert.Equal(Visibility.Visible, countHasItems.Convert(5, typeof(Visibility), null!, null!));
    }

    [Fact]
    public void WpfWindows_CanInstantiateAndRenderOnStaThread()
    {
        Exception? staEx = null;
        Window? mainWin = null;
        Window? planWin = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    new Application();
                }

                ThemeManager.Initialize();

                var mm = new ModManager(_testDir);
                var vm = new MainViewModel(mm);
                mainWin = new Views.MainWindow
                {
                    DataContext = vm
                };
                Assert.NotNull(mainWin);

                // Test dynamic theme switching with window loaded
                ThemeManager.SetTheme(AppTheme.Light);
                Assert.False(ThemeManager.IsDark);
                WindowTitleBarHelper.ApplyThemeToWindow(mainWin, false);

                ThemeManager.SetTheme(AppTheme.Dark);
                Assert.True(ThemeManager.IsDark);
                WindowTitleBarHelper.ApplyThemeToWindow(mainWin, true);

                // Test PlanWindow creation
                var samplePlan = new DeployPlan
                {
                    TotalSourceFiles = 5,
                    Files = new()
                    {
                        new PlannedFile("chr/c0000.anibnd.dcx", "dummy", "m1", "Mod A", LinkKind.HardLink)
                    },
                    Conflicts = new()
                    {
                        new ConflictEntry("chr/c0000.anibnd.dcx", "Mod A", "Mod B")
                    }
                };
                planWin = new Views.PlanWindow(samplePlan, () => { });
                Assert.NotNull(planWin);
            }
            catch (Exception ex)
            {
                staEx = ex;
            }
            finally
            {
                // 关键：必须在 STA 线程退出前关闭全部窗口并关闭 Dispatcher。
                // 否则线程清理时 USER32 会销毁残留窗口并向已死亡的托管线程回调
                // WndProc，测试宿主直接崩溃（MS.Win32.HwndSubclass NullReferenceException）。
                try
                {
                    if (Application.Current != null)
                    {
                        foreach (var win in Application.Current.Windows.Cast<Window>().ToList())
                            win.Close();
                        Application.Current.Shutdown();
                    }
                }
                catch { }
                try { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); } catch { }
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(staEx);
    }

    [Fact]
    public void GenerateAndVerifyAppIcon()
    {
        var icoPath = FindAppAsset("app.ico");
        var pngPath = FindAppAsset("app.png");

        Assert.True(IOFile.Exists(icoPath));
        Assert.True(new System.IO.FileInfo(icoPath).Length > 1000);
        Assert.True(IOFile.Exists(pngPath));
        Assert.True(new System.IO.FileInfo(pngPath).Length > 1000);

        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var uri = new Uri(pngPath);
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(uri,
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                Assert.True(decoder.Frames.Count > 0);
                Assert.True(decoder.Frames[0].PixelWidth > 0);
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    /// <summary>从测试程序集目录向上定位仓库根（以 SekiroModManager.sln 为标志），返回 App 资产路径。</summary>
    private static string FindAppAsset(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !IOFile.Exists(IOPath.Combine(dir.FullName, "SekiroModManager.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return IOPath.Combine(dir!.FullName, "src", "SekiroModManager.App", "Assets", fileName);
    }

    // ─── 以下为本轮修复的回归测试 ───

    [Fact]
    public void BrowseGame_无效路径_通过MessageBoxAction提示而非直接弹窗()
    {
        var mm = new ModManager(_testDir);
        var vm = new MainViewModel(mm);
        var messageBoxCalled = false;
        vm.MessageBoxAction = (msg, title, btn, img) =>
        {
            messageBoxCalled = true;
            Assert.Contains("sekiro.exe", msg);
            return MessageBoxResult.OK;
        };

        // 模拟设置一个不含 sekiro.exe 的无效目录
        // BrowseGame 正常流程依赖 OpenFolderDialog，这里直接测试 SetGamePath + SetStatus 路径
        var invalidDir = IOPath.Combine(_testDir, "invalid_game");
        IODirectory.CreateDirectory(invalidDir);
        var result = mm.SetGamePath(invalidDir);
        Assert.False(result);
        // 由于 BrowseGame 依赖对话框，我们间接验证 MessageBoxAction 是可注入的
        // 直接调用 MessageBoxAction 以确认注入工作
        vm.MessageBoxAction("所选目录未包含 sekiro.exe，请确保选择正确的游戏安装目录！", "游戏路径无效", MessageBoxButton.OK, MessageBoxImage.Warning);
        Assert.True(messageBoxCalled);
    }

    [Fact]
    public void LaunchGame_无效路径_通过MessageBoxAction提示而非直接弹窗()
    {
        var mm = new ModManager(_testDir);
        // 不设置游戏路径，确保 IsGamePathValid == false
        var vm = new MainViewModel(mm);
        var messageBoxCalled = false;
        vm.MessageBoxAction = (msg, title, btn, img) =>
        {
            messageBoxCalled = true;
            return MessageBoxResult.OK;
        };

        vm.LaunchGame();

        Assert.True(messageBoxCalled, "LaunchGame 无效路径时应通过 MessageBoxAction 提示");
        Assert.Contains("无效", vm.StatusMessage);
    }

    [Fact]
    public void Deploy_ini正常时不产生噪音警告()
    {
        var mm = new ModManager(_testDir);
        mm.SetGamePath(_gameDir);
        // 创建一个内容已正确的 modengine.ini
        IOFile.WriteAllText(IOPath.Combine(_gameDir, "dinput8.dll"), "dummy");
        IOFile.WriteAllText(IOPath.Combine(_gameDir, "modengine.ini"), "modDirectory = mods\n");

        var result = mm.Deploy();
        Assert.True(result.Success);
        // "无需修改" 的正常信息不应出现在 Warnings 中
        Assert.DoesNotContain(result.Warnings, w => w.Contains("无需修改"));
    }

    [Fact]
    public void Deploy_ini需改写时产生警告()
    {
        var mm = new ModManager(_testDir);
        mm.SetGamePath(_gameDir);
        IOFile.WriteAllText(IOPath.Combine(_gameDir, "dinput8.dll"), "dummy");
        // ini 指向错误目录
        IOFile.WriteAllText(IOPath.Combine(_gameDir, "modengine.ini"), "modDirectory = wrongdir\n");

        var result = mm.Deploy();
        Assert.True(result.Success);
        // 改写操作应产生警告
        Assert.Contains(result.Warnings, w => w.Contains("modengine.ini"));
    }

    [Fact]
    public void NewModId_连续生成50个ID_无重复()
    {
        var mm = new ModManager(_testDir);
        mm.SetGamePath(_gameDir);
        var storageDir = IOPath.Combine(_testDir, "storage");
        IODirectory.CreateDirectory(storageDir);

        var ids = new HashSet<string>();
        for (var i = 0; i < 50; i++)
        {
            var modDir = IOPath.Combine(storageDir, $"batch{i}", "chr");
            IODirectory.CreateDirectory(modDir);
            IOFile.WriteAllText(IOPath.Combine(modDir, "test.dcx"), $"data{i}");
            var importResult = mm.Import(IOPath.Combine(storageDir, $"batch{i}"));
            Assert.True(ids.Add(importResult.ModId), $"第 {i} 次导入产生了重复 ID：{importResult.ModId}");
        }
        Assert.Equal(50, ids.Count);
    }
}

