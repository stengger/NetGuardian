namespace NetGuardian.Core.Network;

public sealed record NetworkAdapterCandidate(string Id, string Name, bool IsUp, bool HasDefaultGateway,
    bool SupportsIpv4 = true, bool? IsDhcpEnabled = null);

public static class NetworkAdapterSelection
{
    public static bool CanRenewDhcp(NetworkAdapterCandidate selected, NetworkAdapterCandidate current) =>
        selected.Id == current.Id && selected.Name == current.Name && current.IsUp &&
        current.SupportsIpv4 && current.IsDhcpEnabled == true;

    public static string? Choose(IEnumerable<NetworkAdapterCandidate> adapters, string? previousId)
    {
        var available = adapters.Where(adapter => adapter.IsUp).ToList();
        if (previousId is not null && available.Any(adapter => adapter.Id == previousId)) return previousId;
        var routed = available.Where(adapter => adapter.HasDefaultGateway).ToList();
        return routed.Count == 1 ? routed[0].Id : null;
    }
}
