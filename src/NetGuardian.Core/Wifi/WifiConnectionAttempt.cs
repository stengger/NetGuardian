namespace NetGuardian.Core.Wifi;

public sealed record WifiConnectionOutcome(bool Connected, uint ReasonCode);

/// <summary>只接收本次接口和配置的连接结果，避免无关或迟到通知覆盖结果。</summary>
public sealed class WifiConnectionAttempt(Guid interfaceId, string profileName)
{
    private readonly TaskCompletionSource<WifiConnectionOutcome> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<WifiConnectionOutcome> Completion => _completion.Task;

    public async Task<WifiConnectionOutcome?> WaitAsync(TimeSpan timeout, Func<Task> settle, CancellationToken cancellationToken)
    {
        try { return await Completion.WaitAsync(timeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            await settle().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await settle().ConfigureAwait(false);
            throw;
        }
    }

    public void Observe(uint source, uint code, Guid notificationInterface, string notificationProfile, uint reasonCode)
    {
        if ((source & 8) == 0 || code is not (10 or 11) || notificationInterface != interfaceId
            || !string.Equals(notificationProfile, profileName, StringComparison.Ordinal)) return;
        _completion.TrySetResult(new(code == 10 && reasonCode == 0, reasonCode));
    }
}
