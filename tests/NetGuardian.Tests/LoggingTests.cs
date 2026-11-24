using NetGuardian.Core.Logging;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.Tests;

public sealed class FileLogSinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"netguardian-logs-{Guid.NewGuid():N}");

    [Fact]
    public void Write_CreatesDailyFileWithLine()
    {
        using (var sink = new FileLogSink(_dir, retentionDays: 14))
        {
            sink.Write("hello world");
        } // Dispose flushes

        var file = Path.Combine(_dir, $"app-{DateTimeOffset.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(file));
        Assert.Contains("hello world", File.ReadAllText(file));
    }

    [Fact]
    public void Cleanup_DeletesFilesOlderThanRetention()
    {
        Directory.CreateDirectory(_dir);
        var oldFile = Path.Combine(_dir, $"app-{DateTimeOffset.Now.AddDays(-30):yyyyMMdd}.log");
        var recentFile = Path.Combine(_dir, $"app-{DateTimeOffset.Now.AddDays(-2):yyyyMMdd}.log");
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(recentFile, "recent");

        using (var sink = new FileLogSink(_dir, retentionDays: 14))
        {
            sink.Write("trigger cleanup");
        }

        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(recentFile));
    }

    [Fact]
    public void RetentionDays_IsClamped()
    {
        using var sink = new FileLogSink(_dir, retentionDays: 5000);
        Assert.Equal(90, sink.RetentionDays);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

public sealed class EventJournalTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"netguardian-journal-{Guid.NewGuid():N}");

    private EventJournal Journal() => new(_dir);

    [Fact]
    public void RecordCheck_WritesFailuresAndSamplesHealthy()
    {
        var journal = Journal();
        journal.RecordCheck(NetworkState.Healthy, ok: true);      // first healthy -> written
        journal.RecordCheck(NetworkState.Healthy, ok: true);      // within an hour -> skipped
        journal.RecordCheck(NetworkState.InternetIssue, ok: false); // failure -> written
        journal.RecordCheck(NetworkState.Healthy, ok: true);      // first healthy after failure -> written

        var file = Path.Combine(_dir, $"events-{DateTimeOffset.Now:yyyyMM}.jsonl");
        Assert.True(File.Exists(file));
        Assert.Equal(3, File.ReadAllLines(file).Count(line => !string.IsNullOrWhiteSpace(line)));
    }

    [Fact]
    public void GetSummary_CountsEpisodesAttemptsAndSuccesses()
    {
        var journal = Journal();
        // Episode 1: fail, fail, recover successfully.
        journal.RecordCheck(NetworkState.InternetIssue, ok: false);
        journal.RecordCheck(NetworkState.InternetIssue, ok: false);
        journal.RecordRecoveryStarted(2);
        journal.RecordRecoveryResult(true, "Backup");
        journal.RecordCheck(NetworkState.Healthy, ok: true);
        // Episode 2: fail, recovery fails.
        journal.RecordCheck(NetworkState.DnsIssue, ok: false);
        journal.RecordRecoveryStarted(1);
        journal.RecordRecoveryResult(false, null);

        var summary = journal.GetSummary(days: 7);
        Assert.Equal(2, summary.OutageEpisodes);
        Assert.Equal(2, summary.RecoveryAttempts);
        Assert.Equal(1, summary.RecoverySuccesses);
    }

    [Fact]
    public void 写入事件时只清理超出保留期的完整月份()
    {
        Directory.CreateDirectory(_dir);
        foreach (var name in new[] { "events-202607.jsonl", "events-202608.jsonl", "events-202610.jsonl",
            "events-202613.jsonl", "events-202601.jsonl.bak", "app-20260101.log" })
            File.WriteAllText(Path.Combine(_dir, name), "历史记录");
        var clock = new JournalClock(new DateTimeOffset(2026, 11, 29, 12, 0, 0, TimeSpan.Zero));
        var journal = new EventJournal(_dir, clock);

        journal.RecordRecoveryStarted(1);
        Assert.False(File.Exists(Path.Combine(_dir, "events-202607.jsonl")));
        Assert.True(File.Exists(Path.Combine(_dir, "events-202608.jsonl")));

        clock.Now = clock.Now.AddDays(1);
        journal.RecordRecoveryResult(true, "备用网络");
        Assert.False(File.Exists(Path.Combine(_dir, "events-202608.jsonl")));
        foreach (var name in new[] { "events-202610.jsonl", "events-202611.jsonl", "events-202613.jsonl",
            "events-202601.jsonl.bak", "app-20260101.log" })
            Assert.True(File.Exists(Path.Combine(_dir, name)), name);
    }

    [Fact]
    public void 清理后仍能统计跨月的近七天事件()
    {
        var clock = new JournalClock(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        var journal = new EventJournal(_dir, clock);
        journal.RecordCheck(NetworkState.InternetIssue, ok: false);
        journal.RecordRecoveryStarted(1);
        var expired = Path.Combine(_dir, "events-202601.jsonl");
        File.WriteAllText(expired, "过期记录");

        clock.Now = clock.Now.AddDays(1);
        journal.RecordRecoveryResult(true, "备用网络");
        journal.RecordCheck(NetworkState.Healthy, ok: true);

        Assert.False(File.Exists(expired));
        Assert.Equal(new WeeklySummary(1, 1, 1), journal.GetSummary());
    }

    private sealed class JournalClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void GetSummary_EmptyJournal_ReturnsZeros()
    {
        var summary = Journal().GetSummary();
        Assert.Equal(new WeeklySummary(0, 0, 0), summary);
    }

    [Fact]
    public void GetSummary_SkipsCorruptedLines()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, $"events-{DateTimeOffset.Now:yyyyMM}.jsonl");
        File.WriteAllLines(file,
        [
            "{not json",
            $$"""{"ts":"{{DateTimeOffset.Now:O}}","type":"recovery","action":"started"}"""
        ]);

        var summary = Journal().GetSummary();
        Assert.Equal(1, summary.RecoveryAttempts);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
