namespace SekiroModManager.Core.Models;

/// <summary>部署时实际采用的文件挂载方式（按优先级自动降级）。</summary>
public enum LinkKind
{
    /// <summary>硬链接：同卷免管理员权限，性能等同物理文件，删除链接不影响源文件。</summary>
    HardLink,

    /// <summary>符号链接：需开发者模式或管理员权限（跨卷时的首选）。</summary>
    SymbolicLink,

    /// <summary>物理复制：前两者都不可用时的兜底。</summary>
    Copy,
}

/// <summary>部署计划中的一个文件：该游戏相对路径最终由哪个 MOD 的哪个文件生效。</summary>
public record PlannedFile(
    string RelativePath,
    string SourcePath,
    string WinnerModId,
    string WinnerModName,
    LinkKind PlannedKind);

/// <summary>冲突记录：低优先级 MOD 的该文件将被高优先级 MOD 覆盖。</summary>
public record ConflictEntry(
    string RelativePath,
    string WinnerModName,
    string LoserModName);

/// <summary>一次部署的完整计划（预览模型，不落盘）。</summary>
public class DeployPlan
{
    /// <summary>最终生效的文件清单（每个游戏相对路径一条）。</summary>
    public List<PlannedFile> Files { get; init; } = new();

    /// <summary>被覆盖的文件（冲突明细）。</summary>
    public List<ConflictEntry> Conflicts { get; init; } = new();

    public List<string> Warnings { get; init; } = new();

    /// <summary>所有启用 MOD 的文件总数（含被覆盖的）。</summary>
    public int TotalSourceFiles { get; init; }
}
