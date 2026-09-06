using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace SekiroModManager.Core.Services;

/// <summary>
/// MOD 压缩包解压与路径归一化。
/// 玩家打包习惯极不规范（嵌套多层、直接套 chr/、根目录散落 .dcx 等），
/// 导入时递归定位"MOD 根目录"，消除嵌套差异。
/// </summary>
public static class ModArchiveService
{
    /// <summary>只狼游戏数据特征目录。</summary>
    public static readonly string[] FeatureFolders =
    {
        "chr", "parts", "event", "sound", "sfx", "msg", "map", "param",
        "script", "action", "mtd", "font", "menu", "other", "asset", "env", "sgt", "material",
    };

    private static readonly string[] ArchiveExtensions = { ".zip", ".7z", ".rar", ".cbz" };

    public static bool IsSupportedArchive(string path)
        => File.Exists(path) && ArchiveExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static bool IsFolder(string path) => Directory.Exists(path);

    static ModArchiveService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 解压压缩包到 destDir。
    /// 非.UTF8 标记的条目名用 GBK 回退解码，兼容 Windows 资源管理器打包的中文名。
    /// 逐条目校验路径，拒绝条目名逃逸出 destDir 的恶意压缩包（zip-slip）。
    /// </summary>
    public static void ExtractArchive(string archivePath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        var destRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destDir));
        var readerOptions = new ReaderOptions
        {
            ArchiveEncoding = new ArchiveEncoding { Default = Encoding.GetEncoding(936) },
        };
        using var archive = ArchiveFactory.Open(archivePath, readerOptions);
        var extractionOptions = new ExtractionOptions { Overwrite = true };

        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory || entry.Key is null)
                continue;

            var target = Path.GetFullPath(Path.Combine(destRoot, entry.Key));
            if (!target.StartsWith(destRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"压缩包条目路径非法（疑似路径穿越），已中止导入：{entry.Key}");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.WriteToFile(target, extractionOptions);
        }
    }

    /// <summary>
    /// 归一化：广度优先查找第一个"MOD 根目录"——直接包含特征目录（chr/parts 等）
    /// 或直接包含游戏数据文件（.dcx/.param）的最浅目录；找不到则返回解压根目录。
    /// </summary>
    public static string FindModRoot(string root)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            if (Qualifies(dir))
                return dir;
            if (depth >= 8)
                continue;
            foreach (var sub in Directory.EnumerateDirectories(dir))
                queue.Enqueue((sub, depth + 1));
        }
        return root;
    }

    /// <summary>目录的直接子项中是否包含特征目录或游戏数据文件。</summary>
    public static bool Qualifies(string dir)
    {
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(sub).ToLowerInvariant();
            if (FeatureFolders.Contains(name))
                return true;
        }
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".dcx" or ".param")
                return true;
        }
        return false;
    }

    /// <summary>递归复制目录（不跟随目录重解析点；不跳过隐藏/系统文件）。</summary>
    public static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", ScanOptions))
        {
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }

    /// <summary>MOD 文件扫描的统一选项：含隐藏/系统文件，限制递归深度防异常结构。</summary>
    internal static readonly EnumerationOptions ScanOptions = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.None,
        MaxRecursionDepth = 32,
    };
}
