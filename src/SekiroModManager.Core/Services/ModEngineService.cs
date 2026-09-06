namespace SekiroModManager.Core.Services;

public record ModEngineStatus(bool Dinput8Present, bool IniPresent, string IniPath)
{
    public bool Installed => Dinput8Present && IniPresent;
}

/// <summary>
/// Sekiro Mod Engine 检测与 modengine.ini 维护。
/// 原则：只改写明确命中的 modDirectory/modDir 键，其余内容一律不动；改写前自动备份 .bak。
/// </summary>
public static class ModEngineService
{
    private static readonly string[] ModDirKeys = { "moddirectory", "moddir" };

    /// <summary>modengine.ini 中 MOD 目录的期望值（Mod Engine 默认即为 mods）。</summary>
    public const string DefaultModDir = "mods";

    public static ModEngineStatus Check(string gamePath)
    {
        var iniPath = Path.Combine(gamePath, "modengine.ini");
        return new ModEngineStatus(
            File.Exists(Path.Combine(gamePath, "dinput8.dll")),
            File.Exists(iniPath),
            iniPath);
    }

    /// <summary>
    /// 确保 modengine.ini 的 MOD 目录指向 targetModDir。
    /// 返回一条人类可读的结果说明（无需修改 / 已改写并备份 / 未找到 ini）。
    /// </summary>
    public static string EnsureModDir(string gamePath, string targetModDir)
    {
        var iniPath = Path.Combine(gamePath, "modengine.ini");
        if (!File.Exists(iniPath))
            return "未找到 modengine.ini，跳过配置检查（Mod Engine 可能未安装）。";

        var lines = File.ReadAllLines(iniPath);
        var desired = NormalizeDir(targetModDir);
        var changed = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var (key, eqIndex) = SplitIniLine(lines[i]);
            if (key is null || eqIndex < 0 || !ModDirKeys.Contains(key))
                continue;

            // 取 '=' 之后的值并归一化（去引号、统一斜杠、去掉 .\ 前缀）
            var currentValue = lines[i].Substring(eqIndex + 1).Trim().Trim('"');
            if (string.Equals(NormalizeDir(currentValue), desired, StringComparison.OrdinalIgnoreCase))
                return "modengine.ini 无需修改。";

            lines[i] = lines[i].Substring(0, eqIndex + 1) + $" \"{targetModDir}\"";
            changed = true;
            break;
        }

        if (!changed)
            return "modengine.ini 中未找到 modDirectory/modDir 键（Mod Engine 默认加载 mods/ 目录），未做改动。";

        File.Copy(iniPath, iniPath + ".bak", overwrite: true);
        File.WriteAllLines(iniPath, lines);
        return $"已将 modengine.ini 的 MOD 目录改为 \"{targetModDir}\"（原文件备份为 modengine.ini.bak）。";
    }

    private static string NormalizeDir(string dir)
        => dir.Replace("\\", "/").Trim().Trim('/').TrimStart('.');

    private static (string? Key, int EqIndex) SplitIniLine(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith(';') || trimmed.StartsWith('#') || trimmed.StartsWith('['))
            return (null, -1);
        var eq = line.IndexOf('=');
        if (eq <= 0)
            return (null, -1);
        return (line.Substring(0, eq).Trim().ToLowerInvariant(), eq);
    }
}
