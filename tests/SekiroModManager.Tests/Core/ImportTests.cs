using System.IO.Compression;
using SekiroModManager.Core;
using Xunit;

namespace SekiroModManager.Core.Tests;

/// <summary>ModManager 门面集成测试：压缩包导入 → 归一化 → 配置持久化。</summary>
public class ImportTests : IDisposable
{
    private readonly string _baseDir = Path.Combine(Path.GetTempPath(), "smm_import_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void 导入嵌套压缩包_自动归一化()
    {
        var mm = new ModManager(_baseDir);
        var zipPath = Path.Combine(_baseDir, "我的MOD包.zip");
        Directory.CreateDirectory(_baseDir);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var entry = archive.CreateEntry("MyMod/chr/a.dcx").Open();
            using var writer = new StreamWriter(entry);
            writer.Write("content");
        }

        var result = mm.Import(zipPath);

        Assert.Single(mm.Config.Mods);
        var mod = mm.Config.Mods[0];
        Assert.Equal(result.ModId, mod.Id);
        Assert.Equal("我的MOD包", mod.Name);
        Assert.True(mod.Enabled);
        // 归一化：chr 目录直接位于仓库目录下，而非 MyMod/chr
        Assert.True(Directory.Exists(Path.Combine(mm.StorageRoot, result.ModId, "chr")));
        Assert.True(File.Exists(Path.Combine(mm.StorageRoot, result.ModId, "chr", "a.dcx")));
        // 配置已持久化
        Assert.True(File.Exists(Path.Combine(_baseDir, "config.json")));
    }

    [Fact]
    public void 导入文件夹_直接复制()
    {
        var mm = new ModManager(_baseDir);
        var modDir = Path.Combine(_baseDir, "源MOD", "parts");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "w.dcx"), "x");

        var result = mm.Import(Path.Combine(_baseDir, "源MOD"), "雷切");

        Assert.Equal("雷切", mm.Config.Mods[0].Name);
        Assert.True(File.Exists(Path.Combine(mm.StorageRoot, result.ModId, "parts", "w.dcx")));
    }

    [Fact]
    public void 前缀查找与移除()
    {
        var mm = new ModManager(_baseDir);
        var dir = Path.Combine(_baseDir, "src");
        Directory.CreateDirectory(dir);
        var id = mm.Import(dir).ModId;

        Assert.NotNull(mm.FindMod(id[..6]));
        Assert.True(mm.RemoveMod(id[..6]));
        Assert.Empty(mm.Config.Mods);
        Assert.False(Directory.Exists(Path.Combine(mm.StorageRoot, id)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDir))
            Directory.Delete(_baseDir, true);
    }
}
