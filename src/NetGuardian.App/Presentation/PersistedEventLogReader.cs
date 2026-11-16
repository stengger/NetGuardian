using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Logging;
using NetGuardian.Core.Models;

namespace NetGuardian.App.Presentation;

internal sealed record PersistedLogEntry(string Id, DateTimeOffset Time, string Message,
    UiLogLevel Level, UiLogSource Source, string? DisplayMessage = null, bool IsRecoveryEvent = false);

internal sealed record PersistedLogReadResult(IReadOnlyList<PersistedLogEntry> Entries, IReadOnlyList<string> Errors);

/// <summary>只读增量加载日志，不参与监控，不把导入的记录再次写回磁盘。</summary>
internal sealed class PersistedEventLogReader
{
    private static readonly Regex Header = new(@"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [+-]\d{2}:\d{2})  (?<message>.*)$", RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"^\[(?<level>Information|Warning|Error)/(?<source>Interface|Monitor|Diagnostic|Network)\] (?<message>[\s\S]*)$", RegexOptions.Compiled);
    private readonly string[] _directories;
    private readonly int _capacity;
    private readonly TimeProvider _time;
    private readonly Localizer _localizer;
    private readonly Dictionary<string, FileCursor> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _lastCheckOk = new(StringComparer.OrdinalIgnoreCase);

    public PersistedEventLogReader(IEnumerable<string> directories, int capacity = 500,
        TimeProvider? timeProvider = null, Localizer? localizer = null)
    {
        _directories = directories.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _capacity = Math.Max(1, capacity);
        _time = timeProvider ?? TimeProvider.System;
        _localizer = localizer ?? new Localizer();
    }

    public PersistedLogReadResult Read()
    {
        var now = _time.GetLocalNow();
        var from = now.AddDays(-7);
        var normal = new Dictionary<string, PersistedLogEntry>();
        var recovery = new Dictionary<string, PersistedLogEntry>();
        var errors = new List<string>();
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in _directories)
        {
            try
            {
                var paths = Directory.EnumerateFiles(directory).Where(path => InWindow(path, from, now))
                    .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal).ToArray();
                foreach (var path in paths)
                {
                    active.Add(path);
                    if (!_files.TryGetValue(path, out var cursor)) _files[path] = cursor = new FileCursor();
                    try
                    {
                        var journal = Path.GetFileName(path).StartsWith("events-", StringComparison.Ordinal);
                        cursor.Read(path, () => _lastCheckOk.Remove(Path.GetDirectoryName(path)!), (offset, line) =>
                        {
                            if (journal)
                            {
                                try
                                {
                                    var entry = ParseJournal(path, offset, line, from, now);
                                    if (entry is not null) KeepLatest(recovery, entry);
                                }
                                catch (JsonException ex) { if (errors.Count < 4) errors.Add($"{Path.GetFileName(path)}：{ex.Message}"); }
                            }
                            else
                            {
                                var match = Header.Match(line);
                                if (match.Success && DateTimeOffset.TryParseExact(match.Groups["time"].Value,
                                    "yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                                {
                                    if (cursor.Current is not null && cursor.Changed) AddCurrent(cursor, normal, from, now);
                                    cursor.Current = new($"{path}|{cursor.Generation}|{offset}", timestamp, match.Groups["message"].Value,
                                        UiLogLevel.Information, UiLogSource.Background);
                                    cursor.Changed = true;
                                }
                                else if (cursor.Current is not null)
                                {
                                    if (cursor.Current.Message.Length + line.Length > FileCursor.MaxLineBytes)
                                        throw new IOException("单条日志详情过长，未继续加载；完整内容可导出查看。");
                                    cursor.Current = cursor.Current with { Message = cursor.Current.Message + "\n" + line };
                                    cursor.Changed = true;
                                }
                            }
                        });
                        if (!journal && cursor.Current is not null && cursor.Changed) AddCurrent(cursor, normal, from, now);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { if (errors.Count < 4) errors.Add($"{Path.GetFileName(path)}：{ex.Message}"); }
                }
            }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { if (errors.Count < 4) errors.Add($"{directory}：{ex.Message}"); }
        }
        foreach (var path in _files.Keys.Where(path => !active.Contains(path)).ToArray()) _files.Remove(path);
        return new(normal.Values.Concat(recovery.Values).OrderBy(entry => entry.Time).ThenBy(entry => entry.Id, StringComparer.Ordinal).ToArray(),
            errors.Distinct().Take(4).ToArray());
    }

