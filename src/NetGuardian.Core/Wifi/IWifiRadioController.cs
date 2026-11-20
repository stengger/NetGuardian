namespace NetGuardian.Core.Wifi;

public interface IWifiRadioController
{
    string? LastRadioFailure { get; }
    bool TryEnableSoftwareRadio(string adapterName, CancellationToken cancellationToken = default);
}
