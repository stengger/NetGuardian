using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetGuardian.Core.Models;

namespace NetGuardian.App.Presentation;

internal enum WifiScanPhase { Idle, Running, Cancelling, Complete, Cancelled, Failed }

internal sealed class WifiScanSession
{
    private CancellationTokenSource? _cancellation;
    public WifiScanPhase Phase { get; private set; }
    public IReadOnlyList<WifiNetwork>? Networks { get; private set; }
    public string? Error { get; private set; }
    public bool IsBusy => _cancellation is not null;

    public void Cancel()
    {
        if (_cancellation is null) return;
        Phase = WifiScanPhase.Cancelling;
        _cancellation.Cancel();
    }

    public async Task RunAsync(Func<CancellationToken, Task<IReadOnlyList<WifiNetwork>>> scan, Action changed)
    {
        if (IsBusy) return;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Phase = WifiScanPhase.Running;
        Networks = null;
        Error = null;
        try
        {
            changed();
            var networks = await scan(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Networks = networks;
            Phase = WifiScanPhase.Complete;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Phase = WifiScanPhase.Cancelled;
        }
        catch (Exception ex)
        {
            if (cancellation.IsCancellationRequested) Phase = WifiScanPhase.Cancelled;
            else { Error = ex.Message; Phase = WifiScanPhase.Failed; }
        }
        finally
        {
            _cancellation = null;
            changed();
        }
    }
}
