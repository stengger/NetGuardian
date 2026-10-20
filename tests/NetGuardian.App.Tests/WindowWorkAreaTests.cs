using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

[Collection("WPF 桌面")]
public sealed class WindowWorkAreaTests
{
    [Theory]
    [InlineData(0, 0, 1180, 760, 0, 0, 960, 500, 0, 0, 960, 500)]
    [InlineData(-1900, 50, 1180, 760, -1920, 40, 960, 500, -1920, 40, 960, 500)]
    [InlineData(1500, 800, 600, 400, 0, 40, 1920, 1000, 1320, 640, 600, 400)]
    [InlineData(100, 100, 1180, 760, 0, 0, 1920, 1040, 100, 100, 1180, 760)]
    public void 窗口物理边界保持在工作区内(double x, double y, double w, double h,
        double ax, double ay, double aw, double ah, double ex, double ey, double ew, double eh)
    {
        Assert.Equal(new Rect(ex, ey, ew, eh), WindowWorkArea.Fit(new Rect(x, y, w, h), new Rect(ax, ay, aw, ah)));
    }

    [Fact]
    public void 真实测试窗口缩小工作区后可完整放置并恢复原最小尺寸()
    {
        DesktopFixture.Run(window =>
        {
            // 仅操作夹具自己的窗口；负坐标避免测试窗口出现在用户工作区。
            var area = new Rect(-20000, -20000, 960, 500);
            using var policy = new WindowWorkArea(window, () => area);
            policy.Refresh();
            var dpi = VisualTreeHelper.GetDpi(window);
            Assert.Equal(Math.Min(1040, area.Width / dpi.DpiScaleX), window.MinWidth, 3);
            Assert.Equal(Math.Min(660, area.Height / dpi.DpiScaleY), window.MinHeight, 3);
            Assert.True(GetWindowRect(new WindowInteropHelper(window).Handle, out var actual));
            Assert.True(area.Contains(new Rect(actual.Left, actual.Top, actual.Right - actual.Left, actual.Bottom - actual.Top)),
                $"窗口 {actual.Left},{actual.Top},{actual.Right},{actual.Bottom} 超出 {area}");
            area = new Rect(-20000, -20000, 2000, 1400);
            policy.Refresh();
            Assert.Equal(1040, window.MinWidth);
            Assert.Equal(660, window.MinHeight);
        });
    }

    [Fact]
    public void 无效工作区和释放后的通知不改变窗口()
    {
        DesktopFixture.Run(window =>
        {
            var area = Rect.Empty;
            var policy = new WindowWorkArea(window, () => area);
            var before = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
            policy.Refresh();
            Assert.Equal(1040, window.MinWidth);
            Assert.Equal(660, window.MinHeight);
            policy.Dispose();
            area = new Rect(-20000, -20000, 960, 500);
            policy.Refresh();
            Assert.Equal(before, new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight));
        });
    }

    [Fact]
    public void 创建前接线并在显示与工作区消息后自动收回窗口()
    {
        DesktopFixture.Run(owner =>
        {
            var area = new Rect(-20000, -20000, 960, 500);
            var window = new Window { Width = 1180, Height = 760, MinWidth = 1040, MinHeight = 660,
                Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual };
            using var policy = new WindowWorkArea(window, () => area);
            try
            {
                window.Show();
                Drain(window);
                AssertContained(window, area);
                window.Hide();
                area = new Rect(-19000, -19000, 900, 480);
                window.Show();
                Drain(window);
                AssertContained(window, area);
                area = new Rect(-18000, -18000, 850, 450);
                SendMessage(new WindowInteropHelper(window).Handle, 0x001A, IntPtr.Zero, IntPtr.Zero);
                Drain(window);
                AssertContained(window, area);
                // 关闭前仍有排队消息时释放，不能继续改写窗口。
                area = new Rect(-17000, -17000, 800, 420);
                SendMessage(new WindowInteropHelper(window).Handle, 0x0232, IntPtr.Zero, IntPtr.Zero);
                policy.Dispose();
                Drain(window);
                Assert.True(window.MinWidth > 800);
            }
            finally { window.Close(); }
        });
    }

    private static void Drain(Window window) => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    [Fact]
    public void 拖动期间工作区变化延后到鼠标释放再收回窗口()
    {
        DesktopFixture.Run(window =>
        {
            var area = new Rect(-20000, -20000, 2000, 1400);
            using var policy = new WindowWorkArea(window, () => area);
            policy.Refresh();
            var handle = new WindowInteropHelper(window).Handle;
            Assert.True(GetWindowRect(handle, out var before));
            SendMessage(handle, 0x0231, IntPtr.Zero, IntPtr.Zero);
            area = new Rect(-20000, -20000, 960, 500);
            SendMessage(handle, 0x001A, IntPtr.Zero, IntPtr.Zero);
            Drain(window);
            Assert.True(GetWindowRect(handle, out var during));
            Assert.Equal(before, during);
            SendMessage(handle, 0x0232, IntPtr.Zero, IntPtr.Zero);
            Drain(window);
            AssertContained(window, area);
        });
    }

    private static void AssertContained(Window window, Rect area)
    {
        Assert.True(GetWindowRect(new WindowInteropHelper(window).Handle, out var rect));
        Assert.True(area.Contains(new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)),
            $"窗口 {rect.Left},{rect.Top},{rect.Right},{rect.Bottom} 超出 {area}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
}
