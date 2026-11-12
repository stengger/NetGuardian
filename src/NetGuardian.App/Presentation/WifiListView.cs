using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Data;

namespace NetGuardian.App.Presentation;

// 仅改变展示顺序与可见项，源集合及恢复优先级保持不变。
internal sealed class WifiListView(IList source, Func<object, string> name, Func<object, int> signal, Func<object, bool> added)
{
    public ListCollectionView View { get; } = new(source);
    public void Update(string query, bool onlyNew, bool sortByName)
    {
        var text = query.Trim();
        using (View.DeferRefresh())
        {
            View.Filter = item => (!onlyNew || !added(item)) && name(item).Contains(text, StringComparison.CurrentCultureIgnoreCase);
            View.CustomSort = Comparer<object>.Create((a, b) => sortByName
                ? StringComparer.CurrentCultureIgnoreCase.Compare(name(a), name(b))
                : signal(b).CompareTo(signal(a)) is var difference && difference != 0 ? difference
                : StringComparer.CurrentCultureIgnoreCase.Compare(name(a), name(b)));
        }
    }
}
