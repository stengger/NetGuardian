using System;
using NetGuardian.App.Presentation;

namespace NetGuardian.App;

public partial class MainWindow
{
    private bool _dnsReadFailed;
    private bool _adapterReadFailed;
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

    private bool RefreshDnsInfo()
    {
        _dnsOperationMessage = null;
        var success = NetworkConfigurationRefresh.TryRead(RefreshDnsInfoCore, ex =>
        {
            _dnsReadFailed = true;
            _isSyncingDnsControls = true;
            try { _dnsRows.Clear(); _dnsAdapterOptions.Clear(); }
            finally { _isSyncingDnsControls = false; }
            DnsCurrentText.Text = _localizer.T("NetworkReadFailed");
            DnsStatusText.Text = _localizer.T("NetworkReadFailed");
            DnsEmptyText.Text = _localizer.T("NetworkReadFailed");
            UpdateDnsApplyAvailability();
            Log($"{_localizer.T("DnsTools")}: {ex.Message}");
            UpdateEmptyStates();
        });
        if (success)
        {
            _dnsReadFailed = false;
            DnsEmptyText.Text = _localizer.T("NoDnsRows");
        }
        return success;
    }

    private bool RefreshAdapterInfo()
    {
        _dhcpOperationMessage = null;
        var success = NetworkConfigurationRefresh.TryRead(RefreshAdapterInfoCore, ex =>
        {
            _adapterReadFailed = true;
            _adapterRows.Clear();
            AdapterTargetComboBox.ItemsSource = null;
            AdapterStatusText.Text = _localizer.T("NetworkReadFailed");
            AdapterEmptyText.Text = _localizer.T("NetworkReadFailed");
            Log($"{_localizer.T("AdapterTools")}: {ex.Message}");
            UpdateEmptyStates();
        });
        if (success)
        {
            _adapterReadFailed = false;
            AdapterEmptyText.Text = _localizer.T("NoAdapterRows");
        }
        if (AdapterTargetComboBox.IsEnabled) AdapterRenewDhcpButton.IsEnabled = AdapterTargetComboBox.Items.Count > 0;
        return success;
    }
}
