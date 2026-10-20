using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace NetGuardian.App.Presentation;

internal sealed class WindowWorkArea : IDisposable
{
    private readonly Window _window;
    private readonly Func<Rect> _readWorkArea;
    private readonly double _minimumWidth;
    private readonly double _minimumHeight;
    private HwndSource? _source;
    private DispatcherOperation? _pending;
    private bool _disposed;
    private bool _interactiveMove;

    public WindowWorkArea(Window window, Func<Rect>? readWorkArea = null)
    {
        _window = window;
        _minimumWidth = window.MinWidth;
        _minimumHeight = window.MinHeight;
        _readWorkArea = readWorkArea ?? ReadCurrentWorkArea;
        window.SourceInitialized += SourceInitialized;
        window.Loaded += WindowLoaded;
        window.IsVisibleChanged += VisibilityChanged;
        window.StateChanged += StateChanged;
        window.Closed += WindowClosed;
        AttachSource();
    }

    // 坐标和尺寸均为物理像素；不直接将多屏绝对坐标除以当前屏幕 DPI。
    internal static Rect Fit(Rect bounds, Rect workArea)
    {
        if (!Valid(bounds) || !Valid(workArea)) return bounds;
        var width = Math.Min(bounds.Width, workArea.Width);
        var height = Math.Min(bounds.Height, workArea.Height);
        return new Rect(Math.Clamp(bounds.Left, workArea.Left, workArea.Right - width),
            Math.Clamp(bounds.Top, workArea.Top, workArea.Bottom - height), width, height);
    }

    internal void Refresh()
    {
        if (_disposed || !_window.IsVisible) return;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero) return;
        var area = _readWorkArea();
        if (!Valid(area)) return;
        var dpi = VisualTreeHelper.GetDpi(_window);
        _window.MinWidth = Math.Min(_minimumWidth, area.Width / dpi.DpiScaleX);
        _window.MinHeight = Math.Min(_minimumHeight, area.Height / dpi.DpiScaleY);
        // 最大化、最小化和贴靠过程交给系统；普通窗口仅在操作结束后收回。
        if (_interactiveMove || _window.WindowState != WindowState.Normal || !GetWindowRect(handle, out var native)) return;
        var bounds = new Rect(native.Left, native.Top, native.Right - native.Left, native.Bottom - native.Top);
        var fit = Fit(bounds, area);
        if (fit == bounds) return;
        _ = SetWindowPos(handle, IntPtr.Zero, (int)fit.Left, (int)fit.Top,
            (int)fit.Width, (int)fit.Height, 0x0004 | 0x0010); // 保留层级且不抢占焦点。
    }

    private Rect ReadCurrentWorkArea()
    {
        var area = Forms.Screen.FromHandle(new WindowInteropHelper(_window).Handle).WorkingArea;
        return new Rect(area.Left, area.Top, area.Width, area.Height);
    }

    private static bool Valid(Rect rect) => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0
        && double.IsFinite(rect.Left) && double.IsFinite(rect.Top)
        && double.IsFinite(rect.Right) && double.IsFinite(rect.Bottom);

    private void AttachSource()
    {
        if (_source is not null) return;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero) return;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessage);
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0231) _interactiveMove = true;
        if (message == 0x0232) _interactiveMove = false;
        // 等 WPF 完成 DPI 和窗口状态更新后合并检查，避免和系统消息处理争用位置。
        if (message is 0x02E0 or 0x007E or 0x001A or 0x0232) QueueRefresh();
        return IntPtr.Zero;
    }

    private void QueueRefresh()
    {
        if (_disposed || _pending?.Status == DispatcherOperationStatus.Pending) return;
        _pending = _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Refresh));
    }

    private void SourceInitialized(object? sender, EventArgs e) { AttachSource(); QueueRefresh(); }
    private void WindowLoaded(object sender, RoutedEventArgs e) => QueueRefresh();
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => QueueRefresh();
    private void StateChanged(object? sender, EventArgs e) => QueueRefresh();
    private void WindowClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pending?.Abort();
        _source?.RemoveHook(WindowMessage);
        _source = null;
        _window.SourceInitialized -= SourceInitialized;
        _window.Loaded -= WindowLoaded;
        _window.IsVisibleChanged -= VisibilityChanged;
        _window.StateChanged -= StateChanged;
        _window.Closed -= WindowClosed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
