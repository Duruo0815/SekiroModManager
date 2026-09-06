namespace SekiroModManager.Core.Models;

/// <summary>部署清单中的一个条目（持久化到 mods/.modmanager/manifest.json）。</summary>
public class DeployedEntry
{
    /// <summary>游戏内相对路径（相对 mods/）。</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>storage 中的源文件绝对路径。</summary>
    public string SourcePath { get; set; } = "";

    /// <summary>归属 MOD 的 Id。</summary>
    public string OwnerModId { get; set; } = "";

    /// <summary>挂载方式（LinkKind 名称）。</summary>
    public string Kind { get; set; } = "";
}

/// <summary>一次部署/清理的结果。</summary>
public class DeployResult
{
    public bool Success { get; set; }

    public string? Error { get; set; }

    public List<string> Warnings { get; } = new();

    /// <summary>按挂载方式统计的部署数量（键为 LinkKind 名称）。</summary>
    public Dictionary<string, int> DeployedCounts { get; } = new();

    /// <summary>本次清理掉的失效托管条目数。</summary>
    public int RemovedStale { get; set; }

    public TimeSpan Duration { get; set; }
}

/// <summary>MOD 导入结果。</summary>
public record ImportResult(string ModId, string ModRoot, List<string> Warnings);
