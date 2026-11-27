using System.Text;
using System.Text.Json;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Models;
using NetGuardian.Core.Runtime;

namespace NetGuardian.Core.Logging;

/// <summary>单条网络质量基线样本（存入月度 quality-baseline-yyyyMM.jsonl）。</summary>
public sealed record QualityBaselineSample(
    DateTimeOffset Timestamp,
    string? Ssid,
    int? SignalPercent,
    double? GatewayAvgMs,
    double? DnsAvgMs,
    double? HttpsAvgMs,
    bool Ok);

/// <summary>按时间节流持久化并读取网络质量基线数据，保留近 30 天月度文件。</summary>
public sealed class QualityBaselineStore
{
    private const int RetentionDays = 30;
    private static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly TimeSpan _sampleInterval;
    private readonly object _syncRoot = new();
    private DateTimeOffset? _lastWriteAt;
    private bool? _lastOk;
    private DateOnly? _lastCleanupDate;

    public QualityBaselineStore(
        string? logsDirectory = null,
        TimeProvider? timeProvider = null,
        TimeSpan? sampleInterval = null)
    {
        _directory = string.IsNullOrWhiteSpace(logsDirectory)
            ? Path.Combine(GuardianPaths.DefaultDirectory, "logs")
            : logsDirectory;
        _time = timeProvider ?? TimeProvider.System;
        _sampleInterval = sampleInterval ?? DefaultSampleInterval;
    }

    public string DirectoryPath => _directory;

    /// <summary>根据节流规则记录一次探测报告与 Wi-Fi 信号强度。</summary>
    public bool TryRecord(ConnectivityReport report, int? signalPercent, bool force = false)
    {
        var quality = ConnectivityQualitySummary.From(report);
        var now = _time.GetLocalNow();
        lock (_syncRoot)
        {
            var stateChanged = _lastOk is not null && _lastOk.Value != report.Ok;
            var intervalElapsed = _lastWriteAt is null || (now - _lastWriteAt.Value) >= _sampleInterval;
            if (!force && !stateChanged && !intervalElapsed)
            {
                return false;
            }

            var sample = new QualityBaselineSample(
                Timestamp: now,
                Ssid: string.IsNullOrWhiteSpace(report.CurrentSsid) ? null : report.CurrentSsid.Trim(),
                SignalPercent: signalPercent is >= 0 and <= 100 ? signalPercent : null,
                GatewayAvgMs: quality.Gateway.AverageMs is { } gw ? Math.Round(gw, 1) : null,
                DnsAvgMs: quality.Dns.AverageMs is { } dns ? Math.Round(dns, 1) : null,
                HttpsAvgMs: quality.Https.AverageMs is { } https ? Math.Round(https, 1) : null,
                Ok: report.Ok);

            AppendUnsafe(sample);
            _lastWriteAt = now;
            _lastOk = report.Ok;
            CleanupExpiredUnsafe(now);
            return true;
        }
    }

    /// <summary>读取指定窗口天数内的质量基线样本，按时间升序排列。</summary>
    public IReadOnlyList<QualityBaselineSample> ReadWindow(int windowDays = 7)
    {
        var days = Math.Clamp(windowDays, 1, 90);
        var now = _time.GetLocalNow();
        var cutoff = now.AddDays(-days);

        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var results = new List<QualityBaselineSample>();
        var files = Directory.GetFiles(_directory, "quality-baseline-*.jsonl")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal);

        foreach (var file in files)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                while (sr.ReadLine() is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        var sample = JsonSerializer.Deserialize<QualityBaselineSample>(line, JsonOptions);
                        if (sample is not null && sample.Timestamp >= cutoff && sample.Timestamp <= now.AddMinutes(5))
                        {
                            results.Add(sample);
                        }
                    }
                    catch (JsonException)
                    {
                        // 忽略损坏单行，避免影响历史趋势加载。
                    }
                }
            }
            catch (IOException)
            {
                // 跳过并发瞬态锁定的文件。
            }
        }

        results.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return results;
    }

    private void AppendUnsafe(QualityBaselineSample sample)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"quality-baseline-{sample.Timestamp:yyyyMM}.jsonl");
        var line = JsonSerializer.Serialize(sample, JsonOptions) + Environment.NewLine;
        File.AppendAllText(path, line, Encoding.UTF8);
    }

    private void CleanupExpiredUnsafe(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        if (_lastCleanupDate == today)
        {
            return;
        }
        _lastCleanupDate = today;

        if (!Directory.Exists(_directory))
        {
            return;
        }

        var cutoffMonth = new DateOnly(today.AddDays(-RetentionDays).Year, today.AddDays(-RetentionDays).Month, 1);
        foreach (var file in Directory.GetFiles(_directory, "quality-baseline-*.jsonl"))
        {
            var name = Path.GetFileName(file);
            if (name.Length == "quality-baseline-yyyyMM.jsonl".Length
                && DateOnly.TryParseExact(name.AsSpan(17, 6), "yyyyMM", out var month)
                && month < cutoffMonth)
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // 忽略删除失败，下次轮转时重试。
                }
            }
        }
    }
}
