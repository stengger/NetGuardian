namespace NetGuardian.Core.Wifi;

public interface INativeWifiConnector
{
    uint? LastNativeError => null;
    bool ConnectionFailureIsFinal => false;
    string? LastConnectionFailure => null;
    bool TryConnectProfile(string profileName, CancellationToken cancellationToken = default);
}
