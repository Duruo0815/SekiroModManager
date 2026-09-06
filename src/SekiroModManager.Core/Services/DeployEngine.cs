using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SekiroModManager.Core.Models;

namespace SekiroModManager.Core.Services;

/// <summary>
/// 部署引擎：把启用 MOD 的文件按优先级挂载到游戏 mods/ 目录。
///
/// 设计要点：
/// 1. 挂载方式自动降级：同卷硬链接（免管理员）→ 符号链接（需开发者模式/管理员）→ 物理复制；
/// 2. 清理是"清单驱动"的：每次部署在 mods/.modmanager/manifest.json 记录全部托管条目，
///    重新部署/清理时只删除清单内条目，玩家手动放入 mods/ 的文件永远不会被误删；
/// 3. 部署前强制检查游戏进程。
/// </summary>
public class DeployEngine
{
    private readonly string _gamePath;
    private readonly string _storageRoot;

    public DeployEngine(string gamePath, string storageRoot)
    {
        _gamePath = gamePath;
        _storageRoot = storageRoot;
    }

    public static string GetModsDir(string gamePath) => Path.Combine(gamePath, "mods");

    public static string GetManifestPath(string gamePath)
        => Path.Combine(GetModsDir(gamePath), ".modmanager", "manifest.json");

    public static bool IsGameRunning()
        => Process.GetProcessesByName("sekiro").Length > 0;

    /// <summary>
    /// 构建部署计划：按优先级从低到高遍历各 MOD 的全部文件，
    /// 同一游戏相对路径后来者覆盖前者，被覆盖者记入 Conflicts。
    /// </summary>
    public DeployPlan Plan(IEnumerable<ModItem> enabledMods)
    {
        var warnings = new List<string>();
        var files = new Dictionary<string, PlannedFile>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<ConflictEntry>();
        var total = 0;
        var modsRoot = Path.GetFullPath(GetModsDir(_gamePath));

        foreach (var mod in enabledMods.OrderBy(m => m.Priority))
        {
            var modDir = Path.Combine(_storageRoot, mod.Id);
            if (!Directory.Exists(modDir))
            {
                warnings.Add($"MOD 仓库目录缺失，已跳过：{mod.Name}（{modDir}）");
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(modDir, "*", ModArchiveService.ScanOptions))
            {
                total++;
                var rel = Path.GetRelativePath(modDir, file);
                if (files.TryGetValue(rel, out var previous))
                    conflicts.Add(new ConflictEntry(rel, mod.Name, previous.WinnerModName));
                files[rel] = new PlannedFile(rel, file, mod.Id, mod.Name, GuessKind(file, modsRoot));
            }
        }

        return new DeployPlan
        {
            Files = files.Values.ToList(),
            Conflicts = conflicts,
            TotalSourceFiles = total,
            Warnings = warnings,
        };
    }

    /// <summary>预估挂载方式：源文件与游戏同卷用硬链接，跨卷用符号链接。</summary>
    private static LinkKind GuessKind(string source, string modsRoot)
    {
        var sourceRoot = Path.GetPathRoot(Path.GetFullPath(source)) ?? "";
        return string.Equals(sourceRoot, Path.GetPathRoot(modsRoot) ?? "", StringComparison.OrdinalIgnoreCase)
            ? LinkKind.HardLink
            : LinkKind.SymbolicLink;
    }

    /// <summary>执行部署：进程检查 → 清单驱动清理旧条目 → 挂载 → 写新清单。</summary>
    public DeployResult Deploy(DeployPlan plan)
    {
        var sw = Stopwatch.StartNew();
        var result = new DeployResult();

        if (IsGameRunning())
        {
            result.Error = "《只狼》正在运行，请先关闭游戏再部署。";
            return result;
        }

        CleanInternal(result);

        var modsDir = GetModsDir(_gamePath);
        Directory.CreateDirectory(modsDir);

        var deployed = new List<DeployedEntry>();
        foreach (var file in plan.Files)
        {
            var target = Path.Combine(modsDir, file.RelativePath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (Directory.Exists(target))
                {
                    result.Warnings.Add($"目标路径被同名目录占用，已跳过：{file.RelativePath}");
                    continue;
                }
                // 无条件尝试删除：清除可能存在的只读属性，且除普通文件外还能清掉悬空的符号链接（File.Exists 探测不到链接本身）
                try
                {
                    if (File.Exists(target))
                        File.SetAttributes(target, FileAttributes.Normal);
                    File.Delete(target);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }

                var kind = CreateLink(file.SourcePath, target);
                deployed.Add(new DeployedEntry
                {
                    RelativePath = file.RelativePath,
                    SourcePath = file.SourcePath,
                    OwnerModId = file.WinnerModId,
                    Kind = kind.ToString(),
                });
                result.DeployedCounts[kind.ToString()] = result.DeployedCounts.TryGetValue(kind.ToString(), out var n) ? n + 1 : 1;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"部署失败 {file.RelativePath}: {ex.Message}");
            }
        }

