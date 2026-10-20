using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Wpf.Ui.Controls;

namespace NetGuardian.App.Presentation;

// 保留 NavigationView 的视觉与键盘行为，只为现有面板维护选择，不实例化 Page。
public sealed class PanelNavigationView : NavigationView
{
    public PanelNavigationView()
    {
        SetResourceReference(StyleProperty, typeof(NavigationView));
        Loaded += (_, _) =>
        {
            foreach (var item in MenuItems.Cast<object>().Concat(FooterMenuItems.Cast<object>()).OfType<PanelNavigationItem>())
                item.PanelOwner = this;
        };
    }

    public bool SelectPanel(string tag)
    {
        var item = MenuItems.Cast<object>().Concat(FooterMenuItems.Cast<object>())
            .OfType<PanelNavigationItem>().FirstOrDefault(candidate => Equals(candidate.Tag, tag));
        return item is not null && SelectItem(item);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // 业务面板位于主窗口中；不显示库为 Page 导航预留的空 Frame。
        if (NavigationViewContentPresenter is not null) NavigationViewContentPresenter.Visibility = Visibility.Collapsed;
    }

    internal bool SelectItem(PanelNavigationItem item)
    {
        if (!item.IsEnabled || (!MenuItems.Contains(item) && !FooterMenuItems.Contains(item))) return false;
        item.PanelOwner = this;
        if (ReferenceEquals(SelectedItem, item)) return true;
        var previous = SelectedItem as PanelNavigationItem;
        previous?.Deactivate(this);
        SelectedItem = item;
        item.Activate(this);
        previous?.NotifySelection(false);
        item.NotifySelection(true);
        OnSelectionChanged();
        return true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PanelNavigationPeer(this);

    private sealed class PanelNavigationPeer(PanelNavigationView navigation) : FrameworkElementAutomationPeer(navigation), ISelectionProvider
    {
        protected override string GetClassNameCore() => nameof(PanelNavigationView);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tab;
        protected override List<AutomationPeer> GetChildrenCore()
        {
            // 库的 ItemsControl 包装器会替换项目 peer；静态面板项须直接归属同一选择容器。
            var children = new List<AutomationPeer>();
            foreach (var child in base.GetChildrenCore() ?? [])
            {
                if (child is ButtonAutomationPeer) children.Add(child);
                else if (child is ScrollViewerAutomationPeer)
                    children.AddRange((child.GetChildren() ?? []).Where(peer => peer is ScrollBarAutomationPeer));
            }
            foreach (var item in navigation.MenuItems.Cast<object>().Concat(navigation.FooterMenuItems.Cast<object>()))
                if (item is PanelNavigationItem or System.Windows.Controls.GroupBox
                    && CreatePeerForElement((UIElement)item) is { } peer) children.Add(peer);
            return children;
        }
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Selection ? this : base.GetPattern(patternInterface);
        public bool CanSelectMultiple => false;
        public bool IsSelectionRequired => true;
        public IRawElementProviderSimple[] GetSelection() => navigation.SelectedItem is UIElement selected
            && CreatePeerForElement(selected) is { } peer && ProviderFromPeer(peer) is { } provider ? [provider] : [];
    }
}

public sealed class PanelNavigationItem : NavigationViewItem
{
    public PanelNavigationItem() => SetResourceReference(StyleProperty, typeof(NavigationViewItem));

    internal PanelNavigationView? PanelOwner { get; set; }

    protected override void OnClick()
    {
        PanelOwner?.SelectItem(this);
        base.OnClick();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PanelNavigationItemPeer(this);

    internal void NotifySelection(bool selected)
    {
        if (UIElementAutomationPeer.FromElement(this) is PanelNavigationItemPeer peer)
            peer.NotifySelection(selected);
    }

    private sealed class PanelNavigationItemPeer(PanelNavigationItem item) : FrameworkElementAutomationPeer(item), ISelectionItemProvider
    {
        protected override string GetClassNameCore() => nameof(PanelNavigationItem);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;
        protected override string GetNameCore() => string.IsNullOrWhiteSpace(base.GetNameCore()) ? item.Content?.ToString() ?? "" : base.GetNameCore();
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.SelectionItem ? this : base.GetPattern(patternInterface);
        public bool IsSelected => ReferenceEquals(item.PanelOwner?.SelectedItem, item);
        public IRawElementProviderSimple? SelectionContainer => item.PanelOwner is { } parent
            && CreatePeerForElement(parent) is { } peer ? ProviderFromPeer(peer) : null;
        public void AddToSelection() => Select();
        public void Select()
        {
            if (!item.IsEnabled) throw new ElementNotEnabledException();
            item.PanelOwner?.SelectItem(item);
        }
        public void RemoveFromSelection()
        {
            if (IsSelected) throw new InvalidOperationException("导航必须保留一个选中的面板。");
        }
        public void NotifySelection(bool selected)
        {
            RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, !selected, selected);
            if (selected) RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);
        }
    }
}
