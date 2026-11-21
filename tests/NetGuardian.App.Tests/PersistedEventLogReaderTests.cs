using System.IO;
using System.Text;
using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class PersistedEventLogReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NetGuardian-log-tests-" + Guid.NewGuid());
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-11-16T20:00:00+08:00");
    public PersistedEventLogReaderTests() => Directory.CreateDirectory(_directory);
    private string FilePath(string name) => Path.Combine(_directory, name);
    private PersistedEventLogReader Reader(int capacity = 500) => new(new[] { _directory, _directory }, capacity, new FixedTime(_now));
    private void Write(string name, string text) => File.WriteAllText(FilePath(name), text, new UTF8Encoding(false));
    private void Append(string name, string text) => File.AppendAllText(FilePath(name), text, new UTF8Encoding(false));

    [Fact]
    public void 历史切换与重连尝试在大量例行检测后仍可从恢复历史读取()
    {
        Write("app-20261116.log", "2026-11-16 18:30:00 +08:00  RecoverySwitchWifi：办公网络\n" +
            "2026-11-16 18:30:01 +08:00  RecoveryReconnectCurrent：原网络\n" +
            string.Concat(Enumerable.Range(10, 20).Select(i => $"2026-11-16 18:31:{i} +08:00  轻量检测：Healthy，已连通\n")));
        var entries = Reader(4).Read().Entries;
        Assert.Contains(entries, item => item.IsRecoveryEvent && item.DisplayMessage!.Contains("尝试切换") && item.DisplayMessage.Contains("办公网络"));
        Assert.Contains(entries, item => item.IsRecoveryEvent && item.DisplayMessage!.Contains("原网络"));
    }

    [Fact]
    public void 加载历史运行记录保留原始时间级别来源与多行详情()
    {
        Write("app-20261115.log", "2026-11-15 09:01:02 +08:00  [Warning/Network] 第一行\n第二行：中文详情\n2026-11-15 10:00:00 +08:00  RecoveryDnsFlush：\n");
        var reader = Reader();
        var entries = reader.Read().Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-11-15T09:01:02+08:00"), entries[0].Time);
        Assert.Equal(UiLogLevel.Warning, entries[0].Level);
        Assert.Equal(UiLogSource.Network, entries[0].Source);
        Assert.Equal("第一行\n第二行：中文详情", entries[0].Message);
        Assert.Equal(UiLogSource.Background, entries[1].Source);
        Assert.Contains("刷新 DNS", entries[1].DisplayMessage);
        Assert.Empty(reader.Read().Entries);
    }

    [Fact]
    public void 没有运行日志时仍能从事件文件显示断网与完整恢复过程()
    {
        Write("events-202611.jsonl", """
{"ts":"2026-11-15T09:00:00+08:00","type":"check","state":"WifiDisconnected","ok":false}
{"ts":"2026-11-15T09:00:10+08:00","type":"check","state":"WifiDisconnected","ok":false}
{"ts":"2026-11-15T09:00:11+08:00","type":"recovery","action":"started","state":"2"}
{"ts":"2026-11-15T09:00:18+08:00","type":"recovery","action":"connected","ssid":"中文 Wi-Fi"}
{"ts":"2026-11-15T09:00:19+08:00","type":"check","state":"Healthy","ok":true}
{"ts":"2026-11-15T09:02:00+08:00","type":"check","state":"InternetUnreachable","ok":false}
{"ts":"2026-11-15T09:02:20+08:00","type":"recovery","action":"failed"}
""" + "\n");
        var entries = Reader().Read().Entries;
        Assert.Equal(6, entries.Count);
        Assert.All(entries, entry => Assert.True(entry.IsRecoveryEvent));
        Assert.Contains(entries, entry => entry.DisplayMessage!.Contains("断网"));
        Assert.Contains(entries, entry => entry.DisplayMessage!.Contains("中文 Wi-Fi"));
        Assert.Contains(entries, entry => entry.Level == UiLogLevel.Error);
        Assert.Contains("\"type\":\"check\"", entries[0].Message);
    }

    [Fact]
    public void 增量读取等待完整行和跨写入的中文字符且不会重复()
    {
        var reader = Reader();
        Assert.Empty(reader.Read().Entries);
        Write("app-20261116.log", "2026-11-16 19:00:00 +08:00  RecoveryFailed：");
        Assert.Empty(reader.Read().Entries);
        var bytes = Encoding.UTF8.GetBytes("故障\n");
        using (var file = new FileStream(FilePath("app-20261116.log"), FileMode.Append)) file.Write(bytes, 0, 1);
        Assert.Empty(reader.Read().Entries);
        using (var file = new FileStream(FilePath("app-20261116.log"), FileMode.Append)) file.Write(bytes, 1, bytes.Length - 1);
        var first = Assert.Single(reader.Read().Entries);
        Assert.Equal("RecoveryFailed：故障", first.Message);
        Assert.Empty(reader.Read().Entries);
        Append("app-20261116.log", "额外诊断详情\n");
        var updated = Assert.Single(reader.Read().Entries);
        Assert.Equal(first.Id, updated.Id);
        Assert.Contains("额外诊断详情", updated.Message);
        Append("app-20261116.log", "2026-11-16 19:00:02 +08:00  MonitorStopped：\n");
        Assert.Single(reader.Read().Entries);
    }

    [Fact]
    public void 跨月七天窗口排除过期和未来事件且跳过损坏行()
    {
        var reader = new PersistedEventLogReader(new[] { _directory }, timeProvider: new FixedTime(DateTimeOffset.Parse("2026-11-03T20:00:00+08:00")));
        Write("events-202610.jsonl", "{\"ts\":\"2026-10-26T20:00:00+08:00\",\"type\":\"recovery\",\"action\":\"started\"}\n{\"ts\":\"2026-10-31T20:00:00+08:00\",\"type\":\"recovery\",\"action\":\"started\"}\n损坏的 JSON\n");
        Write("events-202611.jsonl", "{\"ts\":\"2026-11-02T20:00:00+08:00\",\"type\":\"recovery\",\"action\":\"connected\"}\n{\"ts\":\"2026-11-04T20:00:00+08:00\",\"type\":\"recovery\",\"action\":\"failed\"}\n");
        var result = reader.Read();
        Assert.Equal(2, result.Entries.Count);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void 文件被截断和新一天创建文件后仍能继续读取()
    {
        Write("app-20261115.log", "2026-11-15 09:00:00 +08:00  [Information/Interface] 很长的旧记录" + new string('长', 100) + "\n");
        var reader = Reader();
        Assert.Single(reader.Read().Entries);
        Write("app-20261115.log", "2026-11-15 09:00:01 +08:00  [Information/Interface] 新记录\n");
        Assert.Equal("新记录", Assert.Single(reader.Read().Entries).Message);
        Write("app-20261116.log", "2026-11-16 19:00:00 +08:00  MonitorStarted：\n");
        Assert.Single(reader.Read().Entries);
    }

    [Fact]
    public void 大文件历史只返回有限最新明细但不会挤掉关键恢复事件()
    {
        Write("app-20261116.log", string.Concat(Enumerable.Range(0, 1000).Select(i => $"2026-11-16 19:{i / 60:00}:{i % 60:00} +08:00  [Information/Interface] 记录 {i}\n")));
        Write("events-202611.jsonl", "{\"ts\":\"2026-11-10T20:00:00+08:00\",\"type\":\"recovery\",\"action\":\"failed\"}\n");
        var entries = Reader(3).Read().Entries;
        Assert.Equal(4, entries.Count);
        Assert.Contains(entries, entry => entry.IsRecoveryEvent);
        Assert.Contains(entries, entry => entry.Message == "记录 999");
        Assert.DoesNotContain(entries, entry => entry.Message == "记录 1");
    }

    [Fact]
    public void 日志目录被占用时报错而不是抛异常或静默呈现为空()
    {
        Write("app-20261116.log", "2026-11-16 19:00:00 +08:00  MonitorStarted：\n");
        using var locked = new FileStream(FilePath("app-20261116.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.NotEmpty(Reader().Read().Errors);
    }

    [Fact]
    public void 月文件被截断重建后断网边界重新计算()
    {
        var failed = "{\"ts\":\"2026-11-15T09:00:00+08:00\",\"type\":\"check\",\"state\":\"WifiDisconnected\",\"ok\":false}\n";
        Write("events-202611.jsonl", failed + failed);
        var reader = Reader();
        Assert.Single(reader.Read().Entries);
        Write("events-202611.jsonl", failed);
        Assert.Single(reader.Read().Entries);
    }

    [Fact]
    public void 中文原始详情带BOM且使用CRLF时仍能保留完整内容()
    {
        File.WriteAllText(FilePath("app-20261116.log"), "2026-11-16 19:00:00 +08:00  [Error/Diagnostic] 原文\r\n异常堆栈\r\n", new UTF8Encoding(true));
        var entry = Assert.Single(Reader().Read().Entries);
        Assert.Equal("原文\n异常堆栈", entry.Message);
        Assert.Equal(UiLogSource.Diagnostic, entry.Source);
        Assert.Equal(UiLogLevel.Error, entry.Level);
    }
    [Fact]
    public void 二十次断网和十四次修复的统计依据在大量正常记录下仍可查看()
    {
        var rows = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            var start = _now.AddDays(-6).AddHours(i);
            rows.Add(System.Text.Json.JsonSerializer.Serialize(new { ts = start, type = "check", state = "InternetIssue", ok = false }));
            if (i < 14)
            {
                rows.Add(System.Text.Json.JsonSerializer.Serialize(new { ts = start.AddSeconds(2), type = "recovery", action = "started", state = "2" }));
                rows.Add(System.Text.Json.JsonSerializer.Serialize(new { ts = start.AddSeconds(5), type = "recovery", action = i < 10 ? "connected" : "failed", ssid = "测试网络" }));
            }
            rows.Add(System.Text.Json.JsonSerializer.Serialize(new { ts = start.AddMinutes(2), type = "check", state = "Healthy", ok = true }));
        }
        Write("events-202611.jsonl", string.Join("\n", rows) + "\n");
        Write("app-20261116.log", string.Concat(Enumerable.Range(0, 1000).Select(i => $"2026-11-16 19:{i / 60:00}:{i % 60:00} +08:00  轻量检测：Healthy，正常\n")));
        var summary = new NetGuardian.Core.Logging.EventJournal(_directory, new FixedTime(_now)).GetSummary();
        Assert.Equal(new NetGuardian.Core.Logging.WeeklySummary(20, 14, 10), summary);
        var store = new EventLogStore(new NetGuardian.Core.Localization.Localizer());
        store.Import(Reader().Read().Entries, _now);
        Assert.Equal(500, store.Entries.Count);
        Assert.Equal(20, store.Entries.Count(entry => entry.IsRecoveryEvent && entry.Summary.Contains("检测到断网")));
        Assert.Equal(14, store.Entries.Count(entry => entry.IsRecoveryEvent && entry.Summary.Contains("开始执行恢复策略")));
        Assert.Equal(10, store.Entries.Count(entry => entry.IsRecoveryEvent && entry.Summary.Contains("自动恢复成功")));
        Assert.Equal(4, store.Entries.Count(entry => entry.IsRecoveryEvent && entry.Summary.Contains("自动恢复未成功")));
    }
    public void Dispose() => Directory.Delete(_directory, true);
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("测试北京时间", TimeSpan.FromHours(8), "测试北京时间", "测试北京时间");
    }
}
