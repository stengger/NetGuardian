using System.Windows;
using System.Windows.Controls;

namespace NetGuardian.App.Views;

// 标题和工具分槽，展开按钮由分区模板负责，输入控件不嵌在展开按钮内。
public partial class ListSectionHeader : UserControl
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(nameof(Heading), typeof(object), typeof(ListSectionHeader));
    public static readonly DependencyProperty ToolsProperty = DependencyProperty.Register(nameof(Tools), typeof(object), typeof(ListSectionHeader));
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(ListSectionHeader), new PropertyMetadata(true));
    public object? Heading { get => GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    public object? Tools { get => GetValue(ToolsProperty); set => SetValue(ToolsProperty, value); }
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public ListSectionHeader() => InitializeComponent();
}
