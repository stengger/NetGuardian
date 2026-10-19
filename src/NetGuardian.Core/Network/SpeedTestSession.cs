namespace NetGuardian.Core.Network;

public enum SpeedTestStatus { Idle, Running, Complete, Partial, Failed, Cancelled }
public enum SpeedMetricState { Pending, Running, Complete, Unavailable, Failed, Cancelled }
public sealed record SpeedTestMetric(double? Value, SpeedMetricState State);

/// <summary>单次测速的可展示状态；轮次编号隔离异步进度，不依赖界面控件。</summary>
public sealed class SpeedTestSession
{
    public int RunId { get; private set; }
    public SpeedTestStatus Status { get; private set; }
    public string Phase { get; private set; } = "Preparing";
    public SpeedTestMetric Download { get; private set; } = new(null, SpeedMetricState.Pending);
    public SpeedTestMetric Upload { get; private set; } = new(null, SpeedMetricState.Pending);
    public SpeedTestMetric Latency { get; private set; } = new(null, SpeedMetricState.Pending);
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public string? FailureDetail { get; private set; }
    public bool NetworkChanged { get; private set; }
    public int LatencySampleCount { get; private set; }
    public bool IsRunning => Status == SpeedTestStatus.Running;
    public bool CanCopy => !IsRunning && new[] { Download, Upload, Latency }.Any(m => m.State == SpeedMetricState.Complete);

    public int Begin()
    {
        RunId++;
        Status = SpeedTestStatus.Running;
        Phase = "Preparing";
        Download = Upload = Latency = new(null, SpeedMetricState.Pending);
        StartedAt = DateTimeOffset.Now;
        FinishedAt = null;
        FailureDetail = null;
        NetworkChanged = false;
        LatencySampleCount = 0;
        return RunId;
    }

    public void Apply(int runId, NetworkSpeedTestProgress progress)
    {
        if (!Accepts(runId)) return;
        if (progress.Phase is not ("Latency" or "Download" or "Upload")) return;
        Phase = progress.Phase;
        var value = Phase == "Latency" ? progress.LatencyMs : progress.Mbps;
        var state = progress.IsPhaseComplete
            ? value.HasValue ? SpeedMetricState.Complete : SpeedMetricState.Unavailable
            : SpeedMetricState.Running;
        var metric = new SpeedTestMetric(value, state);
        if (Phase == "Download") Download = metric;
        if (Phase == "Upload") Upload = metric;
        if (Phase == "Latency")
        {
            Latency = metric;
            LatencySampleCount = progress.LatencySampleCount;
        }
    }

    public void Complete(int runId, NetworkSpeedTestResult result)
    {
        if (!Accepts(runId)) return;
        Download = new(result.DownloadMbps, SpeedMetricState.Complete);
        Upload = new(result.UploadMbps, SpeedMetricState.Complete);
        Latency = new(result.LatencyMs, result.LatencyMs.HasValue ? SpeedMetricState.Complete : SpeedMetricState.Unavailable);
        LatencySampleCount = result.LatencySampleCount;
        Status = result.LatencyMs.HasValue ? SpeedTestStatus.Complete : SpeedTestStatus.Partial;
        FinishedAt = result.CheckedAt;
        Phase = "Complete";
    }

    public void Fail(int runId, string detail)
    {
        if (!Accepts(runId)) return;
        FailureDetail = detail;
        FinishMetrics(SpeedMetricState.Failed);
        Status = new[] { Download, Upload, Latency }.Any(m => m.State == SpeedMetricState.Complete)
            ? SpeedTestStatus.Partial : SpeedTestStatus.Failed;
        FinishedAt = DateTimeOffset.Now;
    }

    public void Cancel(int runId, bool networkChanged = false)
    {
        if (!Accepts(runId)) return;
        NetworkChanged = networkChanged;
        FinishMetrics(SpeedMetricState.Cancelled);
        Status = SpeedTestStatus.Cancelled;
        FinishedAt = DateTimeOffset.Now;
    }

    private bool Accepts(int runId) => runId == RunId && IsRunning;

    private void FinishMetrics(SpeedMetricState reason)
    {
        Download = Finish(Download);
        Upload = Finish(Upload);
        Latency = Finish(Latency);
        SpeedTestMetric Finish(SpeedTestMetric metric) => metric.State switch
        {
            SpeedMetricState.Running => new(null, reason),
            SpeedMetricState.Pending => new(null, SpeedMetricState.Cancelled),
            _ => metric
        };
    }
}
