using System.Text.Json;
using SekiroModManager.Core;
using SekiroModManager.Core.Models;
using SekiroModManager.Core.Services;
using Xunit;

namespace SekiroModManager.Core.Tests;

/// <summary>针对审查修复的回归测试：隐藏文件部署、悬空链接清理、zip-slip 防御。</summary>
public class HardeningTests : IDisposable
{
    private readonly string _root;
    private readonly string _game;
    private readonly string _storage;

    public HardeningTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "smm_harden_" + Guid.NewGuid().ToString("N"));
        _game = Path.Combine(_root, "game");
        _storage = Path.Combine(_root, "storage");
        Directory.CreateDirectory(_game);
        Directory.CreateDirectory(Path.Combine(_storage, "mA", "chr"));
        System.IO.File.WriteAllText(Path.Combine(_storage, "mA", "chr", "a.dcx"), "A");
    }

    [Fact]
    public void 隐藏文件也参与部署()
    {
        var hidden = Path.Combine(_storage, "mA", "chr", "hidden.dcx");
        System.IO.File.WriteAllText(hidden, "H");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var engine = new DeployEngine(_game, _storage);
        var plan = engine.Plan(new[] { new ModItem { Id = "mA", Name = "A", Priority = 10 } });

        Assert.Contains(plan.Files, f => f.RelativePath == Path.Combine("chr", "hidden.dcx"));
        var result = engine.Deploy(plan);
        Assert.True(result.Success, string.Join(";", result.Warnings));
        Assert.True(File.Exists(Path.Combine(_game, "mods", "chr", "hidden.dcx")));
    }

    [Fact]
    public void 悬空符号链接_按清单清理()
    {
        // 创建符号链接需要开发者模式或管理员权限；无权限环境直接跳过（主路径硬链接不受影响）
        var modsDir = DeployEngine.GetModsDir(_game);
        Directory.CreateDirectory(Path.Combine(modsDir, "chr"));
        var linkPath = Path.Combine(modsDir, "chr", "gone.dcx");
        try
        {
            File.CreateSymbolicLink(linkPath, Path.Combine(_storage, "已删除", "x.dcx"));
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var manifest = new List<DeployedEntry>
        {
            new()
            {
                RelativePath = Path.Combine("chr", "gone.dcx"),
                SourcePath = Path.Combine(_storage, "已删除", "x.dcx"),
                OwnerModId = "mGone",
                Kind = nameof(LinkKind.SymbolicLink),
            },
        };
        Directory.CreateDirectory(Path.Combine(modsDir, ".modmanager"));
        System.IO.File.WriteAllText(
            DeployEngine.GetManifestPath(_game),
            JsonSerializer.Serialize(manifest, Json.Options));

        var result = new DeployEngine(_game, _storage).Clean();

        Assert.Equal(1, result.RemovedStale);
        Assert.False(Directory.EnumerateFileSystemEntries(modsDir, "gone.dcx",
            new EnumerationOptions { RecurseSubdirectories = true }).Any());
    }

    [Fact]
    public void 部署时目标为悬空链接_重新部署成功()
    {
        var engine = new DeployEngine(_game, _storage);
        var mods = new[] { new ModItem { Id = "mA", Name = "A", Priority = 10 } };
        engine.Deploy(engine.Plan(mods));

        // 模拟源文件被移除后留下悬空符号链接的场景：手工放置一个指向不存在目标的链接
        var target = Path.Combine(_game, "mods", "chr", "dangling.dcx");
        try
        {
            File.CreateSymbolicLink(target, Path.Combine(_storage, "不存在", "x.dcx"));
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var result = engine.Deploy(engine.Plan(mods));
        Assert.True(result.Success, string.Join(";", result.Warnings));
        Assert.True(File.Exists(target), "重新部署后该路径应由新链接占据");
    }

    [Fact]
    public void 压缩包路径穿越被拒绝()
    {
        var zipPath = Path.Combine(_root, "evil.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var entry = archive.CreateEntry("../evil.txt").Open();
            using var writer = new StreamWriter(entry);
            writer.Write("evil");
        }

        var dest = Path.Combine(_root, "dest");
        Assert.Throws<IOException>(() => ModArchiveService.ExtractArchive(zipPath, dest));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")), "逃逸文件不应存在");
    }

    [Fact]
    public void 只读目标文件可被安全覆盖与清单清理()
    {
        var modsDir = DeployEngine.GetModsDir(_game);
        Directory.CreateDirectory(Path.Combine(modsDir, "chr"));
        var target = Path.Combine(modsDir, "chr", "readonly.dcx");
        File.WriteAllText(target, "OLD");
        File.SetAttributes(target, FileAttributes.ReadOnly);

        var modDir = Path.Combine(_storage, "mReadonly", "chr");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "readonly.dcx"), "NEW");

        var engine = new DeployEngine(_game, _storage);
        var plan = engine.Plan(new[] { new ModItem { Id = "mReadonly", Name = "ReadonlyMod", Priority = 10 } });

        // 部署应当成功，覆盖只读文件
        var deployResult = engine.Deploy(plan);
        Assert.True(deployResult.Success);
        Assert.Equal("NEW", File.ReadAllText(target));

        // 再次设为只读后清理，清单清理应当成功删除
        File.SetAttributes(target, FileAttributes.ReadOnly);
        var cleanResult = engine.Clean();
        Assert.True(cleanResult.Success);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void 目录清理不会波及相似前缀目录()
    {
        var modsBackupDir = Path.Combine(_game, "mods_backup", "chr");
        Directory.CreateDirectory(modsBackupDir);
        var backupFile = Path.Combine(modsBackupDir, "keep.txt");
        File.WriteAllText(backupFile, "IMPORTANT_DATA");

        var engine = new DeployEngine(_game, _storage);
        var cleanResult = engine.Clean();
        Assert.True(cleanResult.Success);

        // mods_backup 目录必须完好无损
        Assert.True(Directory.Exists(modsBackupDir));
        Assert.True(File.Exists(backupFile));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
