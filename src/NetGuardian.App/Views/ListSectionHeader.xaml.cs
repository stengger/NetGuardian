using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace NetGuardian.App.Views;

// 标题和工具分槽，展开按钮由分区模板负责，输入控件不嵌在展开按钮内。
public class ListSectionHeader : Control
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(nameof(Heading), typeof(object), typeof(ListSectionHeader), new PropertyMetadata(null, OnSlotChanged));
    public static readonly DependencyProperty ToolsProperty = DependencyProperty.Register(nameof(Tools), typeof(object), typeof(ListSectionHeader), new PropertyMetadata(null, OnSlotChanged));
    public static readonly DependencyProperty AuxiliaryProperty = DependencyProperty.Register(nameof(Auxiliary), typeof(object), typeof(ListSectionHeader), new PropertyMetadata(null, OnSlotChanged));
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(ListSectionHeader), new PropertyMetadata(true));
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(ListSectionHeader), new PropertyMetadata(false));
    public object? Heading { get => GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    public object? Tools { get => GetValue(ToolsProperty); set => SetValue(ToolsProperty, value); }
    public object? Auxiliary { get => GetValue(AuxiliaryProperty); set => SetValue(AuxiliaryProperty, value); }
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    public ListSectionHeader() => SetResourceReference(StyleProperty, typeof(ListSectionHeader));

    // 隐藏页面尚未生成视觉模板时，内容也必须能查找窗口命名控件并继承资源。
    protected override IEnumerator LogicalChildren
    {
        get
        {
            if (Heading is not null) yield return Heading;
            if (Tools is not null) yield return Tools;
            if (Auxiliary is not null) yield return Auxiliary;
        }
    }

    private static void OnSlotChanged(DependencyObject source, DependencyPropertyChangedEventArgs change)
    {
        var header = (ListSectionHeader)source;
        if (change.OldValue is not null) header.RemoveLogicalChild(change.OldValue);
        if (change.NewValue is not null) header.AddLogicalChild(change.NewValue);
    }
}
