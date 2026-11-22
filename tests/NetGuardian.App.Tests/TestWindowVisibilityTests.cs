using System.Runtime.InteropServices;
using System.Windows.Interop;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class TestWindowVisibilityTests
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [Fact]
    public void 离屏主窗口仍可测量且在首次显示时已从桌面合成中隐藏()
    {
        DesktopFixture.Run(window =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            Assert.True(window.IsVisible && window.IsLoaded && window.ActualWidth > 0);
            Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(handle, 14, out var cloak, sizeof(int)));
            Assert.True((cloak & 1) != 0, $"PID={Environment.ProcessId}，标题={window.Title}，HWND={handle}，DWM 隐藏标记={cloak}");
        });
    }
}
