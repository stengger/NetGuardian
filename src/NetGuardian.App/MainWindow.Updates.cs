using System;
using System.IO;
using System.Windows.Controls;
using System.Text.Json;
using System.Threading;
using System.Windows;
using NetGuardian.App.Presentation;
using NetGuardian.App.Updates;
using NetGuardian.Core.Updates;

namespace NetGuardian.App;

public partial class MainWindow
{
    private UpdateClient _updateHttp = null!;
    private SignedUpdateControls? _signedControls;
    private UpdateRouteStatus? _previousRoute;
    private readonly UpdateStorage _updateStorage = new();
    private UpdateSession? _updateSession;
    private bool _isUpdatePreparing;
    private bool? _previousUpdateSucceeded;
    private string? _previousUpdateLog;
    private string? _previousUpdateVersion;
    private DateTime _previousUpdateTime;
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
        _updateHttp = UpdateClient.CreateOfficial(_updateStorage.PrepareRoot, UpdateStorage.AssertProtected,
            UpdateStorage.CreatePartial, UpdateProxyPolicy.AllowMirrorDirect);
        _updateSession = new UpdateSession(_updateHttp.Service, CurrentAppVersion, async (release, token) =>
        {
            var directory = Path.Combine(_updateStorage.Root, "downloads");
            if (!Directory.Exists(directory)) return default;
            return await _updateHttp.InspectCacheAsync(release, _updateStorage.PrepareDownloads(), token);
        });
        if (_updateHttp.Signed is { } signed)
        {
            _signedControls = new SignedUpdateControls((TextBlock)FindName("UpdateStatusText"), mode =>
            {
                signed.Mode = mode;
                _updateSession.ResetSelection();
            });
            try { _previousRoute = signed.LastSuccessfulRoute(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UpdateException)
            { _previousRoute = null; }
            signed.StatusChanged += ApplyUpdateText;
        }
        _updateSession.Changed += ApplyUpdateText;
        LoadPreviousUpdateResult();
        ApplyUpdateText();
    }

    private void ApplyUpdateText()
    {
        if (_updateSession is null) return;
        _signedControls?.Apply(_localizer.Language == "en-US", !_updateSession.IsBusy && !_isUpdatePreparing);
        SignedUpdateText.Apply(this, _updateSession, CurrentAppVersion, _localizer,
            UpdateInstallLauncher.IsInstalledDirectory(AppContext.BaseDirectory), _updateHttp.Signed?.Status, _previousRoute);
        UpdatePresentation.ApplyHistory(this, _localizer, _previousUpdateSucceeded, _previousUpdateVersion, _previousUpdateTime, _previousUpdateLog);
    }

    private void LoadPreviousUpdateResult()
    {
        try
        {
            _previousUpdateSucceeded = null;
            var resultFile = Path.Combine(_updateStorage.Root, "last-result.json");
            if (!File.Exists(resultFile)) return;
            UpdateStorage.AssertProtected(new FileInfo(resultFile));
            if (new FileInfo(resultFile).Length > 65536) return;
            using var result = JsonDocument.Parse(File.ReadAllText(resultFile));
            if (result.RootElement.TryGetProperty("Stage", out var stage) && stage.GetString() == "Installed") return;
            _previousUpdateVersion = result.RootElement.TryGetProperty("Version", out var version) ? version.GetString() : "—";
            _previousUpdateTime = File.GetLastWriteTime(resultFile);
            _previousUpdateSucceeded = result.RootElement.GetProperty("Success").GetBoolean();
            var job = result.RootElement.GetProperty("JobDirectory").GetString();
            _previousUpdateLog = Path.Combine(job ?? _updateStorage.Root, "update.log");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        { _previousUpdateSucceeded = null; }
    }

    private async void UpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_updateSession is null || _isUpdatePreparing || _updateSession.IsBusy) return;
        await _updateSession.CheckAsync();
    }

    private async void UpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_updateSession is null || _isUpdatePreparing || _updateSession.IsBusy || _updateSession.Release is null) return;
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
            ApplyUpdateText();
            if (!await ConfirmActionAsync(_localizer.T("UpdateConfirmTitle"),
                string.Format(_localizer.T("UpdateConfirmMessage"), downloaded.Version))) return;
            _updateSession.BeginInstall();
            IsEnabled = false;
            await new UpdateInstallLauncher(_updateStorage).PrepareAndExitAsync(downloaded,
                ExitApplicationAsync, _updateSession.WaitForExit, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _updateSession.InstallFailed(ex);
            Log(ex.Message, UiLogLevel.Error);
        }
        finally { IsEnabled = true; _isUpdatePreparing = false; ApplyUpdateText(); }
    }
}
