using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class WindowClosingTests
{
    [Theory]
    [InlineData("exit")]
    [InlineData("tray")]
    [InlineData("ask")]
    public void CloseActionRunsAfterClosingEventReturns(string behavior)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var window = new Window { ShowInTaskbar = false, Width = 100, Height = 100 };
            var closing = false;
            var requested = false;
            var actionRan = false;
            var actionRanDuringClosing = false;
            var closed = false;
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timeout.Tick += (_, _) => frame.Continue = false;
            CancelEventHandler handler = (_, e) =>
            {
                if (actionRan) return;
                closing = true;
                requested = true;
                DeferCloseAction(window, e, () =>
                {
                    actionRan = true;
                    actionRanDuringClosing = closing;
                    if (closing) return;
                    if (behavior == "exit") window.Close();
                    if (behavior == "tray") window.Hide();
                    if (behavior == "ask")
                    {
                        var prompt = new Window { Owner = window, ShowInTaskbar = false, Width = 100, Height = 100 };
                        prompt.Loaded += (_, _) => prompt.Dispatcher.BeginInvoke(new Action(prompt.Close));
                        prompt.ShowDialog();
                    }
                    frame.Continue = false;
                });
                Assert.True(e.Cancel);
                closing = false;
            };
            window.Closing += handler;
            window.Closed += (_, _) => closed = true;
            try
            {
                window.Show();
                // 只发起一次关闭，随后由程序完成所选动作。
                window.Close();
                Assert.True(requested);
                Assert.False(actionRanDuringClosing, "关闭操作必须等原始 Closing 事件返回后执行。");
                Assert.False(actionRan);
                Assert.True(window.IsVisible);
                timeout.Start();
                Dispatcher.PushFrame(frame);
                Assert.True(actionRan, "关闭动作未在预期时间内执行。");
                Assert.Equal(behavior == "exit", closed);
                Assert.Equal(behavior == "ask", window.IsVisible);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                timeout.Stop();
                window.Closing -= handler;
                if (!closed) window.Close();
                window.Dispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "窗口关闭验证超时。");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void DeferCloseAction(Window window, CancelEventArgs e, Action action)
    {
        var method = typeof(MainWindow).GetMethod("DeferCloseAction", BindingFlags.Static | BindingFlags.NonPublic)!;
        method.Invoke(null, [window, e, action]);
    }
}
