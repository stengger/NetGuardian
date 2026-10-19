using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Network;

namespace NetGuardian.App;

public partial class MainWindow
{
    private readonly SpeedTestSession _speedSession = new();
    private readonly Stopwatch _speedElapsed = new();
    private readonly DispatcherTimer _speedUiTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _speedCancellation;

    private void InitializeSpeedTest()
    {
        SpeedView.RunRequested += async (_, _) => await RunUiActionAsync(RunSpeedTestAsync);
        SpeedView.CancelRequested += (_, _) => CancelSpeedTest();
        SpeedView.CopyRequested += (_, _) => CopySpeedResult();
        _speedUiTimer.Tick += (_, _) => RefreshSpeedView();
        NetworkChange.NetworkAddressChanged += SpeedNetworkAddressChanged;
    }

    private void ResetSpeedUi() => RefreshSpeedView();

    private void RefreshSpeedView()
    {
        if (_resourcesCleaned) return;
        SpeedView.Update(_speedSession, _localizer, _speedElapsed.Elapsed, _speedCancellation?.IsCancellationRequested == true && _speedSession.IsRunning);
    }

    private async Task RunSpeedTestAsync()
    {
        if (_speedCancellation is not null || _resourcesCleaned) return;
        using var cancellation = new CancellationTokenSource();
        _speedCancellation = cancellation;
        var id = _speedSession.Begin();
        _speedElapsed.Restart();
        _speedUiTimer.Start();
        RefreshSpeedView();
        try
        {
            var progress = new SpeedTestProgressObserver(Dispatcher, value =>
            {
                if (_resourcesCleaned) return;
                _speedSession.Apply(id, value);
                RefreshSpeedView();
            });
            var result = await _speedTester.RunAsync(progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _speedSession.Complete(id, result);
            Log($"{_localizer.T("SpeedTools")}: ↓ {result.DownloadMbps:F1} Mbps / ↑ {result.UploadMbps:F1} Mbps / {_localizer.T("SpeedResponse")}: {FormatLatency(result.LatencyMs)}", UiLogLevel.Information, UiLogSource.Diagnostic);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _speedSession.Cancel(id);
        }
        catch (OperationCanceledException)
        {
            _speedSession.Fail(id, _localizer.T("SpeedTimeout"));
        }
        catch (Exception exception)
        {
            _speedSession.Fail(id, exception.Message);
            Log(exception.Message, UiLogLevel.Error, UiLogSource.Diagnostic);
        }
        finally
        {
            _speedElapsed.Stop();
            _speedUiTimer.Stop();
            _speedCancellation = null;
            SpeedView.IsEnabled = true;
            RefreshSpeedView();
        }
    }

    private void CancelSpeedTest()
    {
        _speedCancellation?.Cancel();
        RefreshSpeedView();
    }

    private void SpeedNetworkAddressChanged(object? sender, EventArgs e)
    {
        // 捕获事件发生时的任务身份，排队期间开始的新一轮不受旧事件影响。
        var cancellation = _speedCancellation;
        if (cancellation is null || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(_speedCancellation, cancellation) || !_speedSession.IsRunning || _resourcesCleaned) return;
            _speedSession.Cancel(_speedSession.RunId, networkChanged: true);
            cancellation.Cancel();
            RefreshSpeedView();
            // 传输收尾前不允许新一轮覆盖取消令牌与计时器。
            SpeedView.IsEnabled = false;
        }));
    }

    private void CopySpeedResult()
    {
        if (!_speedSession.CanCopy) return;
        try
        {
            Clipboard.SetText(SpeedView.ResultSummary);
            SpeedView.ShowCopyFeedback(_localizer.T("SpeedCopied"));
        }
        catch (ExternalException)
        {
            SpeedView.ShowCopyFeedback(_localizer.T("SpeedCopyFailed"));
        }
    }

    private void CleanupSpeedTest()
    {
        NetworkChange.NetworkAddressChanged -= SpeedNetworkAddressChanged;
        _speedUiTimer.Stop();
        _speedCancellation?.Cancel();
    }
}
