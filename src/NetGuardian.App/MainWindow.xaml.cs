using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;
using NetGuardian.Core.Settings;
using NetGuardian.Core.Wifi;

namespace NetGuardian.App;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly Localizer _localizer = new();
    private readonly IWifiClient _wifi = new WifiClient();
    private readonly ObservableCollection<WifiNetwork> _nearbyNetworks = [];
    private readonly ObservableCollection<WifiProfile> _profiles = [];
    private readonly DispatcherTimer _monitorTimer = new();
    private bool _isChecking;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _store.Load();
        _localizer.SetLanguage(_settings.Language);
        foreach (var profile in _settings.Profiles.OrderBy(profile => profile.Priority))
        {
            _profiles.Add(profile);
        }

        NearbyGrid.ItemsSource = _nearbyNetworks;
        ProfileGrid.ItemsSource = _profiles;
        _monitorTimer.Tick += async (_, _) => await RunCheckAsync();
        ApplySettingsToUi();
        ApplyLanguage();
        ShowPanel(StatusPanel);
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        await RunCheckAsync();
    }

    private void StartMonitor_Click(object sender, RoutedEventArgs e)
    {
        SavePolicyFromUi();
        _monitorTimer.Interval = TimeSpan.FromSeconds(_settings.Policy.CheckIntervalSeconds);
        _monitorTimer.Start();
        Log("Monitor started.");
    }

    private void StopMonitor_Click(object sender, RoutedEventArgs e)
    {
        _monitorTimer.Stop();
        Log("Monitor stopped.");
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        await ScanWifiAsync();
    }

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        if (NearbyGrid.SelectedItem is WifiNetwork network)
        {
            AddProfile(network.Ssid);
        }
    }

    private void AddManual_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SsidDialog(_localizer.T("AddManual")) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Ssid))
        {
            AddProfile(dialog.Ssid.Trim());
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedProfile(-1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedProfile(1);
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileGrid.SelectedItem is not WifiProfile profile)
        {
            return;
        }

        Log($"Connecting to {profile.Ssid}.");
        var ok = await _wifi.ConnectAsync(profile.Ssid);
        Log(ok ? $"Connection command sent: {profile.Ssid}" : $"Connection command failed: {profile.Ssid}");
    }

    private void SavePolicy_Click(object sender, RoutedEventArgs e)
    {
        SavePolicyFromUi();
        SaveSettings();
        Log("Policy saved.");
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string language)
        {
            return;
        }

        _settings.Language = language;
        _localizer.SetLanguage(language);
        ApplyLanguage();
        SaveSettings();
    }

    private void ShowStatus_Click(object sender, RoutedEventArgs e) => ShowPanel(StatusPanel);
    private void ShowWifi_Click(object sender, RoutedEventArgs e) => ShowPanel(WifiPanel);
    private void ShowPolicy_Click(object sender, RoutedEventArgs e) => ShowPanel(PolicyPanel);
    private void ShowTools_Click(object sender, RoutedEventArgs e) => ShowPanel(ToolsPanel);
    private void ShowLog_Click(object sender, RoutedEventArgs e) => ShowPanel(LogPanel);
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowPanel(SettingsPanel);

    private async Task RunCheckAsync()
    {
        if (_isChecking)
        {
            return;
        }

        _isChecking = true;
        CheckNowButton.IsEnabled = false;
        try
        {
            var currentSsid = await _wifi.GetCurrentSsidAsync();
            var tester = new ConnectivityTester(_settings.Policy);
            var report = await tester.RunFullCheckAsync(currentSsid);
            ApplyReport(report);
            Log($"{report.CheckedAt:yyyy-MM-dd HH:mm:ss}  {report.State}: {report.Summary}");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
            _isChecking = false;
        }
    }

    private async Task ScanWifiAsync()
    {
        ScanButton.IsEnabled = false;
        Log(_localizer.T("Scanning"));
        try
        {
            var networks = await _wifi.ScanAsync();
            _nearbyNetworks.Clear();
            foreach (var network in networks)
            {
                _nearbyNetworks.Add(network);
            }

            Log(string.Format(_localizer.T("ScanDone"), _nearbyNetworks.Count));
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private void AddProfile(string ssid)
    {
        if (_profiles.Any(profile => profile.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _profiles.Add(new WifiProfile
        {
            Ssid = ssid,
            Priority = _profiles.Count + 1
        });
        RenumberProfiles();
        SaveSettings();
    }

    private void MoveSelectedProfile(int direction)
    {
        if (ProfileGrid.SelectedItem is not WifiProfile profile)
        {
            return;
        }

        var oldIndex = _profiles.IndexOf(profile);
        var newIndex = oldIndex + direction;
        if (newIndex < 0 || newIndex >= _profiles.Count)
        {
            return;
        }

        _profiles.Move(oldIndex, newIndex);
        RenumberProfiles();
        ProfileGrid.SelectedItem = profile;
        SaveSettings();
    }

    private void RenumberProfiles()
    {
        for (var index = 0; index < _profiles.Count; index++)
        {
            _profiles[index].Priority = index + 1;
        }

        ProfileGrid.Items.Refresh();
    }

    private void ApplyReport(ConnectivityReport report)
    {
        HealthStateText.Text = StateText(report.State);
        HealthStateText.Foreground = report.State switch
        {
            NetworkState.Healthy => System.Windows.Media.Brushes.SeaGreen,
            NetworkState.Recovering => System.Windows.Media.Brushes.SteelBlue,
            NetworkState.GatewayIssue or NetworkState.DnsIssue or NetworkState.InternetIssue => System.Windows.Media.Brushes.DarkOrange,
            _ => System.Windows.Media.Brushes.Firebrick
        };
        SummaryText.Text = report.Summary;
        CurrentSsidText.Text = string.IsNullOrWhiteSpace(report.CurrentSsid) ? "-" : report.CurrentSsid;
        LastCheckText.Text = report.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        ProbeGrid.ItemsSource = report.Probes.Select(probe => new ProbeRow(
            probe.Name,
            probe.Ok,
            probe.LatencyMs.HasValue ? $"{probe.LatencyMs.Value:F0} ms" : "-",
            probe.Detail));
    }

    private string StateText(NetworkState state)
    {
        return state switch
        {
            NetworkState.Healthy => _localizer.T("Healthy"),
            NetworkState.Disconnected => _localizer.T("Disconnected"),
            NetworkState.GatewayIssue => _localizer.T("GatewayIssue"),
            NetworkState.DnsIssue => _localizer.T("DnsIssue"),
            NetworkState.InternetIssue => _localizer.T("InternetIssue"),
            NetworkState.Recovering => _localizer.T("Recovering"),
            NetworkState.Failed => _localizer.T("Failed"),
            _ => state.ToString()
        };
    }

    private void ApplySettingsToUi()
    {
        IntervalTextBox.Text = _settings.Policy.CheckIntervalSeconds.ToString();
        FailuresTextBox.Text = _settings.Policy.ConsecutiveFailuresBeforeRecovery.ToString();
        CooldownTextBox.Text = _settings.Policy.RecoveryCooldownSeconds.ToString();
        AutoSwitchCheckBox.IsChecked = _settings.Policy.EnableAutoSwitch;
        SoftReconnectCheckBox.IsChecked = _settings.Policy.EnableCurrentWifiReconnect;
        DnsFlushCheckBox.IsChecked = _settings.Policy.EnableDnsFlush;

        foreach (ComboBoxItem item in LanguageComboBox.Items)
        {
            if ((string)item.Tag == _settings.Language)
            {
                LanguageComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private void SavePolicyFromUi()
    {
        if (int.TryParse(IntervalTextBox.Text, out var interval))
        {
            _settings.Policy.CheckIntervalSeconds = interval;
        }

        if (int.TryParse(FailuresTextBox.Text, out var failures))
        {
            _settings.Policy.ConsecutiveFailuresBeforeRecovery = failures;
        }

        if (int.TryParse(CooldownTextBox.Text, out var cooldown))
        {
            _settings.Policy.RecoveryCooldownSeconds = cooldown;
        }

        _settings.Policy.EnableAutoSwitch = AutoSwitchCheckBox.IsChecked == true;
        _settings.Policy.EnableCurrentWifiReconnect = SoftReconnectCheckBox.IsChecked == true;
        _settings.Policy.EnableDnsFlush = DnsFlushCheckBox.IsChecked == true;
        _settings.Policy.Normalize();
        ApplySettingsToUi();
    }

    private void SaveSettings()
    {
        _settings.Profiles = _profiles.ToList();
        _store.Save(_settings);
    }

    private void ApplyLanguage()
    {
        Title = _localizer.T("AppTitle");
        AppTitleText.Text = _localizer.T("AppTitle");
        StatusNavButton.Content = _localizer.T("Status");
        WifiNavButton.Content = _localizer.T("Wifi");
        PolicyNavButton.Content = _localizer.T("Policy");
        ToolsNavButton.Content = _localizer.T("Tools");
        LogNavButton.Content = _localizer.T("Log");
        SettingsNavButton.Content = _localizer.T("Settings");
        CheckNowButton.Content = _localizer.T("CheckNow");
        StartMonitorButton.Content = _localizer.T("StartMonitor");
        StopButton.Content = _localizer.T("Stop");
        ScanButton.Content = _localizer.T("Scan");
        AddSelectedButton.Content = _localizer.T("AddSelected");
        AddManualButton.Content = _localizer.T("AddManual");
        MoveUpButton.Content = _localizer.T("MoveUp");
        MoveDownButton.Content = _localizer.T("MoveDown");
        ConnectButton.Content = _localizer.T("Connect");
        HealthStateLabel.Text = _localizer.T("HealthState");
        CurrentNetworkLabel.Text = _localizer.T("CurrentNetwork");
        LastCheckLabel.Text = _localizer.T("LastCheck");
        NearbyNetworksTitle.Text = _localizer.T("NearbyNetworks");
        PriorityListTitle.Text = _localizer.T("PriorityList");
        NearbySsidColumn.Header = _localizer.T("Ssid");
        NearbySignalColumn.Header = _localizer.T("Signal");
        NearbySecurityColumn.Header = _localizer.T("Security");
        ProfileSsidColumn.Header = _localizer.T("Ssid");
        ProfilePriorityColumn.Header = _localizer.T("Priority");
        ProfileEnabledColumn.Header = _localizer.T("Enabled");
        ProfileFailuresColumn.Header = _localizer.T("Failures");
        PolicyTitle.Text = _localizer.T("Policy");
        IntervalLabel.Text = _localizer.T("Interval");
        FailuresBeforeRecoveryLabel.Text = _localizer.T("FailuresBeforeRecovery");
        CooldownLabel.Text = _localizer.T("Cooldown");
        AutoSwitchCheckBox.Content = _localizer.T("AutoSwitch");
        SoftReconnectCheckBox.Content = _localizer.T("SoftReconnect");
        DnsFlushCheckBox.Content = _localizer.T("DnsFlush");
        SavePolicyButton.Content = _localizer.T("Save");
        ToolsCheckButton.Content = _localizer.T("CheckNow");
        ToolsScanButton.Content = _localizer.T("Scan");
        HighRiskText.Text = _localizer.T("HighRiskReserved");
        SettingsTitle.Text = _localizer.T("Settings");
        LanguageLabel.Text = _localizer.T("Language");
        ((ComboBoxItem)LanguageComboBox.Items[0]).Content = _localizer.T("Chinese");
        ((ComboBoxItem)LanguageComboBox.Items[1]).Content = _localizer.T("English");
        if (HealthStateText.Text == "尚未检测" || HealthStateText.Text == "Not checked")
        {
            HealthStateText.Text = _localizer.T("NotChecked");
        }
    }

    private void ShowPanel(UIElement panel)
    {
        foreach (var item in new UIElement[] { StatusPanel, WifiPanel, PolicyPanel, ToolsPanel, LogPanel, SettingsPanel })
        {
            item.Visibility = item == panel ? Visibility.Visible : Visibility.Collapsed;
        }

        PageTitleText.Text = panel == StatusPanel ? _localizer.T("Status")
            : panel == WifiPanel ? _localizer.T("Wifi")
            : panel == PolicyPanel ? _localizer.T("Policy")
            : panel == ToolsPanel ? _localizer.T("Tools")
            : panel == LogPanel ? _localizer.T("Log")
            : _localizer.T("Settings");
        PageSubtitleText.Text = panel == StatusPanel ? _localizer.T("Summary") : "";
    }

    private void Log(string message)
    {
        LogTextBox.AppendText($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private sealed record ProbeRow(string Name, bool Ok, string LatencyText, string Detail);
}
