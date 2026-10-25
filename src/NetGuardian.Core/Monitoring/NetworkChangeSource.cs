using System.Net.NetworkInformation;

namespace NetGuardian.Core.Monitoring;

/// <summary>监控生命周期内的网络变化订阅，返回值负责解除订阅。</summary>
public interface INetworkChangeSource
{
    IDisposable Subscribe(Action changed);
}

public sealed class NetworkChangeSource : INetworkChangeSource
{
    public IDisposable Subscribe(Action changed) => new Subscription(changed);

    private sealed class Subscription : IDisposable
    {
        private readonly NetworkAvailabilityChangedEventHandler _availability;
        private readonly NetworkAddressChangedEventHandler _address;
        private int _disposed;

        public Subscription(Action changed)
        {
            _availability = (_, _) => changed();
            _address = (_, _) => changed();
            NetworkChange.NetworkAvailabilityChanged += _availability;
            try { NetworkChange.NetworkAddressChanged += _address; }
            catch
            {
                NetworkChange.NetworkAvailabilityChanged -= _availability;
                throw;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            NetworkChange.NetworkAvailabilityChanged -= _availability;
            NetworkChange.NetworkAddressChanged -= _address;
        }
    }
}
