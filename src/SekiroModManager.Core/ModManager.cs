using System.Diagnostics;
using SekiroModManager.Core.Models;
using SekiroModManager.Core.Services;

namespace SekiroModManager.Core;

/// <summary>
/// 对外门面：定位游戏、导入 MOD、生成部署计划、执行部署、启动游戏。
/// 任何 UI（WPF / Avalonia / CLI）只依赖这一层即可，业务与界面完全解耦。
/// </summary>
public class ModManager
{
    private readonly ConfigStore _store;

    /// <param name="baseDir">工作目录（config.json 与 storage/ 所在处）。默认程序目录，绿色便携。</param>
    public ModManager(string? baseDir = null)
    {
        BaseDir = baseDir ?? AppContext.BaseDirectory;
        _store = new ConfigStore(BaseDir);
        Config = _store.Load();
    }

    public string BaseDir { get; }

    public AppConfig Config { get; private set; }

    public string StorageRoot
        => Path.IsPathRooted(Config.StorageRoot) ? Config.StorageRoot : Path.Combine(BaseDir, Config.StorageRoot);

    public void SaveConfig() => _store.Save(Config);

    /// <summary>校验并设置游戏目录。</summary>
    public bool SetGamePath(string path)
    {
        if (!GameLocator.IsValidGamePath(path))
            return false;
        Config.GamePath = Path.GetFullPath(path);
        SaveConfig();
        return true;
    }

    /// <summary>自动定位游戏并写入配置；成功返回路径，失败返回空字符串。</summary>
    public string LocateGame()
    {
        var path = GameLocator.LocateGame();
        if (!string.IsNullOrEmpty(path))
        {
            Config.GamePath = path;
            SaveConfig();
        }
        return path;
    }

