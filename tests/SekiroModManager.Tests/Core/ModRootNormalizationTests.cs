using SekiroModManager.Core.Services;
using Xunit;

namespace SekiroModManager.Core.Tests;

public class ModRootNormalizationTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "smm_norm_" + Guid.NewGuid().ToString("N"));

    private string Dir(params string[] parts)
    {
        var path = Path.Combine(new[] { _tmp }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private string File(params string[] parts)
    {
        var path = Path.Combine(new[] { _tmp }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void 嵌套一层_识别子目录为根()
    {
        File("MyMod", "chr", "a.dcx");
        Assert.Equal(Path.Combine(_tmp, "MyMod"), ModArchiveService.FindModRoot(_tmp));
    }

    [Fact]
    public void 根目录即_MOD_直接返回()
    {
        File("chr", "a.dcx");
        Assert.Equal(_tmp, ModArchiveService.FindModRoot(_tmp));
    }

    [Fact]
    public void 深层嵌套_能穿过无关目录定位()
    {
        File("a", "b", "c", "MyMod", "parts", "x.dcx");
        Assert.Equal(Path.Combine(_tmp, "a", "b", "c", "MyMod"), ModArchiveService.FindModRoot(_tmp));
    }

    [Fact]
    public void 根目录散落参数文件_视为根()
    {
        File("gameparam.parambnd.dcx");
        Assert.Equal(_tmp, ModArchiveService.FindModRoot(_tmp));
    }

    [Fact]
    public void 无特征时兜底返回根目录()
    {
        File("readme", "说明.txt");
        Assert.Equal(_tmp, ModArchiveService.FindModRoot(_tmp));
    }

    [Fact]
    public void 多个子目录时选取最浅的含特征者()
    {
        File("docs", "readme.txt");
        File("mod", "chr", "a.dcx");
        Assert.Equal(Path.Combine(_tmp, "mod"), ModArchiveService.FindModRoot(_tmp));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmp))
            Directory.Delete(_tmp, true);
    }
}
