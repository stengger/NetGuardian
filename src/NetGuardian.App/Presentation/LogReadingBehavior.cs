using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace NetGuardian.App.Presentation;

internal static class LogReadingBehavior
{
    public static void Attach(DataGrid grid, CheckBox follow)
    {
        // 正文已接管边界滚轮时，也要保留向上阅读暂停跟随的用户意图。
        grid.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, args) =>
        {
            if (args.Delta > 0) follow.IsChecked = false;
        }), true);
        grid.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler((sender, e) =>
        {
            if (e.OriginalSource is ScrollBar { Orientation: Orientation.Vertical }
                && e.ScrollEventType != ScrollEventType.EndScroll)
                follow.IsChecked = false;
        }), true);
    }
}
