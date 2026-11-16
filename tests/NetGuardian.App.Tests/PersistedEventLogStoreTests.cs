using System.IO;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class PersistedEventLogStoreTests
{
    [Fact]
    public void 导入历史按原始时间排序且同一文件位置更新不重复()
    {
        var now = DateTimeOffset.Now;
        var store = new EventLogStore(new Localizer());
        var recent = new PersistedLogEntry("new", now.AddHours(-1), "新记录", UiLogLevel.Information, UiLogSource.Background);
        var old = recent with { Id = "old", Time = now.AddDays(-1), Message = "旧记录" };
        store.Import(new[] { recent, old }, now);
        var oldItem = store.Entries[0];
        var lastItem = store.Entries[1];
        store.Import(new[] { recent with { Message = "新记录\n补充详情" } }, now);
        Assert.Equal(2, store.Entries.Count);
        Assert.Same(oldItem, store.Entries[0]);
        Assert.Same(lastItem, store.Entries[1]);
        Assert.Contains("补充详情", lastItem.Message);
        Assert.Equal(old.Time, oldItem.Time);
    }

    [Fact]
    public void 磁盘回读当前窗口已经显示的记录时合并且保留真实重复操作()
    {
        var store = new EventLogStore(new Localizer());
        var first = store.Add("同一条界面消息", UiLogLevel.Information, UiLogSource.Interface);
        var second = store.Add("同一条界面消息", UiLogLevel.Information, UiLogSource.Interface);
        var now = DateTimeOffset.Now;
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(first.Time.ToUnixTimeSeconds());
        store.Import(new[]
        {
            new PersistedLogEntry("offset-1", timestamp, first.Message, first.Level, first.Source),
            new PersistedLogEntry("offset-2", timestamp, second.Message, second.Level, second.Source)
        }, now);
        store.Import(new[] { new PersistedLogEntry("offset-1", timestamp, first.Message, first.Level, first.Source) }, now);
        Assert.Equal(2, store.Entries.Count);
        Assert.Contains(first, store.Entries);
        Assert.Contains(second, store.Entries);
        Assert.Equal(2, store.Entries.Select(entry => entry.PersistedId).Distinct().Count());
    }

    [Fact]
    public void 有界容量优先保留断网修复记录但过期事件仍会移除()
    {
        var now = DateTimeOffset.Now;
        var store = new EventLogStore(new Localizer(), 3);
        var recovery = new PersistedLogEntry("r", now.AddDays(-6), "事件原文", UiLogLevel.Warning,
            UiLogSource.Recovery, "检测到断网", true);
        store.Import(new[] { recovery }, now);
        for (var i = 0; i < 20; i++) store.Add("检测正常 " + i, UiLogLevel.Information, UiLogSource.Background);
        Assert.Equal(3, store.Entries.Count);
        Assert.Contains(store.Entries, entry => entry.IsRecoveryEvent);
        Assert.Contains(store.Entries, entry => entry.Message == "检测正常 19");
        Assert.True(store.Entries.Single(entry => entry.IsRecoveryEvent).Matches("断网", null, UiLogSource.Recovery));
        store.Import(Array.Empty<PersistedLogEntry>(), now.AddDays(2));
        Assert.DoesNotContain(store.Entries, entry => entry.IsRecoveryEvent);
    }

    [Fact]
    public void 读取实际写盘格式并增量合并后条数与关键事件不丢失()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NetGuardian-log-store-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var now = DateTimeOffset.Now;
            var store = new EventLogStore(new Localizer());
            var local = store.Add("正在连接测试网络", UiLogLevel.Information, UiLogSource.Interface);
            using (var sink = new NetGuardian.Core.Logging.FileLogSink(directory))
                sink.Write("[Information/Interface] " + local.Message);
            var journal = new NetGuardian.Core.Logging.EventJournal(directory);
            journal.RecordCheck(NetGuardian.Core.Models.NetworkState.InternetIssue, false);
            journal.RecordRecoveryStarted(2);
            journal.RecordRecoveryResult(true, "测试网络");
            var reader = new PersistedEventLogReader(new[] { directory });
            var batch = reader.Read();
            store.Import(batch.Entries, DateTimeOffset.Now);
            Assert.Empty(batch.Errors);
            Assert.Equal(4, store.Entries.Count);
            Assert.Equal(3, store.Entries.Count(entry => entry.IsRecoveryEvent));
            Assert.Contains(local, store.Entries);
            journal.RecordCheck(NetGuardian.Core.Models.NetworkState.Healthy, true);
            store.Import(reader.Read().Entries, DateTimeOffset.Now);
            Assert.Equal(5, store.Entries.Count);
            Assert.Empty(reader.Read().Entries);
        }
        finally { Directory.Delete(directory, true); }
    }
}
