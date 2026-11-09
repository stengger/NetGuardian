using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace NetGuardian.App.Presentation;

// 直接复用表格列的绑定；详情只负责展示，不向业务模型增加字段。
public sealed class TableRowDetails : Grid
{
    private DataGrid? owner;

    public TableRowDetails()
    {
        Margin = new Thickness(12, 8, 12, 12);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120), MaxWidth = 120 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Loaded += (_, _) =>
        {
            owner = ResultTableInteraction.Ancestor<DataGrid>(this);
            if (owner is null) return;
            owner.SizeChanged += Resize;
            Build();
            Resize(this, null!);
        };
        Unloaded += (_, _) =>
        {
            if (owner is not null) owner.SizeChanged -= Resize;
            owner = null;
        };
        DataContextChanged += (_, _) => { if (IsLoaded) Build(); };
    }

    private void Resize(object sender, SizeChangedEventArgs args)
    {
        if (owner is null) return;
        // 为行头、边框和可能出现的纵向滚动条预留空间，长文本不参与列宽扩张。
        Width = Math.Max(0, owner.ActualWidth - owner.RowHeaderWidth - SystemParameters.VerticalScrollBarWidth - Margin.Left - Margin.Right - 4);
    }

    private void Build()
    {
        Children.Clear();
        RowDefinitions.Clear();
        if (owner is null || DataContext is null) return;
        foreach (var column in owner.Columns.OrderBy(column => column.DisplayIndex))
        {
            var original = (column is DataGridBoundColumn bound ? bound.Binding : column.ClipboardContentBinding) as Binding;
            if (string.IsNullOrWhiteSpace(original?.Path?.Path)) continue;
            var index = RowDefinitions.Count;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 12, 6) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            label.SetBinding(TextBlock.TextProperty, new Binding(nameof(column.Header)) { Source = column });
            var value = new TextBox
            {
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(0, 6, 0, 6),
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
            SetRow(label, index); SetRow(value, index); SetColumn(value, 1);
            Children.Add(label); Children.Add(value);
        }
    }
}
