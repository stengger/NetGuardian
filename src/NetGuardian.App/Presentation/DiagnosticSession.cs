using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetGuardian.App.Presentation;

internal enum DiagnosticPhase { Idle, Running, Cancelling, Complete, Cancelled, Failed }

internal sealed class DiagnosticSession<T> where T : class
{
    private CancellationTokenSource? _cancellation;
    public DiagnosticPhase Phase { get; private set; }
    public T? Result { get; private set; }
    public string? Error { get; private set; }
    public bool IsBusy => _cancellation is not null;

    public void Cancel()
    {
        if (_cancellation is null) return;
        Phase = DiagnosticPhase.Cancelling;
        _cancellation.Cancel();
    }

    public async Task RunAsync(Func<CancellationToken, Task<T>> action, Action changed)
    {
        if (IsBusy) return;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Result = null;
        Error = null;
        Phase = DiagnosticPhase.Running;
        try
        {
            changed();
            var result = await action(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Result = result;
            Phase = DiagnosticPhase.Complete;
        }
        catch (Exception ex)
        {
            if (cancellation.IsCancellationRequested) Phase = DiagnosticPhase.Cancelled;
            else { Error = ex.Message; Phase = DiagnosticPhase.Failed; }
        }
        finally
        {
            _cancellation = null;
            changed();
        }
    }
}
