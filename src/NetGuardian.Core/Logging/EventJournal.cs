using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetGuardian.Core.Models;

namespace NetGuardian.Core.Logging;

public sealed class JournalRecord
{
    [JsonPropertyName("ts")] public DateTimeOffset Timestamp { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";   // check | recovery
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("ok")] public bool? Ok { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }    // started | connected | failed
    [JsonPropertyName("ssid")] public string? Ssid { get; set; }
    [JsonPropertyName("severity")] public string? Severity { get; set; }
    [JsonPropertyName("schema")] public int? Schema { get; set; }
    [JsonPropertyName("incident")] public IncidentStep? Incident { get; set; }
}

public sealed record WeeklySummary(int OutageEpisodes, int RecoveryAttempts, int RecoverySuccesses);

/// <summary>
/// Append-only JSONL journal of check failures and recovery actions, rolled monthly
/// (events-yyyyMM.jsonl). Healthy checks are sampled (first after a failure, then
/// at most once per hour) to keep files small while preserving outage boundaries.
/// </summary>
public sealed partial class EventJournal
{
    private const int RetentionDays = 90;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _directory;
    private readonly IncidentHistoryReader _summaryReader;
    private readonly TimeProvider _time;
    private readonly object _syncRoot = new();
    private bool _lastCheckFailed;
    private DateTimeOffset? _lastHealthyWriteAt;
    private DateOnly? _lastCleanupDate;

    public EventJournal(string? directory = null, TimeProvider? timeProvider = null)
    {
        _directory = directory ?? GetDefaultDirectory();
        _time = timeProvider ?? TimeProvider.System;
        _summaryReader = new([_directory]);
    }

    public string Directory => _directory;

    public void RecordCheck(NetworkState state, bool ok)
    {
        lock (_syncRoot)
        {
            var now = _time.GetLocalNow();
            var shouldWrite = !ok
                || _lastCheckFailed
                || _lastHealthyWriteAt is null
                || now - _lastHealthyWriteAt >= TimeSpan.FromHours(1);

            if (shouldWrite)
            {
                Append(new JournalRecord { Timestamp = now, Type = "check", State = state.ToString(), Ok = ok });
                if (ok)
                {
                    _lastHealthyWriteAt = now;
                }
            }

            _lastCheckFailed = !ok;
        }
    }

    public void RecordRecoveryStarted(int failureStreak)
    {
        lock (_syncRoot)
        {
            Append(new JournalRecord
            {
                Timestamp = _time.GetLocalNow(),
                Type = "recovery",
                Action = "started",
                State = failureStreak.ToString()
            });
        }
    }

    public void RecordRecoveryResult(bool success, string? ssid)
    {
        lock (_syncRoot)
        {
            Append(new JournalRecord
            {
                Timestamp = _time.GetLocalNow(),
                Type = "recovery",
                Action = success ? "connected" : "failed",
                Ssid = ssid
            });
        }
    }

    public void RecordHighRisk(string action, bool commandOk, bool networkOk)
    {
        lock (_syncRoot)
        {
            Append(new JournalRecord
            {
                Timestamp = _time.GetLocalNow(),
                Type = "recovery",
                Action = networkOk ? "connected" : "failed",
                State = $"high-risk:{action}" + (commandOk ? "" : ":command-failed"),
                Severity = "high"
            });
        }
    }

    /// <summary>Aggregates the journal over the trailing window (default 7 days).</summary>
    public WeeklySummary GetSummary(int days = 7)
    {
        var now = _time.GetLocalNow();
        var cutoff = now.AddDays(-days);
        IReadOnlyList<IncidentSummary> incidents;
        lock (_summaryReader) incidents = _summaryReader.Read();
        var episodes = incidents.Count(i => i.StartedAt >= cutoff && i.StartedAt <= now);
        var steps = incidents.SelectMany(i => i.Steps).Where(s => s.At >= cutoff && s.At <= now);
        var attempts = steps.Count(s => s.Kind == IncidentStepKind.RoundStarted);
        var successes = steps.Count(s => s.Kind == IncidentStepKind.PublicVerified && s.AfterAction);

        // v1 只有汇总动作，在原口径中保留。v2 的兼容行不能再次计入。
        foreach (var record in ReadRecords(cutoff, now))
        {
            if (record.Schema is not null || record.Type != "recovery") continue;
            if (record.Action == "started") attempts++;
            if (record.Action == "connected") successes++;
        }
        return new WeeklySummary(episodes, attempts, successes);
    }

    private IEnumerable<JournalRecord> ReadRecords(DateTimeOffset from, DateTimeOffset to)
    {
        var months = new List<string>();
        for (var month = new DateTimeOffset(from.Year, from.Month, 1, 0, 0, 0, from.Offset);
             month <= to;
             month = month.AddMonths(1))
        {
            months.Add(month.ToString("yyyyMM"));
        }

        foreach (var month in months)
        {
            var path = Path.Combine(_directory, $"events-{month}.jsonl");
            if (!File.Exists(path))
            {
                continue;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JournalRecord? record;
                try
                {
                    record = JsonSerializer.Deserialize<JournalRecord>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (record is not null && record.Timestamp >= from && record.Timestamp <= to)
                {
                    yield return record;
                }
            }
        }
    }

    private void Append(JournalRecord record)
    {
        try
        {
            if (_observesIncidents) record.Schema = 2;
            System.IO.Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"events-{record.Timestamp:yyyyMM}.jsonl");
            File.AppendAllText(path, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine, Encoding.UTF8);
            CleanupExpiredMonths(record.Timestamp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void CleanupExpiredMonths(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.Date);
        if (_lastCleanupDate == today) return;
        _lastCleanupDate = today;

        // 截止日所在月份仍可能有保留期内的事件，必须保留整卷。
        var cutoff = today.AddDays(-RetentionDays);
        var cutoffMonth = new DateOnly(cutoff.Year, cutoff.Month, 1);
        foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "events-??????.jsonl"))
        {
            var name = Path.GetFileName(path);
            if (name.Length != "events-yyyyMM.jsonl".Length
                || !name.StartsWith("events-", StringComparison.Ordinal)
                || !name.EndsWith(".jsonl", StringComparison.Ordinal)
                || !DateOnly.TryParseExact(name.AsSpan(7, 6), "yyyyMM", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var month)
                || month >= cutoffMonth)
                continue;

            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 单个文件暂时无法清理时继续处理其他文件，下次检查再重试。
            }
        }
    }

    private static string GetDefaultDirectory()
    {
        return Path.Combine(NetGuardian.Core.Runtime.GuardianPaths.DefaultDirectory, "logs");
    }
}
