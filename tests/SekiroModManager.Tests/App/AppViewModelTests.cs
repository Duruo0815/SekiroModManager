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
        return IOPath.Combine(dir!.FullName, "src", "SekiroModManager.App", fileName);
    }
}
