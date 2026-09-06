namespace SekiroModManager.Core.Models;

/// <summary>管理器全局配置（持久化为程序目录下 config.json）。</summary>
public class AppConfig
{
    /// <summary>游戏根目录（包含 sekiro.exe）。</summary>
    public string GamePath { get; set; } = "";

    /// <summary>MOD 仓库目录。相对路径基于程序目录，默认 storage。</summary>
    public string StorageRoot { get; set; } = "storage";

    public List<ModItem> Mods { get; set; } = new();
}
