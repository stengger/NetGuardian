using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace NetGuardian.App.Presentation;

public static class ResultTableInteraction
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ResultTableInteraction), new PropertyMetadata(false, EnabledChanged));
    public static readonly DependencyProperty DeferSingleClickProperty = DependencyProperty.RegisterAttached(
        "DeferSingleClick", typeof(bool), typeof(ResultTableInteraction), new PropertyMetadata(false));
    private static readonly ConditionalWeakTable<DataGrid, Gesture> Gestures = new();
    private static readonly ConditionalWeakTable<DataGridRow, Transition> Transitions = new();

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    public static bool GetDeferSingleClick(DependencyObject target) => (bool)target.GetValue(DeferSingleClickProperty);
    public static void SetDeferSingleClick(DependencyObject target, bool value) => target.SetValue(DeferSingleClickProperty, value);

    private sealed class Gesture
    {
        public DataGridRow? Row;
        public Point Start;
        public DispatcherTimer? Pending;
    }
    private sealed class Transition { public int Version; public double Height; }

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not DataGrid grid) return;
        grid.PreviewMouseDown -= Down; grid.PreviewMouseUp -= Up; grid.PreviewMouseMove -= Move;
        grid.PreviewKeyDown -= Key; grid.RowDetailsVisibilityChanged -= DetailsChanged; grid.Unloaded -= Unloaded;
        if (!(bool)args.NewValue) return;
        grid.PreviewMouseDown += Down; grid.PreviewMouseUp += Up; grid.PreviewMouseMove += Move;
        grid.PreviewKeyDown += Key; grid.RowDetailsVisibilityChanged += DetailsChanged; grid.Unloaded += Unloaded;
    }

    internal static T? Ancestor<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = Parent(current))
            if (current is T match) return match;
        return null;
    }

    private static DependencyObject? Parent(DependencyObject value) => value is Visual or Visual3D
        ? VisualTreeHelper.GetParent(value) : value is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(value);

    private static DataGridRow? PlainRow(DependencyObject? source)
    {
        for (var current = source; current is not null; current = Parent(current))
        {
            if (current is ButtonBase or TextBoxBase or PasswordBox or ScrollBar or Thumb or Hyperlink or DataGridDetailsPresenter or DataGridRowHeader)
                return null;
            if (current is FrameworkElement { Cursor: { } cursor } && cursor == Cursors.SizeAll) return null;
            if (current is DataGridRow row) return row;
        }
        return null;
    }

    private static void Down(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        var grid = (DataGrid)sender;
        var gesture = Gestures.GetOrCreateValue(grid);
        gesture.Pending?.Stop();
        gesture.Row = args.ClickCount > 1 ? null : PlainRow(args.OriginalSource as DependencyObject);
        gesture.Start = args.GetPosition(grid);
    }

    private static void Move(object sender, MouseEventArgs args)
    {
        var grid = (DataGrid)sender;
        var gesture = Gestures.GetOrCreateValue(grid);
        var position = args.GetPosition(grid);
        if (Math.Abs(position.X - gesture.Start.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(position.Y - gesture.Start.Y) > SystemParameters.MinimumVerticalDragDistance)
            gesture.Row = null;
    }

    private static void Up(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        var grid = (DataGrid)sender;
        var gesture = Gestures.GetOrCreateValue(grid);
        var row = gesture.Row;
        gesture.Row = null;
        if (row is null || !ReferenceEquals(row, PlainRow(args.OriginalSource as DependencyObject))) return;
        if (!GetDeferSingleClick(grid)) { Toggle(row); return; }
        // Wi-Fi 保留原有双击编辑：第二次按下会取消尚未执行的单击。
        var item = row.Item;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            gesture.Pending = null;
            if (row.IsLoaded && ReferenceEquals(row.Item, item)) Toggle(row);
        };
        gesture.Pending = timer;
        timer.Start();
    }

    private static void Key(object sender, KeyEventArgs args)
    {
        if (args.Key is not (System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) || args.KeyboardDevice.Modifiers != ModifierKeys.None) return;
        var source = args.OriginalSource as DependencyObject;
        if (Ancestor<DataGridRowHeader>(source) is not null && Ancestor<ToggleButton>(source) is { } button)
        {
            if (args.Key == System.Windows.Input.Key.Enter) { button.IsChecked = button.IsChecked != true; args.Handled = true; }
            return;
        }
        if (PlainRow(source) is not { } row) return;
        Toggle(row);
        args.Handled = true;
    }

    private static void Toggle(DataGridRow row) => row.DetailsVisibility = row.DetailsVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private static void Unloaded(object sender, RoutedEventArgs args)
    {
        var gesture = Gestures.GetOrCreateValue((DataGrid)sender);
        gesture.Pending?.Stop(); gesture.Pending = null; gesture.Row = null;
    }

    private static void DetailsChanged(object? sender, DataGridRowDetailsEventArgs args)
    {
        var row = args.Row;
        var transition = Transitions.GetOrCreateValue(row);
        var version = ++transition.Version;
        row.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (version != transition.Version || !row.IsLoaded) return;
            var details = args.DetailsElement as FrameworkElement;
            var presenter = Ancestor<DataGridDetailsPresenter>(details);
            if (presenter is null || details is null) return;
            var expanded = row.DetailsVisibility == Visibility.Visible;
            var from = presenter.HasAnimatedProperties ? presenter.Height : expanded ? 0 : transition.Height;
            presenter.BeginAnimation(FrameworkElement.HeightProperty, null);
            presenter.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Visible);
            details.Measure(new Size(Math.Max(0, ((DataGrid)sender!).ActualWidth - ((DataGrid)sender!).RowHeaderWidth - 24), double.PositiveInfinity));
            var target = expanded ? details.DesiredSize.Height : 0;
            if (expanded) transition.Height = target;
            var animation = new DoubleAnimation(double.IsNaN(from) ? 0 : from, target, TimeSpan.FromMilliseconds(150))
            { FillBehavior = FillBehavior.Stop, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            presenter.Height = target;
            animation.Completed += (_, _) =>
            {
                if (version != transition.Version) return;
                presenter.BeginAnimation(FrameworkElement.HeightProperty, null);
                presenter.Height = double.NaN;
                presenter.SetCurrentValue(UIElement.VisibilityProperty, row.DetailsVisibility);
            };
            presenter.BeginAnimation(FrameworkElement.HeightProperty, animation);
        }));
    }
}
