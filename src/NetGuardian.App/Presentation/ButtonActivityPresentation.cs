using System;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace NetGuardian.App.Presentation;

// 不改 IsEnabled，避免覆盖原有网络操作互斥及可用性判断。
internal sealed class ButtonActivityPresentation : IDisposable
{
    private readonly System.Windows.Controls.Button _button;
    private readonly object _original;
    private bool _disposed;
    public ButtonActivityPresentation(System.Windows.Controls.Button button, string text)
    {
        _button = button;
        _original = button.Content;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        FrameworkElement icon = SystemParameters.ClientAreaAnimation
            ? new ProgressRing { IsIndeterminate = true }
            : new SymbolIcon { Symbol = SymbolRegular.Wifi124 };
        icon.SetResourceReference(FrameworkElement.WidthProperty, "ActivityIconSize");
        icon.SetResourceReference(FrameworkElement.HeightProperty, "ActivityIconSize");
        icon.SetResourceReference(FrameworkElement.MarginProperty, "SpacingInlineGap");
        icon.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(icon);
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        button.Content = panel;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _button.Content = _original;
    }
}
