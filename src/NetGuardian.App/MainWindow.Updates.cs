using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Windows;
using NetGuardian.App.Presentation;
using NetGuardian.App.Updates;
using NetGuardian.Core.Updates;

namespace NetGuardian.App;

public partial class MainWindow
{
    private readonly HttpClient _updateHttp = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly UpdateStorage _updateStorage = new();
    private UpdateSession? _updateSession;
    private bool _isUpdatePreparing;
    private bool? _previousUpdateSucceeded;
    private string? _previousUpdateLog;
    private static Version CurrentAppVersion
    {
        get
        {
            var version = typeof(MainWindow).Assembly.GetName().Version!;
            return new Version(version.Major, version.Minor, version.Build);
        }
    }

    private void InitializeUpdates()
    {
        _updateSession = new UpdateSession(new GitHubUpdateService(_updateHttp), CurrentAppVersion);
        _updateSession.Changed += ApplyUpdateText;
        LoadPreviousUpdateResult();
        ApplyUpdateText();
    }

    private void ApplyUpdateText()
    {
        if (_updateSession is null) return;
        UpdatePresentation.Apply(this, _updateSession, CurrentAppVersion, _localizer,
            UpdateInstallLauncher.IsInstalledDirectory(AppContext.BaseDirectory));
        if (_previousUpdateSucceeded is { } success)
        {
            UpdatePreviousResultText.Text = string.Format(_localizer.T("UpdatePreviousResult"),
                _localizer.T(success ? "UpdatePreviousSucceeded" : "UpdatePreviousFailed"), _previousUpdateLog);
            UpdatePreviousResultText.Visibility = Visibility.Visible;
        }
    }

    private void LoadPreviousUpdateResult()
    {
        try
        {
            var resultFile = Path.Combine(_updateStorage.Root, "last-result.json");
            if (!File.Exists(resultFile)) return;
            UpdateStorage.AssertProtected(new FileInfo(resultFile));
            if (new FileInfo(resultFile).Length > 65536) return;
            using var result = JsonDocument.Parse(File.ReadAllText(resultFile));
            _previousUpdateSucceeded = result.RootElement.GetProperty("Success").GetBoolean();
            var job = result.RootElement.GetProperty("JobDirectory").GetString();
            _previousUpdateLog = Path.Combine(job ?? _updateStorage.Root, "update.log");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        { _previousUpdateSucceeded = null; }
    }

    private async void UpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_updateSession is null || _updateSession.IsBusy) return;
        await _updateSession.CheckAsync();
    }

    private async void UpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_updateSession is null || _updateSession.IsBusy || _updateSession.Release is null) return;
        try
        {
            await _updateSession.DownloadAsync(_updateStorage.PrepareDownloads());
            if (_updateSession.Downloaded is { } downloaded) UpdateStorage.ProtectFile(downloaded.InstallerPath);
        }
        catch (Exception ex)
        {
            _updateSession.DownloadPreparationFailed(ex);
        }
    }

    private void UpdateCancel_Click(object sender, RoutedEventArgs e) => _updateSession?.Cancel();

    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_updateSession is not { Phase: UpdatePhase.Ready, Downloaded: { } downloaded } || _isUpdatePreparing) return;
        _isUpdatePreparing = true;
        try
        {
            if (!await ConfirmActionAsync(_localizer.T("UpdateConfirmTitle"),
                string.Format(_localizer.T("UpdateConfirmMessage"), downloaded.Version))) return;
            _updateSession.BeginInstall();
            IsEnabled = false;
            await new UpdateInstallLauncher(_updateStorage).PrepareAsync(downloaded, CancellationToken.None);
            ExitApplication();
        }
        catch (Exception ex)
        {
            _updateSession.InstallFailed(ex);
            Log(ex.Message, UiLogLevel.Error);
        }
        finally { IsEnabled = true; _isUpdatePreparing = false; }
    }
}
