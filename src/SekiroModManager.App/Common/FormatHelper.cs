namespace SekiroModManager.App.Common;

/// <summary>通用格式化辅助方法（消除 ViewModel 间的重复代码）。</summary>
public static class FormatHelper
{
    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B"
    };
}
