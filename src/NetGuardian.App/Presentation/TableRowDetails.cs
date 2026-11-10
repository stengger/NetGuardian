using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace NetGuardian.App.Presentation;

// 直接复用表格列的绑定；详情只负责展示，不向业务模型增加字段。
public sealed class TableRowDetails : Grid
{
    private DataGrid? owner;
    private readonly List<(DataGridColumn Column, TextBlock Label, TextBox Value)> fields = new();
    private bool arranging;
    private DispatcherOperation? pendingArrange;
    private static readonly DependencyPropertyDescriptor ColumnWidth = DependencyPropertyDescriptor.FromProperty(
        DataGridColumn.ActualWidthProperty, typeof(DataGridColumn));
    private static readonly DependencyPropertyDescriptor ColumnVisibility = DependencyPropertyDescriptor.FromProperty(
        DataGridColumn.VisibilityProperty, typeof(DataGridColumn));

    public TableRowDetails()
    {
        SetResourceReference(MarginProperty, "SpacingDetailsOuter");
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Loaded += (_, _) =>
        {
            owner = ResultTableInteraction.Ancestor<DataGrid>(this);
            if (owner is null) return;
            owner.SizeChanged += Resize;
            foreach (var column in owner.Columns)
            {
                ColumnWidth.AddValueChanged(column, QueueArrange);
                ColumnVisibility.AddValueChanged(column, QueueArrange);
            }
            Build();
            Resize(this, null!);
        };
        Unloaded += (_, _) =>
        {
            if (owner is not null)
            {
                owner.SizeChanged -= Resize;
                foreach (var column in owner.Columns)
                {
                    ColumnWidth.RemoveValueChanged(column, QueueArrange);
                    ColumnVisibility.RemoveValueChanged(column, QueueArrange);
                }
            }
            pendingArrange?.Abort();
            owner = null;
        };
        DataContextChanged += (_, _) => { if (IsLoaded) Build(); };
    }

    private void Resize(object sender, SizeChangedEventArgs args)
    {
        if (owner is null) return;
        // 为行头、边框和可能出现的纵向滚动条预留空间，长文本不参与列宽扩张。
        Width = Math.Max(0, owner.ActualWidth - owner.RowHeaderWidth - SystemParameters.VerticalScrollBarWidth - Margin.Left - Margin.Right
            - (double)FindResource("DetailMeasureReserve"));
        ArrangeFields();
        QueueArrange(this, EventArgs.Empty);
    }

    private void QueueArrange(object? sender, EventArgs args)
    {
        if (pendingArrange?.Status == DispatcherOperationStatus.Pending) return;
        pendingArrange = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ArrangeFields));
    }

    private void Build()
    {
        Children.Clear();
        fields.Clear();
        RowDefinitions.Clear();
        if (owner is null || DataContext is null) return;
        foreach (var column in owner.Columns.OrderBy(column => column.DisplayIndex))
        {
            var original = (column is DataGridBoundColumn bound ? bound.Binding : column.ClipboardContentBinding) as Binding;
            if (string.IsNullOrWhiteSpace(original?.Path?.Path)) continue;
            var label = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            label.SetBinding(TextBlock.TextProperty, new Binding(nameof(column.Header)) { Source = column });
            var value = new TextBox
            {
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Padding = new Thickness(0),
                MinHeight = 0, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalContentAlignment = VerticalAlignment.Top
            };
            value.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            value.SetBinding(TextBox.TextProperty, new Binding(original.Path.Path)
            {
                Mode = BindingMode.OneWay, Converter = original.Converter, ConverterParameter = original.ConverterParameter,
                ConverterCulture = original.ConverterCulture, StringFormat = original.StringFormat,
                TargetNullValue = original.TargetNullValue, FallbackValue = original.FallbackValue
            });
            value.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(column.Header)) { Source = column });
            value.TextChanged += (_, _) => ArrangeFields();
            fields.Add((column, label, value));
            Children.Add(label); Children.Add(value);
        }
        ArrangeFields();
    }

    private void ArrangeFields()
    {
        if (arranging || owner is null || fields.Count == 0 || !double.IsFinite(Width)) return;
        arranging = true;
        try
        {
            var compact = Width < (double)FindResource("DetailStackBreakpoint");
            var labelLimit = Math.Min(Width, (double)FindResource("DetailLabelMaxWidth"));
            ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            ColumnDefinitions[0].MaxWidth = compact ? double.PositiveInfinity : labelLimit;
            ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            var row = ResultTableInteraction.Ancestor<DataGridRow>(this);
            var overflow = fields.Select(field => IsOverflow(field.Column, field.Value, row)).ToArray();
            var hasOverflow = overflow.Any(value => value);
            RowDefinitions.Clear();
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                // 保留行标识、主行隐藏列和溢出字段；全部短值时仍提供完整的可复制视图。
                var visible = !hasOverflow || index == 0 || overflow[index];
                field.Label.Visibility = field.Value.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (!visible) continue;
                field.Label.MaxWidth = compact ? Width : labelLimit;
                field.Label.SetResourceReference(MarginProperty, compact ? "SpacingDetailStackLabel" : "SpacingDetailLabel");
                field.Value.SetResourceReference(MarginProperty, compact ? "SpacingDetailStackValue" : "SpacingDetailValue");
                var labelRow = RowDefinitions.Count;
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                if (compact) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                SetRow(field.Label, labelRow);
                SetRow(field.Value, compact ? labelRow + 1 : labelRow);
                SetColumn(field.Value, compact ? 0 : 1);
            }
        }
        finally { arranging = false; }
    }

    private static bool IsOverflow(DataGridColumn column, TextBox value, DataGridRow? row)
    {
        if (column.Visibility != Visibility.Visible) return !string.IsNullOrEmpty(value.Text);
        if (value.Text.Contains('\n') || value.Text.Contains('\r')) return true;
        if (row is null || string.IsNullOrEmpty(value.Text)) return false;
        var content = column.GetCellContent(row);
        // 徽标模板的第一个 TextBlock 可能只是状态圆点，不能拿它的宽度衡量整列文字。
        var available = content is TextBlock text ? text.ActualWidth
            : column.ActualWidth - (content?.Margin.Left ?? 0) - (content?.Margin.Right ?? 0);
        if (available <= 0) return false;
        var formatted = new FormattedText(value.Text, value.Language.GetEquivalentCulture(), value.FlowDirection,
            new Typeface(value.FontFamily, value.FontStyle, value.FontWeight, value.FontStretch), value.FontSize,
            value.Foreground, VisualTreeHelper.GetDpi(value).PixelsPerDip);
        // 文本布局按设备像素取整，忽略不足一个物理像素的测量差。
        return formatted.WidthIncludingTrailingWhitespace > available + 1 / VisualTreeHelper.GetDpi(value).DpiScaleX;
    }

}
