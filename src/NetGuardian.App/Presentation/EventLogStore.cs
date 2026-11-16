using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using NetGuardian.Core.Localization;

namespace NetGuardian.App.Presentation;

internal enum UiLogLevel { Information, Warning, Error }
internal enum UiLogSource { Interface, Monitor, Diagnostic, Network, Background, Recovery }

internal sealed class UiLogEntry(DateTimeOffset time, string message, UiLogLevel level, UiLogSource source, Localizer localizer,
    string? displayMessage = null, bool isRecoveryEvent = false, string? persistedId = null) : INotifyPropertyChanged
{
    public DateTimeOffset Time { get; private set; } = time;
    public string TimeText => Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string Message { get; private set; } = message;
    private string? _displayMessage = displayMessage;
    public string Summary => (_displayMessage ?? Message).ReplaceLineEndings(" ");
    public UiLogLevel Level { get; private set; } = level;
    public UiLogSource Source { get; private set; } = source;
    public bool IsRecoveryEvent { get; private set; } = isRecoveryEvent;
    public string? PersistedId { get; private set; } = persistedId;
    public string LevelText => localizer.T($"LogLevel{Level}");
    public string SourceText => localizer.T($"LogSource{Source}");
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Matches(string? search, UiLogLevel? level, UiLogSource? source)
    {
        if (level.HasValue && level.Value != Level || source.HasValue && source.Value != Source) return false;
        var text = search?.Trim();
        return string.IsNullOrEmpty(text) || Message.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Summary.Contains(text, StringComparison.OrdinalIgnoreCase) || TimeText.Contains(text, StringComparison.OrdinalIgnoreCase)
            || LevelText.Contains(text, StringComparison.OrdinalIgnoreCase) || SourceText.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    public void Update(PersistedLogEntry entry)
    {
        Time = entry.Time;
        Message = entry.Message;
        _displayMessage = entry.DisplayMessage;
        Level = entry.Level;
        Source = entry.Source;
        IsRecoveryEvent = entry.IsRecoveryEvent;
        PersistedId = entry.Id;
        foreach (var name in new[] { nameof(Time), nameof(TimeText), nameof(Message), nameof(Summary), nameof(Level), nameof(Source), nameof(LevelText), nameof(SourceText) })
            PropertyChanged?.Invoke(this, new(name));
    }

    public void RefreshLanguage()
    {
        PropertyChanged?.Invoke(this, new(nameof(LevelText)));
        PropertyChanged?.Invoke(this, new(nameof(SourceText)));
    }
}

internal sealed class EventLogStore
{
    private readonly Localizer _localizer;
    private readonly int _capacity;
    public ObservableCollection<UiLogEntry> Entries { get; } = [];

    public EventLogStore(Localizer localizer, int capacity = 500)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _localizer = localizer;
        _capacity = capacity;
    }

    public UiLogEntry Add(string message, UiLogLevel level, UiLogSource source)
    {
        ArgumentNullException.ThrowIfNull(message);
        var entry = new UiLogEntry(DateTimeOffset.Now, message, level, source, _localizer);
        Insert(entry);
        return entry;
    }

    public void Import(IEnumerable<PersistedLogEntry> records, DateTimeOffset now)
    {
        foreach (var expired in Entries.Where(entry => entry.Time < now.AddDays(-7)).ToArray()) Entries.Remove(expired);
        foreach (var record in records.Where(record => record.Time >= now.AddDays(-7) && record.Time <= now))
        {
            var existing = Entries.FirstOrDefault(entry => entry.PersistedId == record.Id)
                ?? Entries.FirstOrDefault(entry => entry.PersistedId is null && entry.Message == record.Message
                    && entry.Level == record.Level && entry.Source == record.Source
                    && Math.Abs((entry.Time - record.Time).TotalSeconds) < 2);
            if (existing is not null)
            {
                var timeChanged = existing.Time != record.Time;
                if (timeChanged) Entries.Remove(existing);
                existing.Update(record);
                if (timeChanged) Insert(existing);
            }
            else Insert(new(record.Time, record.Message, record.Level, record.Source, _localizer,
                record.DisplayMessage, record.IsRecoveryEvent, record.Id));
        }
    }

    private void Insert(UiLogEntry entry)
    {
        var index = 0;
        while (index < Entries.Count && Entries[index].Time <= entry.Time) index++;
        Entries.Insert(index, entry);
        while (Entries.Count > _capacity)
        {
            // 关键断网/恢复事件优先，避免高频运行明细把统计依据挤出列表。
            var oldest = Entries.FirstOrDefault(item => !item.IsRecoveryEvent) ?? Entries[0];
            Entries.Remove(oldest);
        }
    }

    public void RefreshLanguage()
    {
        foreach (var entry in Entries) entry.RefreshLanguage();
    }
}
