using SekiroModManager.Core.Models;
using SekiroModManager.Core.Services;
using Xunit;

namespace SekiroModManager.Core.Tests;

public class DeployPlanTests : IDisposable
{
    private readonly string _root;
    private readonly string _storage;
    private readonly string _game;

    public DeployPlanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "smm_plan_" + Guid.NewGuid().ToString("N"));
        _storage = Path.Combine(_root, "storage");
        _game = Path.Combine(_root, "game");
        Directory.CreateDirectory(Path.Combine(_storage, "mA", "chr"));
        Directory.CreateDirectory(Path.Combine(_storage, "mB", "chr"));
        Directory.CreateDirectory(Path.Combine(_storage, "mB", "sfx"));
        System.IO.File.WriteAllText(Path.Combine(_storage, "mA", "chr", "same.dcx"), "A");
        System.IO.File.WriteAllText(Path.Combine(_storage, "mB", "chr", "same.dcx"), "B");
        System.IO.File.WriteAllText(Path.Combine(_storage, "mB", "sfx", "b.dcx"), "B2");
    }

    [Fact]
    public void 高优先级胜出并记录冲突()
    {
        var engine = new DeployEngine(_game, _storage);
        var plan = engine.Plan(new[]
        {
            new ModItem { Id = "mA", Name = "A", Priority = 10 },
            new ModItem { Id = "mB", Name = "B", Priority = 20 },
        });

        // 三个源文件，但 chr/same.dcx 是同一目标路径，唯一部署文件应为 2 个
        Assert.Equal(2, plan.Files.Count);
        Assert.Equal(3, plan.TotalSourceFiles);

        var winner = plan.Files.Single(f => f.RelativePath == Path.Combine("chr", "same.dcx"));
        Assert.Equal("mB", winner.WinnerModId);
        Assert.Equal("B", winner.WinnerModName);

        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal("B", conflict.WinnerModName);
        Assert.Equal("A", conflict.LoserModName);
        Assert.Equal(Path.Combine("chr", "same.dcx"), conflict.RelativePath);
    }

    [Fact]
    public void 仓库目录缺失产生警告()
    {
        var engine = new DeployEngine(_game, _storage);
        var plan = engine.Plan(new[] { new ModItem { Id = "m不存在", Name = "X", Priority = 10 } });
        Assert.Empty(plan.Files);
        Assert.Contains(plan.Warnings, w => w.Contains("m不存在"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