    /// <summary>
    /// 导入 MOD（zip/7z/rar 压缩包或文件夹），自动归一化后存入仓库并启用。
    /// </summary>
    public ImportResult Import(string sourcePath, string? displayName = null)
    {
        var warnings = new List<string>();
        sourcePath = sourcePath.Trim('"', ' ');
        var isArchive = ModArchiveService.IsSupportedArchive(sourcePath);
        var isFolder = ModArchiveService.IsFolder(sourcePath);
        if (!isArchive && !isFolder)
            throw new FileNotFoundException("请提供 zip/7z/rar 压缩包或 MOD 文件夹。", sourcePath);

        Directory.CreateDirectory(StorageRoot);
        var modId = NewModId();
        var finalDir = Path.Combine(StorageRoot, modId);

        if (isArchive)
        {
            // 解压到仓库内的临时目录（保证同卷 Move），归一化后移入正式位置
            var tmpDir = Path.Combine(StorageRoot, ".tmp", modId);
            try
            {
                ModArchiveService.ExtractArchive(sourcePath, tmpDir);
                var root = ModArchiveService.FindModRoot(tmpDir);
                if (root == tmpDir)
                    warnings.Add("未识别到游戏数据特征（chr/parts 等目录或 .dcx 文件），已按解压根目录导入，请确认内容。");
                if (Directory.Exists(finalDir))
                    Directory.Delete(finalDir, true);
                Directory.Move(root, finalDir);
            }
            finally
            {
                if (Directory.Exists(tmpDir))
                    Directory.Delete(tmpDir, true);
            }
        }
        else
        {
            var root = ModArchiveService.FindModRoot(sourcePath);
            if (!string.Equals(root, sourcePath, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"已识别 MOD 根目录：{Path.GetFullPath(root)}（仅导入该子目录）");
            if (Directory.Exists(finalDir))
                Directory.Delete(finalDir, true);
            ModArchiveService.CopyDirectory(root, finalDir);
        }

        var modName = string.IsNullOrWhiteSpace(displayName)
            ? isArchive
                ? Path.GetFileNameWithoutExtension(sourcePath)
                : Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : displayName.Trim();
        if (string.IsNullOrWhiteSpace(modName))
            modName = modId;

        var mod = new ModItem
        {
            Id = modId,
            Name = modName,
            SourcePath = sourcePath,
            Enabled = true,
            Priority = Config.Mods.Count == 0 ? 10 : Config.Mods.Max(m => m.Priority) + 10,
        };
        Config.Mods.Add(mod);
        SaveConfig();
        return new ImportResult(modId, finalDir, warnings);
    }

    /// <summary>移除 MOD：删除仓库目录与配置项。已部署的链接在下次部署时随清单清理。</summary>
    public bool RemoveMod(string modId)
    {
        var mod = FindMod(modId);
        if (mod is null)
            return false;
        var dir = Path.Combine(StorageRoot, mod.Id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        Config.Mods.Remove(mod);
        SaveConfig();
        return true;
    }

    /// <summary>按 Id 或唯一前缀查找 MOD。</summary>
    public ModItem? FindMod(string idOrPrefix)
    {
        var exact = Config.Mods.FirstOrDefault(m => string.Equals(m.Id, idOrPrefix, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;
        var matches = Config.Mods.Where(m => m.Id.StartsWith(idOrPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>构建部署计划（启用 MOD，按优先级解析冲突），供 UI 预览。</summary>
    public DeployPlan Plan()
        => CreateEngine().Plan(Config.Mods.Where(m => m.Enabled));

    /// <summary>
    /// 执行部署：校验游戏路径与进程 → 维护 modengine.ini（自动备份）→ 清单驱动挂载。
    /// </summary>
    public DeployResult Deploy(DeployPlan? plan = null)
    {
        if (!GameLocator.IsValidGamePath(Config.GamePath))
            return new DeployResult { Success = false, Error = "游戏路径无效，请先运行 locate 或 set-game。" };
        if (DeployEngine.IsGameRunning())
            return new DeployResult { Success = false, Error = "《只狼》正在运行，请先关闭游戏再部署。" };

        string iniMessage;
        try
        {
            iniMessage = ModEngineService.EnsureModDir(Config.GamePath, ModEngineService.DefaultModDir);
        }
        catch (Exception ex)
        {
            // ini 不可写（如只读）不应阻断部署：Mod Engine 默认即加载 mods/ 目录
            iniMessage = $"modengine.ini 检查失败（部署继续）：{ex.Message}";
        }

        var engine = CreateEngine();
        plan ??= engine.Plan(Config.Mods.Where(m => m.Enabled));
        var result = engine.Deploy(plan);
        // ini 状态仅在非“无需修改”时才插入警告，避免正常场景下的信息噪音
        if (!iniMessage.Contains("无需修改"))
            result.Warnings.Insert(0, iniMessage);
        foreach (var warning in plan.Warnings)
            result.Warnings.Add(warning);
        return result;
    }

    /// <summary>按部署清单清理游戏 mods/ 目录（不动外来文件）。</summary>
    public DeployResult Clean() => CreateEngine().Clean();

    public ModEngineStatus CheckModEngine()
        => GameLocator.IsValidGamePath(Config.GamePath)
            ? ModEngineService.Check(Config.GamePath)
            : new ModEngineStatus(false, false, "");

    public bool IsGameRunning() => DeployEngine.IsGameRunning();

    /// <summary>直接启动 sekiro.exe（Mod Engine 通过 dinput8.dll 自动生效，无需特殊启动器）。</summary>
    public bool LaunchGame()
    {
        if (!GameLocator.IsValidGamePath(Config.GamePath))
            return false;
        var exe = Path.Combine(Config.GamePath, GameLocator.ExeName);
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Config.GamePath, UseShellExecute = true });
        return true;
    }

    private DeployEngine CreateEngine() => new(Config.GamePath, StorageRoot);

    private static string NewModId()
        => "m" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + Guid.NewGuid().ToString("N")[..4];
}
