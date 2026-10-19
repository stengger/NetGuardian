using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NetGuardian.Core.Localization;

namespace NetGuardian.App.Presentation;

internal sealed class PolicyEditFeedback
{
    private readonly TextBlock _status;
    private readonly Localizer _localizer;
    private readonly Func<string> _readState;
    private string _baseline;
    private int _suppression;
    private bool _saved;
    public bool IsDirty { get; private set; }

    public PolicyEditFeedback(FrameworkElement editor, TextBlock status, Localizer localizer, Func<string> readState)
    {
        _status = status;
        _localizer = localizer;
        _readState = readState;
        _baseline = readState();
        editor.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Changed()));
        editor.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => Changed()));
        editor.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => Changed()));
        editor.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => Changed()));
        RefreshLanguage();
    }

    public IDisposable SuppressChanges()
    {
        _suppression++;
        return new Scope(() => _suppression--);
    }

    public void MarkSaved(string message)
    {
        IsDirty = false;
        _saved = true;
        _baseline = _readState();
        _status.Text = message;
    }

    public void RefreshLanguage() => _status.Text = _localizer.T(IsDirty ? "PolicyUnsavedHint" : _saved ? "PolicySavedTitle" : "PolicySaveHint");

    private void Changed()
    {
        if (_suppression != 0) return;
        IsDirty = _readState() != _baseline;
        RefreshLanguage();
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            dispose();
        }
    }
}
