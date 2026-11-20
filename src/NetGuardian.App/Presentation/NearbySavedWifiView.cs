using System.Collections;
using System.Collections.Specialized;
using System.Collections.Generic;
using System;
using System.Windows.Data;
using System.Windows.Controls;
using NetGuardian.Core.Models;

namespace NetGuardian.App.Presentation;

internal sealed class NearbySavedWifiView
{
    private HashSet<string> _nearby = new(StringComparer.Ordinal);
    public ListCollectionView View { get; }
    public bool ShowsAll { get; private set; }

    public NearbySavedWifiView(IList profiles)
    {
        View = new ListCollectionView(profiles);
        View.Filter = item => item is WifiProfile profile && (ShowsAll || (profile.CredentialSaved && _nearby.Contains(profile.Ssid)));
    }

    public void BindPriorityColumn(DataGridTextColumn column)
    {
        void RefreshBinding() => column.Binding = new Binding { Converter = new NearbyPriorityIndexConverter(this), Mode = BindingMode.OneWay };
        // 扫描和移动都会刷新视图；更新绑定使已复用的单元格也重新计算序号。
        ((INotifyCollectionChanged)View).CollectionChanged += (_, _) => RefreshBinding();
        RefreshBinding();
    }
    // 当前附近列表的序号仅用于展示，不写入保存配置的全局恢复优先级。
    public int? GetPriority(WifiProfile? profile)
    {
        var index = profile is null ? -1 : View.IndexOf(profile);
        return index < 0 ? null : index + 1;
    }

    public void SetShowAll(bool showAll)
    {
        if (ShowsAll == showAll) return;
        ShowsAll = showAll;
        View.Refresh();
    }

    public void Update(IEnumerable<string> nearbySsids)
    {
        _nearby = new(nearbySsids, StringComparer.Ordinal);
        View.Refresh();
    }
}
