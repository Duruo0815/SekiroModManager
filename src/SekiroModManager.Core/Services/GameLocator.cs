using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SekiroModManager.Core.Services;

/// <summary>定位并校验《只狼》安装目录（Steam AppId 814380）。</summary>
public static class GameLocator
{
    public const string SekiroAppId = "814380";
    public const string ExeName = "sekiro.exe";

    /// <summary>校验目录是否为有效的游戏根目录（存在 sekiro.exe）。</summary>
    public static bool IsValidGamePath(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && Directory.Exists(path)
           && File.Exists(Path.Combine(path, ExeName));

    /// <summary>自动定位游戏：卸载注册表 → Steam 库扫描。找不到返回空字符串。</summary>
    public static string LocateGame()
    {
        foreach (var candidate in EnumerateCandidates())
        {
            if (IsValidGamePath(candidate))
                return candidate;
        }
        return "";
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        // 1) 卸载信息注册表（64 位视图，兼顾 WOW6432Node 重定向）
        string[] uninstallKeys =
        {
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {SekiroAppId}",
            $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {SekiroAppId}",
        };
        foreach (var keyName in uninstallKeys)
        {
            string? location = null;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(keyName);
                location = key?.GetValue("InstallLocation") as string;
            }
            catch
            {
                // 注册表不可访问时静默跳过
            }
            if (!string.IsNullOrWhiteSpace(location))
                yield return location!;
        }

        // 2) Steam 库扫描：解析 libraryfolders.vdf 中所有 "path" 库目录
        var steamLibraries = new List<string>();
        try
        {
            using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = steamKey?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                var vdf = Path.Combine(steamPath!, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    var content = File.ReadAllText(vdf);
                    foreach (Match match in Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\""))
                    {
                        var library = match.Groups[1].Value.Replace("\\\\", "\\");
                        steamLibraries.Add(library);
                    }
                }
            }
        }
        catch
        {
            // Steam 未安装或注册表不可访问
        }

        foreach (var library in steamLibraries)
            yield return Path.Combine(library, "steamapps", "common", "Sekiro");
    }
}
