using System.Runtime.ExceptionServices;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetGuardian.App.Views;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Network;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class SpeedTestViewTests
{
    [Fact]
    public void 滚动查看说明时取消按钮仍在可见区域()
    {
        RunSta(() =>
        {
            var view = CreateView();
            var session = new SpeedTestSession();
            session.Begin();
            view.Update(session, new Localizer(), TimeSpan.FromSeconds(1));
            view.Measure(new Size(520, 400));
            view.Arrange(new Rect(0, 0, 520, 400));
            view.UpdateLayout();
            var scroll = FindScroll(view)!;
            scroll.ScrollToBottom();
            view.UpdateLayout();
            Assert.True(scroll.VerticalOffset > 0);
            var cancel = (Button)view.FindName("CancelButton");
            var bounds = cancel.TransformToAncestor(view).TransformBounds(new Rect(cancel.RenderSize));
            Assert.True(bounds.Top >= 0 && bounds.Bottom <= 400, $"取消操作滚出了视口：{bounds}");
        });
    }

    private static ScrollViewer? FindScroll(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var found = FindScroll(VisualTreeHelper.GetChild(parent, index));
            if (found is not null) return found;
        }
        return null;
    }

    [Fact]
    public void 首次运行与完成状态使用准确文案且切语言不丢失结果()
    {
        RunSta(() =>
        {
            var view = CreateView();
            var session = new SpeedTestSession();
            var localizer = new Localizer();
            view.Update(session, localizer, TimeSpan.Zero);
            Layout(view, 886);
            Assert.Equal("—", ((TextBlock)view.FindName("DownloadValue")).Text);
            Assert.False(((Button)view.FindName("CopyButton")).IsEnabled);
            SavePreview(view, "speed-ready.png");
            var id = session.Begin();
            session.Complete(id, new(128.4, 24.8, 92, 5_000_000, 1_000_000, DateTimeOffset.Now, 2));
            view.Update(session, localizer, TimeSpan.FromSeconds(7));
            Layout(view, 886);
            Assert.Contains("128.4 Mbps", view.ResultSummary);
            Assert.Contains("有效响应样本 2/2", view.ResultSummary);
            SavePreview(view, "speed-complete.png");
            localizer.SetLanguage("en-US");
            view.Update(session, localizer, TimeSpan.FromSeconds(7));
            Assert.Equal("Test complete", ((TextBlock)view.FindName("StatusText")).Text);
            Assert.Contains("128.4 Mbps", view.ResultSummary);
            Assert.Contains("Website response time", view.ResultSummary);
        });
    }

    [Theory]
    [InlineData(520, "zh-CN")]
    [InlineData(746, "zh-CN")]
    [InlineData(886, "en-US")]
    [InlineData(746, "en-US", true)]
    public void 长数字与操作在窄布局中完整可见(double width, string language, bool light = false)
    {
        RunSta(() =>
        {
            var view = CreateView(light);
            var localizer = new Localizer();
            localizer.SetLanguage(language);
            var session = new SpeedTestSession();
            var id = session.Begin();
            session.Complete(id, new(12345.6, 9876.5, 1234, 5_000_000, 1_000_000, DateTimeOffset.Now, 2));
            view.Update(session, localizer, TimeSpan.FromSeconds(8));
            Layout(view, width);
            foreach (var name in new[] { "DownloadValue", "UploadValue", "ResponseValue", "RunButton", "CopyButton" })
            {
                var element = (FrameworkElement)view.FindName(name);
                Assert.True(element.ActualWidth > 0, name);
                var bounds = element.TransformToAncestor(view).TransformBounds(new Rect(element.RenderSize));
                Assert.True(bounds.Left >= -1 && bounds.Right <= width + 1, $"{name} 超出可见宽度：{bounds}");
                if (element is TextBlock text)
                {
                    var required = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                        text.FontSize, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
                    Assert.True(text.ActualWidth + 1 >= required, $"{name} 的数字被裁切");
                }
            }
            Assert.True(((Button)view.FindName("CopyButton")).IsEnabled);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting((DependencyObject)view.FindName("StatusText")));
            SavePreview(view, $"speed-{width}-{language}{(light ? "-light" : "")}.png");
        });
    }

    [Fact]
    public void 上传阶段可取消且失败后不把未完成数值复制为结果()
    {
        RunSta(() =>
        {
            var view = CreateView();
            var session = new SpeedTestSession();
            var localizer = new Localizer();
            var id = session.Begin();
            session.Apply(id, new("Download", 80, null, true));
            session.Apply(id, new("Upload", 12, null));
            view.Update(session, localizer, TimeSpan.FromSeconds(5));
            Layout(view, 886);
            Assert.Equal(Visibility.Visible, ((Button)view.FindName("CancelButton")).Visibility);
            Assert.False(((Button)view.FindName("RunButton")).IsEnabled);
            SavePreview(view, "speed-running.png");
            session.Fail(id, "测试用连接中断");
            view.Update(session, localizer, TimeSpan.FromSeconds(6));
            Assert.Equal("—", ((TextBlock)view.FindName("UploadValue")).Text);
            Assert.Contains("80.0 Mbps", view.ResultSummary);
            Assert.DoesNotContain("12.0 Mbps", view.ResultSummary);
            Assert.Contains("部分完成", ((TextBlock)view.FindName("StatusText")).Text);
            SavePreview(view, "speed-partial.png");
        });
    }

    private static SpeedTestView CreateView(bool light = false)
    {
        var view = new SpeedTestView();
        // 使用实际主题资源，隔离主窗口启动、网络监控和配置写入。
        view.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = light ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark });
        view.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/NetGuardian;component/Themes/Tokens.Common.xaml") });
        view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/NetGuardian;component/Themes/Palette.{(light ? "Light" : "Dark")}.xaml") });
        view.Background = new SolidColorBrush(light ? Color.FromRgb(243, 243, 243) : Color.FromRgb(32, 32, 32));
        return view;
    }

    private static void Layout(FrameworkElement view, double width)
    {
        view.Measure(new Size(width, 720));
        view.Arrange(new Rect(0, 0, width, 720));
        view.UpdateLayout();
    }

    private static void SavePreview(FrameworkElement view, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("NETGUARDIAN_PREVIEW_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Layout(view, view.ActualWidth);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, fileName));
        encoder.Save(output);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "测速视图布局验证超时。");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
