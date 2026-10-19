namespace NetGuardian.Core.Wifi;

public interface INativeWifiConnector
{
    uint? LastNativeError => null;
    bool TryConnectProfile(string profileName, CancellationToken cancellationToken = default);
}
