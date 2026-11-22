using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NetGuardian.App.Tests;

internal static class TestDesktopWindow
{
    private const int DwmCloak = 13;

    // 保留布局、键盘与 UI 自动化所需的真实 HWND，在首次 Show 前隐藏桌面预览。
    internal static void Show(Window window)
    {
        Prepare(window);
        window.Show();
    }

    internal static bool? ShowDialog(Window window)
    {
        Prepare(window);
        return window.ShowDialog();
    }

    private static void Prepare(Window window)
    {
        window.ShowInTaskbar = false;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var cloak = 1;
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(hwnd, DwmCloak, ref cloak, sizeof(int)));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