    private static bool InWindow(string path, DateTimeOffset from, DateTimeOffset now)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("app-", StringComparison.Ordinal) && name.EndsWith(".log", StringComparison.Ordinal)
            && DateTime.TryParseExact(name[4..^4], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return day.Date >= from.Date && day.Date <= now.Date;
        if (name.StartsWith("events-", StringComparison.Ordinal) && name.EndsWith(".jsonl", StringComparison.Ordinal)
            && DateTime.TryParseExact(name[7..^6], "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            return month.Year * 12 + month.Month >= from.Year * 12 + from.Month
                && month.Year * 12 + month.Month <= now.Year * 12 + now.Month;
        return false;
    }

    private void KeepLatest(Dictionary<string, PersistedLogEntry> entries, PersistedLogEntry entry)
    {
        entries[entry.Id] = entry;
        if (entries.Count > _capacity) entries.Remove(entries.Values.MinBy(value => value.Time)!.Id);
    }

    private void AddCurrent(FileCursor cursor, Dictionary<string, PersistedLogEntry> normal, DateTimeOffset from, DateTimeOffset now)
    {
        var entry = cursor.Current!;
        cursor.Changed = false;
        if (entry.Time < from || entry.Time > now) return;
        var tag = Tag.Match(entry.Message);
        if (tag.Success)
            entry = entry with { Message = tag.Groups["message"].Value, Level = Enum.Parse<UiLogLevel>(tag.Groups["level"].Value), Source = Enum.Parse<UiLogSource>(tag.Groups["source"].Value) };
        else
            entry = entry with { Level = BackgroundLevel(entry.Message), DisplayMessage = BackgroundMessage(entry.Message) };
        KeepLatest(normal, entry);
    }

    private PersistedLogEntry? ParseJournal(string path, long offset, string line, DateTimeOffset from, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var record = JsonSerializer.Deserialize<JournalRecord>(line);
        if (record is null || record.Timestamp < from || record.Timestamp > now) return null;
        string? display = null;
        var level = UiLogLevel.Information;
        var directory = Path.GetDirectoryName(path)!;
        if (record.Type == "check" && record.Ok is bool ok)
        {
            var previous = !_lastCheckOk.TryGetValue(directory, out var value) || value;
            _lastCheckOk[directory] = ok;
            if (!ok && previous)
            {
                display = string.Format(_localizer.T("LogOutageDetected"), StateText(record.State));
                level = UiLogLevel.Warning;
            }
            else if (ok && !previous) display = _localizer.T("LogConnectivityRestored");
        }
        else if (record.Type == "recovery")
        {
            display = record.Action switch
            {
                "started" => string.Format(_localizer.T("RecoveryStarted"), record.State ?? "—"),
                "connected" => string.Format(_localizer.T("LogRecoverySucceeded"), record.Ssid ?? "—"),
                "failed" => _localizer.T("LogRecoveryFailed"),
                _ => null
            };
            if (record.Action == "failed") level = UiLogLevel.Error;
            if (record.Severity == "high" && display is not null)
                display += " " + string.Format(_localizer.T("LogHighRiskDetails"), record.State ?? "—");
        }
        return display is null ? null : new($"{path}|{_files[path].Generation}|{offset}", record.Timestamp, line, level, UiLogSource.Recovery, display, true);
    }

    private string StateText(string? state) => Enum.TryParse<NetworkState>(state, out var parsed)
        ? _localizer.T(parsed.ToString()) : state ?? "—";

    private static UiLogLevel BackgroundLevel(string message)
    {
        var kind = message.Split('：', 2)[0];
        if (kind is "RecoveryFailed" or "CheckError" or "WifiAdapterEnableFailed") return UiLogLevel.Error;
        if (kind is "RecoveryStarted" or "HighRiskActionStarted") return UiLogLevel.Warning;
        var colon = message.IndexOf('：');
        if (colon >= 0 && kind is ("轻量检测" or "完整诊断"))
        {
            var state = message[(colon + 1)..].Split('，', 2)[0].Trim();
            if (Enum.TryParse<NetworkState>(state, out var parsed) && parsed != NetworkState.Healthy) return UiLogLevel.Warning;
        }
        return UiLogLevel.Information;
    }

    private string BackgroundMessage(string message)
    {
        var colon = message.IndexOf('：');
        if (colon < 0) return message;
        var kind = message[..colon];
        var argument = message[(colon + 1)..].Split(" | ", StringSplitOptions.None)[0].Trim();
        var translated = kind switch
        {
            "MonitorStarted" => _localizer.T("MonitorStartedLog"),
            "MonitorStopped" => _localizer.T("MonitorStoppedLog"),
            "RecoveryStarted" => string.Format(_localizer.T("RecoveryStarted"), argument),
            "RecoveryDnsFlush" => _localizer.T("RecoveryDnsFlush"),
            "RecoveryReconnectCurrent" => string.Format(_localizer.T("RecoveryReconnectCurrent"), argument),
            "RecoverySwitchWifi" => string.Format(_localizer.T("RecoverySwitchWifi"), argument),
            "RecoveryWifiConnected" => string.Format(_localizer.T("LogRecoverySucceeded"), argument),
            "RecoveryFailed" => _localizer.T("LogRecoveryFailed"),
            _ => message
        };
        return translated;
    }

    /// <summary>按字节保存位置和未完成行，允许另一个进程写入，跨写入的 UTF-8 不提前解码。</summary>
    private sealed class FileCursor
    {
        public const int MaxLineBytes = 256 * 1024;
        private long _offset;
        private long _lineOffset;
        private readonly List<byte> _pending = new();
        private DateTime _created;
        private DateTime _written;
        public int Generation { get; private set; }
        public PersistedLogEntry? Current { get; set; }
        public bool Changed { get; set; }

        public void Read(string path, Action reset, Action<long, string> accept)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var info = new FileInfo(path);
            var length = stream.Length;
            if (length < _offset || (_offset > 0 && (_created != info.CreationTimeUtc
                || (length == _offset && _written != info.LastWriteTimeUtc))))
            {
                _offset = _lineOffset = 0;
                _pending.Clear();
                Current = null;
                Changed = false;
                Generation++;
                reset();
            }
            _created = info.CreationTimeUtc;
            _written = info.LastWriteTimeUtc;
            stream.Position = _offset;
            var buffer = new byte[16 * 1024];
            while (_offset < length)
            {
                var count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, length - _offset));
                if (count == 0) break;
                for (var i = 0; i < count; i++)
                {
                    _offset++;
                    if (buffer[i] == (byte)'\n')
                    {
                        var line = Encoding.UTF8.GetString(_pending.ToArray()).TrimEnd('\r').TrimStart('\uFEFF');
                        var offset = _lineOffset;
                        _pending.Clear();
                        _lineOffset = _offset;
                        accept(offset, line);
                    }
                    else
                    {
                        if (_pending.Count >= MaxLineBytes) throw new IOException("日志行过长，未继续加载；完整内容可导出查看。");
                        _pending.Add(buffer[i]);
                    }
                }
            }
        }
    }
}
