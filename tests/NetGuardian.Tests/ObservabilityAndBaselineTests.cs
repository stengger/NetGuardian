using NetGuardian.Core.Logging;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.Tests;

public sealed class ObservabilityAndBaselineTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ng_obs_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void ComputeSla_MergesOverlapsCalculatesAvailabilityMttrAndRanking()
    {
        var now = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.FromHours(8));
        var incidents = new List<IncidentSummary>
        {
            new(
                "inc-1",
                now.AddHours(-24),
                now.AddHours(-23).AddMinutes(-50), // 10 min outage
                "Lab-WiFi",
                IncidentOutcome.RecoveredAfterAction,
                TimeSpan.FromMinutes(10),
                false,
                false,
                [new IncidentStep("s1", now.AddHours(-24), IncidentStepKind.Failure, "test", "sess-1", "Lab-WiFi", "drop")]),
            new(
                "inc-2",
                now.AddHours(-12),
                now.AddHours(-11).AddMinutes(-40), // 20 min outage
                "Office-5G",
                IncidentOutcome.NaturalRecovery,
                TimeSpan.FromMinutes(20),
                false,
                false,
                [new IncidentStep("s2", now.AddHours(-12), IncidentStepKind.Failure, "test", "sess-2", "Office-5G", "drop")]),
            new(
                "inc-3",
                now.AddDays(-2),
                now.AddDays(-2).AddMinutes(10), // 10 min outage, failed
                "Lab-WiFi",
                IncidentOutcome.Open,
                TimeSpan.FromMinutes(10),
                false,
                false,
                [new IncidentStep("s3", now.AddDays(-2), IncidentStepKind.Failure, "test", "sess-3", "Lab-WiFi", "drop")]),
            new(
                "inc-old",
                now.AddDays(-20),
                now.AddDays(-20).AddHours(1), // outside 7d, inside 30d
                "Dorm-Net",
                IncidentOutcome.RecoveredAfterAction,
                TimeSpan.FromHours(1),
                false,
                false,
                [new IncidentStep("s4", now.AddDays(-20), IncidentStepKind.Failure, "test", "sess-4", "Dorm-Net", "drop")])
        };

        var sla7 = NetworkObservabilityAnalyzer.ComputeSla(incidents, 7, now);
        Assert.Equal(3, sla7.IncidentCount);
        Assert.Equal(2, sla7.RecoveredCount);
        Assert.Equal(15.0, sla7.MttrMinutes); // (10 + 20) / 2
        Assert.Equal(20.0, sla7.LongestOutageMinutes);
        Assert.InRange(sla7.AvailabilityPercent, 99.5, 99.7); // 40 min / (7*1440 min) = ~99.60%
        Assert.Equal("Lab-WiFi", sla7.WifiStabilityRanking[0].Ssid);
        Assert.Equal(2, sla7.WifiStabilityRanking[0].OutageCount);

        var sla30 = NetworkObservabilityAnalyzer.ComputeSla(incidents, 30, now);
        Assert.Equal(4, sla30.IncidentCount);
        Assert.Equal(3, sla30.RecoveredCount);
    }

    [Fact]
    public void QualityBaselineStore_ThrottlesWritesAndComputesPercentiles()
    {
        var t0 = new DateTimeOffset(2026, 11, 27, 8, 0, 0, TimeSpan.FromHours(8));
        var fakeTime = new FakeTimeProvider(t0);
        var store = new QualityBaselineStore(_tempDir, fakeTime, TimeSpan.FromMinutes(5));

        var okReport1 = new ConnectivityReport
        {
            State = NetworkState.Healthy,
            CurrentSsid = "Studio-5G",
            Probes = [new ConnectivityProbe("HTTPS https://www.msftconnecttest.com", true, 40, "200")]
        };

        Assert.True(store.TryRecord(okReport1, signalPercent: 86));
        // 1 分钟后健康状态未变化，应节流跳过。
        fakeTime.Advance(TimeSpan.FromMinutes(1));
        Assert.False(store.TryRecord(okReport1, signalPercent: 84));

        // 发生状态变化（Healthy -> Offline），应绕过节流立即落盘。
        var failReport = new ConnectivityReport
        {
            State = NetworkState.InternetIssue,
            CurrentSsid = "Studio-5G",
            Probes = [new ConnectivityProbe("HTTPS https://www.msftconnecttest.com", false, 2000, "timeout")]
        };
        Assert.True(store.TryRecord(failReport, signalPercent: 40));

        // 6 分钟后再次记录更高延迟健康样本。
        fakeTime.Advance(TimeSpan.FromMinutes(6));
        var okReport2 = new ConnectivityReport
        {
            State = NetworkState.Healthy,
            CurrentSsid = "Studio-5G",
            Probes = [new ConnectivityProbe("HTTPS https://www.msftconnecttest.com", true, 120, "200")]
        };
        Assert.True(store.TryRecord(okReport2, signalPercent: 90));

        var samples = store.ReadWindow(7);
        Assert.Equal(3, samples.Count);

        var trend = NetworkObservabilityAnalyzer.SummarizeQualityTrend(samples);
        Assert.Equal(3, trend.SampleCount);
        Assert.Equal(80.0, trend.P50HttpsMs); // [40, 120] 50th = 80
        Assert.Equal(116.0, trend.P95HttpsMs); // 40 + (120-40)*0.95 = 116
        Assert.Equal(72.0, trend.AverageSignalPercent); // (86+40+90)/3 = 72
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset initial) => _now = initial;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
    }
}
