using System;
using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Updates;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Updates;

namespace NetGuardian.App.Presentation;

internal static class UpdatePresentation
{
    public static void ApplyHistory(Window window, Localizer text, bool? succeeded, string? version, DateTime time, string? logPath)
    {
        var history = (Expander)window.FindName("UpdateHistoryExpander");
        var summary = (TextBlock)window.FindName("UpdatePreviousResultText");
        var path = (TextBox)window.FindName("UpdateLogPathText");
        if (succeeded is not { } success)
        {
            history.Visibility = Visibility.Collapsed;
            history.IsExpanded = false;
            summary.Text = path.Text = string.Empty;
            return;
        }
        summary.Text = string.Format(text.T("UpdatePreviousResult"), version, time,
            text.T(success ? "UpdatePreviousSucceeded" : "UpdatePreviousFailed"));
        path.Text = logPath ?? string.Empty;
        System.Windows.Automation.AutomationProperties.SetName(path, text.T("UpdateLogLocation"));
        history.Visibility = Visibility.Visible;
    }

    public static void Apply(Window window, UpdateSession session, Version version, Localizer text, bool installed)
    {
        T Get<T>(string name) => (T)window.FindName(name);
        Get<TextBlock>("UpdateTitleText").Text = text.T("UpdateTitle");
        Get<TextBlock>("UpdateVersionText").Text = string.Format(text.T("UpdateCurrentVersion"), version);
        Get<Expander>("UpdateNotesExpander").Header = text.T("UpdateNotes");
        Get<Expander>("UpdateNotesExpander").Visibility = session.Release is null ? Visibility.Collapsed : Visibility.Visible;
        Get<TextBlock>("UpdateNotesText").Text = session.Release?.Notes ?? "";
        var canDownload = session.Release is not null && session.Phase is UpdatePhase.Available or UpdatePhase.Cancelled or UpdatePhase.Failed;
        var canResume = canDownload && session.Progress is { Received: > 0 };
        var ready = session.Phase is UpdatePhase.Ready or UpdatePhase.Installing or UpdatePhase.WaitingForExit;
        Button Action(string name, string key, bool visible, bool enabled, string? hint = null)
        {
            var button = Get<Button>(name);
            button.Content = text.T(key);
            button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            button.IsEnabled = enabled;
            button.ToolTip = hint ?? text.T(enabled ? key : "UpdateBusy");
            ToolTipService.SetShowOnDisabled(button, true);
            return button;
        }
        var check = Action("UpdateCheckButton", "UpdateCheck", true, !session.IsBusy);
        check.Style = (Style)window.FindResource(canDownload || ready ? "SecondaryButtonStyle" : "PrimaryButtonStyle");
        Action("UpdateDownloadButton", canResume ? "UpdateResume" : "UpdateDownload", canDownload, !session.IsBusy);
        Action("UpdateCancelButton", "Cancel", session.Phase is UpdatePhase.Checking or UpdatePhase.Downloading, true);
        Action("UpdateInstallButton", "UpdateInstall", ready, session.Phase == UpdatePhase.Ready && installed,
            installed ? null : text.T("UpdateInstalledOnly"));
        var progress = Get<ProgressBar>("UpdateProgressBar");
        progress.Visibility = session.IsBusy || canResume ? Visibility.Visible : Visibility.Collapsed;
        progress.IsIndeterminate = !canResume && (session.Phase != UpdatePhase.Downloading || session.Progress is null);
        progress.Value = session.Progress?.Percentage ?? 0;
        var message = session.Phase switch
        {
            UpdatePhase.Checking => text.T("UpdateChecking"),
            UpdatePhase.NoUpdate => text.T("UpdateNoUpdate"),
            UpdatePhase.Available => string.Format(text.T("UpdateAvailable"), session.Release!.Version),
            UpdatePhase.Downloading when session.Progress is null => text.T("UpdatePreparingDownload"),
            UpdatePhase.Downloading => string.Format(text.T("UpdateDownloading"), session.Progress.Received / 1048576d,
                session.Progress.Total / 1048576d, session.Progress.Percentage),
            UpdatePhase.Ready => session.Error is null ? string.Format(text.T("UpdateReady"), session.Downloaded!.Version)
                : string.Format(text.T("UpdateInstallFailed"), session.Error.Message),
            UpdatePhase.Cancelled => text.T("UpdateCancelled"),
            UpdatePhase.Failed => session.Error is UpdateException error ? text.T(error.Error switch
                {
                    UpdateError.RateLimited => "UpdateRateLimited", UpdateError.InvalidRelease => "UpdateInvalidRelease",
                    UpdateError.VerificationFailed => "UpdateVerificationFailed", _ => "UpdateFailed"
                }) : string.Format(text.T("UpdateFailedDetail"), session.Error?.Message),
            UpdatePhase.Installing => text.T("UpdateInstalling"),
            UpdatePhase.WaitingForExit => text.T("UpdateWaitingForExit"),
            _ => text.T("UpdateIdle")
        };
        if (canResume && session.Progress is { } saved)
            message += " " + string.Format(text.T("UpdateSavedProgress"), saved.Received / 1048576d, saved.Total / 1048576d, saved.Percentage);
        Get<TextBlock>("UpdateStatusText").Text = message;
    }
}
