using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using NetGuardian.Core.Localization;

namespace NetGuardian.App.Presentation;

internal enum UiLogLevel { Information, Warning, Error }
internal enum UiLogSource { Interface, Monitor, Diagnostic, Network }

internal sealed class UiLogEntry(DateTimeOffset time, string message, UiLogLevel level, UiLogSource source, Localizer localizer) : INotifyPropertyChanged
{
    public DateTimeOffset Time { get; } = time;
    public string TimeText => Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string Message { get; } = message;
    public string Summary => Message.ReplaceLineEndings(" ");
    public UiLogLevel Level { get; } = level;
    public UiLogSource Source { get; } = source;
    public string LevelText => localizer.T($"LogLevel{Level}");
    public string SourceText => localizer.T($"LogSource{Source}");
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Matches(string? search, UiLogLevel? level, UiLogSource? source)
    {
        if (level.HasValue && level.Value != Level || source.HasValue && source.Value != Source) return false;
        var text = search?.Trim();
        return string.IsNullOrEmpty(text) || Message.Contains(text, StringComparison.OrdinalIgnoreCase)
            || TimeText.Contains(text, StringComparison.OrdinalIgnoreCase) || LevelText.Contains(text, StringComparison.OrdinalIgnoreCase)
            || SourceText.Contains(text, StringComparison.OrdinalIgnoreCase);
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
        Entries.Add(entry);
        while (Entries.Count > _capacity) Entries.RemoveAt(0);
        return entry;
    }

    public void RefreshLanguage()
    {
        foreach (var entry in Entries) entry.RefreshLanguage();
    }
}
