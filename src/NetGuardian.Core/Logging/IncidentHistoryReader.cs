using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetGuardian.Core.Models;

namespace NetGuardian.Core.Logging;

/// <summary>增量读取各目录的月日志。仅消费完整行，按事件标识去重，旧日志原地只读。</summary>
public sealed class IncidentHistoryReader(IEnumerable<string> directories)
{
    private readonly string[] _directories = directories.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private readonly Dictionary<string, Cursor> _files = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<IncidentSummary> _history = [];
    public IReadOnlyList<string> Errors { get; private set; } = [];

    public IReadOnlyList<IncidentSummary> Read()
    {
        var changed = false;
        var errors = new List<string>();
        foreach (var directory in _directories)
        {
            try
            {
                var paths = Directory.GetFiles(directory, "events-??????.jsonl");
                foreach (var path in paths)
                {
                    if (!_files.TryGetValue(path, out var cursor)) _files[path] = cursor = new();
                    try { changed |= cursor.Read(path); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"{path}：{ex.Message}"); }
                    if (cursor.InvalidLines > 0) errors.Add($"{Path.GetFileName(path)} 有 {cursor.InvalidLines} 条记录格式异常");
                }
                foreach (var removed in _files.Keys.Where(p => string.Equals(Path.GetDirectoryName(p), directory,
                    StringComparison.OrdinalIgnoreCase)).Except(paths, StringComparer.OrdinalIgnoreCase).ToArray()) changed |= _files.Remove(removed);
            }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"{directory}：{ex.Message}"); }
        }
        Errors = errors;
        if (changed) _history = Build(_files.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).SelectMany(kv => kv.Value.Steps));
        return _history;
    }

    private sealed class Cursor
    {
        private long _offset;
        private DateTime _lastWrite;
        private long _lineNumber;
        public int InvalidLines { get; private set; }
        public List<IncidentStep> Steps { get; } = [];

        public bool Read(string path)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = file.Length;
            var write = File.GetLastWriteTimeUtc(path);
            var reset = length < _offset || (length == _offset && write != _lastWrite);
            if (reset) { _offset = _lineNumber = InvalidLines = 0; Steps.Clear(); }
            _lastWrite = write;
            if (length == _offset) return reset;
            var startCount = Steps.Count;
            file.Position = _offset;
            using var line = new MemoryStream();
            var buffer = new byte[16384];
            var oversized = false;
            while (file.Position < length)
            {
                var blockStart = file.Position;
                var read = file.Read(buffer, 0, (int)Math.Min(buffer.Length, length - file.Position));
                if (read == 0) break;
                var start = 0;
                while (start < read)
                {
                    var end = Array.IndexOf(buffer, (byte)'\n', start, read - start);
                    var count = (end < 0 ? read : end) - start;
                    if (line.Length + count > 1024 * 1024) oversized = true;
                    if (!oversized) line.Write(buffer, start, count);
                    if (end < 0) break;
                    _lineNumber++;
                    if (oversized) InvalidLines++;
                    else Parse(path, Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).Trim('\uFEFF', '\r'));
                    _offset = blockStart + end + 1;
                    line.SetLength(0);
                    oversized = false;
                    start = end + 1;
                }
            }
            return reset || Steps.Count != startCount;
        }

        private void Parse(string path, string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            try
            {
                var r = JsonSerializer.Deserialize<JournalRecord>(line);
                if (r?.Timestamp is null || r.Timestamp == default) return;
                if (r.Schema == 2 && r.Type == "incident" && r.Incident is { } step &&
                    !string.IsNullOrWhiteSpace(step.Id) && !string.IsNullOrWhiteSpace(step.Session) && step.At == r.Timestamp)
                {
                    Steps.Add(step with { RawLog = line });
                }
                else if (r.Schema is null && (r.Type == "check" && r.Ok.HasValue || r.Type == "recovery" && r.Action == "connected"))
                {
                    var key = $"{Path.GetFileName(path)}|{_lineNumber}|{line}";
                    var id = "v1-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
                    Steps.Add(new(id, r.Timestamp, r.Ok == false ? IncidentStepKind.Failure : IncidentStepKind.LegacyRestored,
                        "旧版日志", "v1", r.Ssid, r.State) { RawLog = line });
                }
            }
            catch (JsonException) { InvalidLines++; }
        }
    }

    private static IReadOnlyList<IncidentSummary> Build(IEnumerable<IncidentStep> source)
    {
        var result = new List<IncidentSummary>();
        var steps = new List<IncidentStep>();
        var duration = TimeSpan.Zero;
        bool gap = false, attempted = false;
        void Finish(IncidentOutcome outcome)
        {
            var first = steps[0];
            result.Add(new(first.Id, first.At, outcome == IncidentOutcome.Open ? null : steps[^1].At,
                first.Target, outcome, duration, gap, first.Session == "v1", steps.ToArray()));
            steps.Clear();
            duration = TimeSpan.Zero;
            gap = attempted = false;
        }
        foreach (var s in source.DistinctBy(s => s.Id).OrderBy(s => s.At))
        {
            if (steps.Count == 0)
            {
                if (s.Kind == IncidentStepKind.Failure) steps.Add(s);
                continue;
            }
            // 旧进程的迟到结果没有事件关联或探针，不能结束由新版本确认的中断。
            if (s.Session == "v1" && steps[0].Session != "v1") continue;
            var previous = steps[^1];
            var elapsed = s.At - previous.At;
            var missing = s.Session != previous.Session || s.Kind == IncidentStepKind.Boundary ||
                previous.Kind == IncidentStepKind.Boundary || (previous.MaxSilenceSeconds > 0 && elapsed.TotalSeconds > previous.MaxSilenceSeconds);
            if (missing)
            {
                gap = true;
                if (previous.Kind != IncidentStepKind.Boundary && s.Kind != IncidentStepKind.Boundary)
                    steps.Add(new($"gap-{s.Id}", s.At, IncidentStepKind.Boundary, s.Actor, s.Session, Detail: "期间缺少观测"));
            }
            else duration += elapsed;
            steps.Add(s);
            attempted |= s.Kind is IncidentStepKind.SwitchAttempt or IncidentStepKind.ReconnectAttempt or IncidentStepKind.ActionStarted;
            if (s.Kind == IncidentStepKind.PublicVerified)
                Finish(s.AfterAction ? IncidentOutcome.RecoveredAfterAction : gap || attempted || steps[0].Session == "v1"
                    ? IncidentOutcome.CauseUnknown : IncidentOutcome.NaturalRecovery);
            else if (s.Kind == IncidentStepKind.LegacyRestored) Finish(IncidentOutcome.CauseUnknown);
        }
        if (steps.Count != 0) Finish(IncidentOutcome.Open);
        result.Reverse();
        return result;
    }
}
