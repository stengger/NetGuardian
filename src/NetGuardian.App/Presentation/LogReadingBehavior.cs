using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace NetGuardian.App.Presentation;

internal static class LogReadingBehavior
{
    public static void Attach(DataGrid grid, CheckBox follow)
    {
        grid.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler((sender, e) =>
        {
            if (e.OriginalSource is ScrollBar { Orientation: Orientation.Vertical }
                && e.ScrollEventType != ScrollEventType.EndScroll)
                follow.IsChecked = false;
        }), true);
    }
}
