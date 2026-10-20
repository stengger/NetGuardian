using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using NetGuardian.App.Views;

namespace NetGuardian.App.Presentation;

internal sealed class TrayMenuHost : IDisposable
{
    private readonly Window _owner;
    private readonly TrayContextMenu _menu;
    private bool _disposed;

    public TrayMenuHost(Window owner, TrayContextMenu menu)
    {
        _owner = owner;
        _menu = menu;
        menu.PlacementTarget = owner;
        menu.Opened += MenuOpened;
        menu.Closed += MenuClosed;
    }

    public void ShowAtCursor()
    {
        if (_disposed) return;
        _menu.IsOpen = false;
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    private void MenuOpened(object sender, RoutedEventArgs e)
    {
        if (PresentationSource.FromVisual(_menu) is HwndSource source)
        {
            // 聚焦菜单自己的弹出窗口，不必显示隐藏在托盘中的主窗口。
            _ = SetForegroundWindow(source.Handle);
            _menu.GetCommand("ShowWindow").Focus();
        }
    }

    private void MenuClosed(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(_owner).Handle;
        if (handle != IntPtr.Zero) _ = PostMessage(handle, 0, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _menu.IsOpen = false;
        _menu.Opened -= MenuOpened;
        _menu.Closed -= MenuClosed;
        _menu.PlacementTarget = null;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
