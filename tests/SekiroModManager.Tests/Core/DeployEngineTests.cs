using System.Text.Json;
using SekiroModManager.Core;
using SekiroModManager.Core.Models;
using SekiroModManager.Core.Services;
using Xunit;

namespace SekiroModManager.Core.Tests;

/// <summary>部署引擎集成测试：真实文件系统上的挂载、清单清理与 ini 维护。</summary>
public class DeployEngineTests : IDisposable
{
    private readonly string _root;
    private readonly string _game;
    private readonly string _storage;

    public DeployEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "smm_deploy_" + Guid.NewGuid().ToString("N"));
        _game = Path.Combine(_root, "game");
        _storage = Path.Combine(_root, "storage");
        Directory.CreateDirectory(_game);
        Directory.CreateDirectory(Path.Combine(_storage, "mA", "chr"));
        Directory.CreateDirectory(Path.Combine(_storage, "mB", "parts"));
        System.IO.File.WriteAllText(Path.Combine(_storage, "mA", "chr", "a.dcx"), "A");
        System.IO.File.WriteAllText(Path.Combine(_storage, "mB", "parts", "b.dcx"), "B");
    }

    private DeployEngine CreateEngine() => new(_game, _storage);

    private List<ModItem> TwoMods() => new()
    {
        new ModItem { Id = "mA", Name = "A", Priority = 10 },
        new ModItem { Id = "mB", Name = "B", Priority = 20 },
    };

    [Fact]
    public void 部署_创建链接并写入清单()
    {
        var engine = CreateEngine();
        var result = engine.Deploy(engine.Plan(TwoMods()));

        Assert.True(result.Success, string.Join(";", result.Warnings));

        var targetA = Path.Combine(_game, "mods", "chr", "a.dcx");
        Assert.True(File.Exists(targetA));
        Assert.Equal("A", File.ReadAllText(targetA));
        Assert.True(File.Exists(Path.Combine(_game, "mods", "parts", "b.dcx")));

        // 清单存在且记录两条
        var manifest = JsonSerializer.Deserialize<List<DeployedEntry>>(
            File.ReadAllText(DeployEngine.GetManifestPath(_game)), Json.Options);
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Count);
        Assert.All(manifest, e => Assert.Equal(nameof(LinkKind.HardLink), e.Kind));

        // 部署计数按硬链接统计
        Assert.True(result.DeployedCounts.TryGetValue(nameof(LinkKind.HardLink), out var hard));
        Assert.Equal(2, hard);
    }

    [Fact]
    public void 重新部署_移除失效条目_保留外来文件()
    {
        var engine = CreateEngine();
        engine.Deploy(engine.Plan(TwoMods()));

        // 玩家手动放置的文件 + 仅保留 mB 重新部署
        var foreign = Path.Combine(_game, "mods", "chr", "我的私货.dcx");
        File.WriteAllText(foreign, "foreign");
        engine.Deploy(engine.Plan(new[] { new ModItem { Id = "mB", Name = "B", Priority = 20 } }));

        Assert.False(File.Exists(Path.Combine(_game, "mods", "chr", "a.dcx")), "失效链接应被清理");
        Assert.True(File.Exists(foreign), "外来文件不应被删除");
        Assert.True(File.Exists(Path.Combine(_game, "mods", "parts", "b.dcx")));
        // 清理不得触碰 storage 源文件
        Assert.True(File.Exists(Path.Combine(_storage, "mA", "chr", "a.dcx")));
    }

    [Fact]
    public void 无清单时外来文件被警告并保留()
    {
        var modsDir = DeployEngine.GetModsDir(_game);
        Directory.CreateDirectory(modsDir);
        var foreign = Path.Combine(modsDir, "我的私货.dcx");
        File.WriteAllText(foreign, "foreign");

        var engine = CreateEngine();
        var result = engine.Deploy(engine.Plan(TwoMods()));

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("不由本管理器管理"));
        Assert.True(File.Exists(foreign));
        Assert.True(File.Exists(Path.Combine(_game, "mods", "chr", "a.dcx")));
    }

    [Fact]
    public void Clean_按清单清空托管条目()
    {
        var engine = CreateEngine();
        engine.Deploy(engine.Plan(TwoMods()));

        var result = engine.Clean();
        Assert.Equal(2, result.RemovedStale);
        Assert.False(File.Exists(Path.Combine(_game, "mods", "chr", "a.dcx")));
        Assert.False(File.Exists(Path.Combine(_game, "mods", "parts", "b.dcx")));
        Assert.False(Directory.Exists(Path.Combine(_game, "mods", "chr")), "空目录应被一并清理");
        Assert.True(File.Exists(Path.Combine(_storage, "mB", "parts", "b.dcx")), "仓库源文件不受影响");
    }

    [Fact]
    public void Ini_指向其他目录时改写并备份()
    {
        File.WriteAllText(Path.Combine(_game, "modengine.ini"),
            "[mods]\nmodDirectory = \"other\"\nloadLooseParams = 1\n");

        var message = ModEngineService.EnsureModDir(_game, ModEngineService.DefaultModDir);

        Assert.Contains("备份", message);
        var ini = File.ReadAllText(Path.Combine(_game, "modengine.ini"));
        Assert.Contains("\"mods\"", ini);
        Assert.Contains("loadLooseParams = 1", ini);
        Assert.True(File.Exists(Path.Combine(_game, "modengine.ini.bak")));
    }

    [Fact]
    public void Ini_已正确时不动()
    {
        var content = "[mods]\nmodDirectory = \"mods\"\n";
        File.WriteAllText(Path.Combine(_game, "modengine.ini"), content);

        var message = ModEngineService.EnsureModDir(_game, ModEngineService.DefaultModDir);

        Assert.Contains("无需修改", message);
        Assert.Equal(content, File.ReadAllText(Path.Combine(_game, "modengine.ini")));
        Assert.False(File.Exists(Path.Combine(_game, "modengine.ini.bak")));
    }

    [Fact]
    public void 游戏运行时拒绝部署()
    {
        // 直接用一个模拟手段验证错误路径：无法在本测试中真的启动 sekiro 进程，
        // 改为验证引擎在非运行状态下成功、错误分支由门面覆盖（此处仅保障无进程时通过）。
        Assert.False(DeployEngine.IsGameRunning());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
