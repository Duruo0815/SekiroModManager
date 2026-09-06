using System.Text.Json;
using SekiroModManager.Core.Models;

namespace SekiroModManager.Core;

/// <summary>config.json 的读写。</summary>
public class ConfigStore
{
    private readonly string _configPath;

    public ConfigStore(string baseDir)
        => _configPath = Path.Combine(baseDir, "config.json");

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(_configPath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_configPath), Json.Options) ?? new AppConfig();
        }
        catch
        {
            // 配置损坏时回退到默认配置，避免启动崩溃
        }
        return new AppConfig();
    }

    public void Save(AppConfig config)
        => File.WriteAllText(_configPath, JsonSerializer.Serialize(config, Json.Options));
}
