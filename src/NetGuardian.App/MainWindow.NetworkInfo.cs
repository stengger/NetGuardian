using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Controls;
using NetGuardian.Core.Network;
using NetGuardian.App.Presentation;

namespace NetGuardian.App;

public partial class MainWindow
{
    private bool _configurationReadFailed;
    private NetworkOperationMessage? _dnsOperationMessage;
    private NetworkOperationMessage? _dhcpOperationMessage;

    private string SetDnsOperationMessage(string key, bool readFailed, params object[] arguments)
    {
        _dnsOperationMessage = new(key, arguments, readFailed);
        return DnsStatusText.Text = _dnsOperationMessage.Format(_localizer);
    }

    private string SetDhcpOperationMessage(string key, bool readFailed, params object[] arguments)
    {
        _dhcpOperationMessage = new(key, arguments, readFailed);
        return AdapterStatusText.Text = _dhcpOperationMessage.Format(_localizer);
    }

    private void RelocalizeNetworkInfo()
    {
        for (var i = 0; i < _dnsRows.Count; i++)
            _dnsRows[i] = _dnsRows[i] with { Source = _localizer.T("DnsOriginUnknown"), Status = NetworkAdapterPresentation.Status(_dnsRows[i].LinkState, _localizer) };
        for (var i = 0; i < _adapterRows.Count; i++)
            _adapterRows[i] = _adapterRows[i] with
            {
                Type = NetworkAdapterPresentation.Type(_adapterRows[i].InterfaceType, _localizer),
                Status = NetworkAdapterPresentation.Status(_adapterRows[i].LinkState, _localizer)
            };
    }

    private bool RefreshConfiguration()
    {
        var previousId = (AdapterTargetComboBox.SelectedItem as NetworkAdapterCandidate)?.Id;
        _dnsOperationMessage = _dhcpOperationMessage = null;
        _isSyncingDnsControls = true;
        try
        {
            _configurationReadFailed = !NetworkConfigurationRefresh.TryRead(() => ReadConfiguration(previousId), ex =>
            {
                _adapterRows.Clear();
                _dnsRows.Clear();
                AdapterTargetComboBox.ItemsSource = null;
                NetworkLog($"{_localizer.T("ConfigurationWorkspace")}: {ex.Message}", UiLogLevel.Error);
            });
            if (previousId != (AdapterTargetComboBox.SelectedItem as NetworkAdapterCandidate)?.Id || _configurationReadFailed)
            {
                SelectDnsPreset("none");
                ApplyDnsPresetToFields();
            }
        }
        finally { _isSyncingDnsControls = false; }
        RefreshTargetDetails();
        DnsEmptyText.Text = _localizer.T(_configurationReadFailed ? "NetworkReadFailed" : "NoDnsRows");
        AdapterEmptyText.Text = _localizer.T(_configurationReadFailed ? "NetworkReadFailed" : "NoAdapterRows");
        if (_configurationReadFailed)
            DnsCurrentText.Text = DnsStatusText.Text = AdapterStatusText.Text = _localizer.T("NetworkReadFailed");
        else
        {
            DnsStatusText.Text = _dnsRows.Count == 0 ? _localizer.T("NoDnsServers") : string.Format(_localizer.T("DnsServerCount"), _dnsRows.Count);
            AdapterStatusText.Text = string.Format(_localizer.T("AdapterCount"), _adapterRows.Count);
        }
        UpdateEmptyStates();
        RefreshProxyAndVpnBanners();
        return !_configurationReadFailed;
    }

    private void ReadConfiguration(string? previousId)
    {
        var targets = new List<NetworkAdapterCandidate>();
        _adapterRows.Clear();
        _dnsRows.Clear();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(item => item.OperationalStatus == OperationalStatus.Up).ThenBy(item => item.Name))
        {
            var properties = adapter.GetIPProperties();
            var up = adapter.OperationalStatus == OperationalStatus.Up;
            var supportsIpv4 = adapter.Supports(NetworkInterfaceComponent.IPv4);
            var ipv4 = properties.UnicastAddresses.Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => item.Address.ToString()).ToArray();
            var gateways = properties.GatewayAddresses.Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => item.Address).ToArray();
            var dns = NetworkAdapterPresentation.Addresses(properties.DnsAddresses);
            var status = LocalizeOperationalStatus(adapter.OperationalStatus);
            targets.Add(new(adapter.Id, adapter.Name, up, gateways.Any(ip => !ip.Equals(IPAddress.Any)), supportsIpv4,
                up && supportsIpv4 ? properties.GetIPv4Properties()?.IsDhcpEnabled : null));
            _adapterRows.Add(new(adapter.Id, adapter.Name, NetworkAdapterPresentation.Type(adapter.NetworkInterfaceType, _localizer), status,
                ipv4.Length == 0 ? "—" : string.Join(", ", ipv4), gateways.Length == 0 ? "—" : string.Join(", ", gateways.AsEnumerable()),
                dns, adapter.NetworkInterfaceType, adapter.OperationalStatus));
            if (up || dns != "—") _dnsRows.Add(new(adapter.Id, adapter.Name, dns, _localizer.T("DnsOriginUnknown"), status, adapter.OperationalStatus));
        }
        AdapterTargetComboBox.ItemsSource = targets;
        // 已选接口消失时清空目标，下次写入必须重新选择。
        var chosenId = previousId ?? NetworkAdapterSelection.Choose(targets, null);
        AdapterTargetComboBox.SelectedItem = targets.FirstOrDefault(item => item.Id == chosenId);
    }

    private void ConfigurationRow_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingDnsControls || sender != e.OriginalSource || !AdapterTargetComboBox.IsEnabled || e.AddedItems.Count != 1) return;
        var id = e.AddedItems[0] switch { AdapterRow adapter => adapter.Id, DnsServerRow dns => dns.Id, _ => null };
        if (id is not null && (AdapterTargetComboBox.SelectedItem as NetworkAdapterCandidate)?.Id != id)
            AdapterTargetComboBox.SelectedItem = AdapterTargetComboBox.Items.OfType<NetworkAdapterCandidate>().FirstOrDefault(item => item.Id == id);
    }

    private void RefreshTargetDetails()
    {
        FillDnsFieldsFromSelectedAdapter();
        UpdateRenewAvailability();
        var target = AdapterTargetComboBox.SelectedItem as NetworkAdapterCandidate;
        RenderSelectedAdapterDetail(target);
        AdapterGrid.SelectedItem = _adapterRows.FirstOrDefault(row => row.Id == target?.Id);
        DnsGrid.SelectedItem = _dnsRows.FirstOrDefault(row => row.Id == target?.Id);
        if (AdapterGrid.SelectedItem is { } adapter) AdapterGrid.ScrollIntoView(adapter);
        if (DnsGrid.SelectedItem is { } dns) DnsGrid.ScrollIntoView(dns);
    }

    private void UpdateRenewAvailability() => AdapterRenewDhcpButton.IsEnabled = !_isNetworkActionRunning && !_isApplyingDns
        && AdapterTargetComboBox.IsEnabled && AdapterTargetComboBox.SelectedItem is NetworkAdapterCandidate { IsUp: true, SupportsIpv4: true, IsDhcpEnabled: true };
}