        SaveManifest(deployed);
        result.Success = true;
        result.Duration = sw.Elapsed;
        return result;
    }

    /// <summary>按清单清理全部托管条目（不动外来文件）。</summary>
    public DeployResult Clean()
    {
        var result = new DeployResult();
        CleanInternal(result);
        result.Success = true;
        return result;
    }

    private void CleanInternal(DeployResult result)
    {
        var modsDir = GetModsDir(_gamePath);
        if (!Directory.Exists(modsDir))
            return;

        var manifestPath = GetManifestPath(_gamePath);
        if (!File.Exists(manifestPath))
        {
            // 无清单：无法区分托管与外来文件，只警告、不动手（.modmanager 目录本身不算内容）
            var hasContent = Directory.EnumerateFileSystemEntries(modsDir)
                .Any(e => !string.Equals(Path.GetFileName(e), ".modmanager", StringComparison.OrdinalIgnoreCase));
            if (hasContent)
                result.Warnings.Add("mods/ 目录中存在不由本管理器管理的文件（无部署清单），已跳过清理，请自行确认。");
            return;
        }

        var manifest = LoadManifest();
        if (manifest is null)
        {
            result.Warnings.Add("部署清单损坏无法解析，已跳过清理（请检查 mods/.modmanager/manifest.json）。");
            return;
        }

        var dirsToPrune = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest)
        {
            var path = Path.Combine(modsDir, entry.RelativePath);
            try
            {
                if (File.Exists(path))
                {
                    var attributes = File.GetAttributes(path);
                    var isReparse = attributes.HasFlag(FileAttributes.ReparsePoint);
                    // 硬链接/符号链接/复制文件均由本管理器创建，可直接删除；
                    // 若条目声明为链接但实际是普通文件，说明被外部替换过，保留并提示。
                    var safeToDelete = entry.Kind is nameof(LinkKind.HardLink) or nameof(LinkKind.Copy) || isReparse;
                    if (safeToDelete)
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                        result.RemovedStale++;
                    }
                    else
                    {
                        result.Warnings.Add($"清单条目与实际不符（被外部替换为普通文件），已保留：{entry.RelativePath}");
                    }
                }
                else if (entry.Kind == nameof(LinkKind.SymbolicLink))
                {
                    // 源文件被移除后符号链接会悬空（File.Exists 探测不到），但链接本身仍需删除
                    try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                    File.Delete(path);
                    result.RemovedStale++;
                }
            }
            catch (FileNotFoundException)
            {
                // 路径本身不存在（链接已不在），忽略
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"清理失败 {entry.RelativePath}: {ex.Message}");
            }
            var dir = Path.GetDirectoryName(path);
            if (dir is not null)
                dirsToPrune.Add(dir);
        }

        // 自底向上删除空目录：只沿清单记录的祖先链向上走，绝不触碰 mods/ 根本身
        var modsDirWithSep = Path.TrimEndingDirectorySeparator(modsDir) + Path.DirectorySeparatorChar;
        foreach (var dir in dirsToPrune.OrderByDescending(d => d.Length))
        {
            var current = dir;
            while (current is not null
                   && (current + Path.DirectorySeparatorChar).StartsWith(modsDirWithSep, StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(current, modsDir, StringComparison.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any())
                    break;
                try
                {
                    Directory.Delete(current);
                }
                catch
                {
                    break;
                }
                current = Directory.GetParent(current)?.FullName;
            }
        }
    }

    /// <summary>
    /// 挂载单个文件，按可用性自动降级：
    /// 同卷硬链接 → 符号链接 → 物理复制。
    /// </summary>
    private static LinkKind CreateLink(string source, string target)
    {
        if (SameVolume(source, target) && TryCreateHardLink(source, target))
            return LinkKind.HardLink;

        try
        {
            File.CreateSymbolicLink(target, source);
            return LinkKind.SymbolicLink;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            // 未开启开发者模式且无管理员权限时的典型失败，降级处理
        }

        File.Copy(source, target, true);
        return LinkKind.Copy;
    }

    private static bool SameVolume(string a, string b)
    {
        var rootA = Path.GetPathRoot(Path.GetFullPath(a)) ?? "";
        var rootB = Path.GetPathRoot(Path.GetFullPath(b)) ?? "";
        return string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkNative(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    private static bool TryCreateHardLink(string source, string target)
        => CreateHardLinkNative(target, source, IntPtr.Zero);

    private List<DeployedEntry>? LoadManifest()
    {
        var path = GetManifestPath(_gamePath);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<DeployedEntry>>(File.ReadAllText(path), Json.Options);
        }
        catch
        {
            return null;
        }
    }

    private void SaveManifest(List<DeployedEntry> entries)
    {
        var path = GetManifestPath(_gamePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, Json.Options));
    }
}
