using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace NetGuardian.App.Presentation;

// 应用业务弹窗的共同外壳；系统选择器、通知和异常兜底不经过此窗口。
public class FluentDialogWindow : FluentWindow
{
    protected ScrollViewer Body { get; }
    protected WrapPanel Actions { get; }

    public FluentDialogWindow()
    {
        SetResourceReference(WidthProperty, "DialogWidth");
        SetResourceReference(MinWidthProperty, "DialogMinWidth");
        SetResourceReference(MinHeightProperty, "DialogMinHeight");
        SetResourceReference(FontFamilyProperty, "AppFontFamily");
        SetResourceReference(FontSizeProperty, "FontBody");
        SetResourceReference(BackgroundProperty, "SurfaceCardBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ExtendsContentIntoTitleBar = true;
        WindowCornerPreference = WindowCornerPreference.Round;
        WindowBackdropType = WindowBackdropType.None;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Icon = new BitmapImage(new Uri("pack://application:,,,/NetGuardian;component/Assets/app.ico"));

        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var titleBar = new TitleBar
        {
            Name = "DialogTitleBar", ShowMinimize = false, ShowMaximize = false, ForceShutdown = false,
            Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/NetGuardian;component/Assets/app-icon-256.png")) }
        };
        titleBar.SetBinding(TitleBar.TitleProperty, new Binding(nameof(Title)) { Source = this });
        shell.Children.Add(titleBar);
        Body = new ScrollViewer
        {
            Name = "DialogBody", VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false
        };
        Body.SetResourceReference(MarginProperty, "SpacingDialogBody");
        Grid.SetRow(Body, 1);
        shell.Children.Add(Body);
        Actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        Actions.SetResourceReference(MarginProperty, "SpacingDialogActions");
        Grid.SetRow(Actions, 2);
        shell.Children.Add(Actions);
        Content = shell;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // FluentWindow 关闭背景特效时会重置背景，随后重新绑定应用语义资源。
        SetResourceReference(BackgroundProperty, "SurfaceCardBrush");
        UpdateWorkArea();
        if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is { } source)
            source.AddHook(WindowMessage);
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x02E0 or 0x007E or 0x001A)
            Dispatcher.BeginInvoke(new Action(UpdateWorkArea));
        return IntPtr.Zero;
    }

    private void UpdateWorkArea()
    {
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(Owner ?? this).Handle);
        var dpi = VisualTreeHelper.GetDpi(Owner ?? this);
        MaxHeight = screen.WorkingArea.Height / dpi.DpiScaleY;
        MaxWidth = screen.WorkingArea.Width / dpi.DpiScaleX;
        MinHeight = Math.Min((double)FindResource("DialogMinHeight"), MaxHeight);
        MinWidth = Math.Min((double)FindResource("DialogMinWidth"), MaxWidth);
    }
}
