using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SekiroModManager.App.Theme;

/// <summary>
/// Windows 10/11 DWM 原生标题栏与边框主题适配器。
/// 彻底消除 Windows 系统强制强调色（如蓝色）造成的视觉割裂，使标题栏与应用顶栏浑然一体。
/// </summary>
public static class WindowTitleBarHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void ApplyThemeToWindow(Window window, bool isDark)
    {
        if (window == null) return;

        try
        {
            var helper = new WindowInteropHelper(window);
            var hwnd = helper.EnsureHandle();
            ApplyThemeToHwnd(hwnd, isDark);
        }
        catch
        {
            // 在测试环境或非常规窗口生命周期中静默降级
        }
    }

    public static void ApplyThemeToHwnd(IntPtr hwnd, bool isDark)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var darkMode = isDark ? 1 : 0;
            // 1. 深色/浅色模式沉浸式切换
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref darkMode, sizeof(int));

            // 2. Windows 11 自定义标题栏背景色、文字颜色与边框色 (Win32 BGR 格式: 0x00BBGGRR)
            // 亮色模式：顶栏纯白 #FFFFFF (0x00FFFFFF)，文字深黑 #18181B (0x001B1818)，细边框 #DCDFE8 (0x00E8DFDC)
            // 暗色模式：顶栏暗黑 #1C1C21 (0x00211C1C)，文字亮白 #F4F4F7 (0x00F7F4F4)，细边框 #2D2D38 (0x00382D2D)
            int captionColor = isDark
                ? ColorToBgr(0x1C, 0x1C, 0x21)
                : ColorToBgr(0xFF, 0xFF, 0xFF);

            int textColor = isDark
                ? ColorToBgr(0xF4, 0xF4, 0xF7)
                : ColorToBgr(0x18, 0x18, 0x1B);

            int borderColor = isDark
                ? ColorToBgr(0x2D, 0x2D, 0x38)
                : ColorToBgr(0xDC, 0xDF, 0xE8);

            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }
        catch
        {
            // 在不支持 DWM 自定义颜色的旧版系统上静默降级
        }
    }

    private static int ColorToBgr(byte r, byte g, byte b)
        => r | (g << 8) | (b << 16);
}
