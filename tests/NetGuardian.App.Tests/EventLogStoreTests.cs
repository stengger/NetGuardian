using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class EventLogStoreTests
{
    [Fact]
    public void 搜索与级别来源同时生效且不根据文字猜测级别()
    {
        var store = new EventLogStore(new Localizer(), 3);
        var info = store.Add("这段文字包含失败，但调用者明确标为信息", UiLogLevel.Information, UiLogSource.Interface);
        var error = store.Add("DNS\nACCESS DENIED", UiLogLevel.Error, UiLogSource.Network);
        Assert.True(error.Matches("access denied", UiLogLevel.Error, UiLogSource.Network));
        Assert.False(error.Matches("dns", UiLogLevel.Warning, UiLogSource.Network));
        Assert.False(error.Matches("dns", UiLogLevel.Error, UiLogSource.Monitor));
        Assert.False(info.Matches("失败", UiLogLevel.Error, null));
        Assert.True(info.Matches("失败", null, null));
        Assert.Contains("\n", error.Message);
    }

    [Fact]
    public void 有界保留最新记录且语言变化只更新元数据()
    {
        var localizer = new Localizer();
        var store = new EventLogStore(localizer, 2);
        var old = store.Add("旧记录", UiLogLevel.Information, UiLogSource.Interface);
        store.Add("保留记录", UiLogLevel.Warning, UiLogSource.Monitor);
        var latest = store.Add("原始错误不能被翻译或改写", UiLogLevel.Error, UiLogSource.Network);
        Assert.Equal(2, store.Entries.Count);
        Assert.DoesNotContain(old, store.Entries);
        var notified = new List<string?>();
        latest.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        localizer.SetLanguage("en-US");
        store.RefreshLanguage();
        Assert.Equal(localizer.T("LogLevelError"), latest.LevelText);
        Assert.Equal("原始错误不能被翻译或改写", latest.Message);
        Assert.Contains(nameof(UiLogEntry.LevelText), notified);
        Assert.Contains(nameof(UiLogEntry.SourceText), notified);
    }
}
