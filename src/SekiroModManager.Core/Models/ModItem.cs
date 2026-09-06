namespace SekiroModManager.Core.Models;

/// <summary>一个已导入的 MOD。</summary>
public class ModItem
{
    /// <summary>唯一标识，同时作为 storage 下的目录名。</summary>
    public string Id { get; set; } = "";

    /// <summary>显示名称（默认取压缩包/文件夹名）。</summary>
    public string Name { get; set; } = "";

    /// <summary>原始来源（压缩包或文件夹路径），仅作记录。</summary>
    public string? SourcePath { get; set; }

    /// <summary>来源链接（如 NexusMOD 页面），可选元数据。</summary>
    public string? SourceUrl { get; set; }

    public string? Version { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.Now;

    public bool Enabled { get; set; }

    /// <summary>
    /// 优先级。数值越大越优先：同路径文件冲突时，高优先级 MOD 的文件覆盖低优先级。
    /// 部署时按优先级从低到高挂载，后者覆盖前者。
    /// </summary>
    public int Priority { get; set; }
}
