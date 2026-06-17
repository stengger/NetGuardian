using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;
using NetGuardian.Core.Settings;
using NetGuardian.Core.Wifi;

namespace NetGuardian.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly Localizer _localizer = new();
    private readonly IWifiClient _wifi = new WifiClient();
    private readonly ObservableCollection<WifiNetwork> _nearbyNetworks = [];
    private readonly ObservableCollection<WifiProfile> _profiles = [];
    private readonly ObservableCollection<string> _recentEvents = [];
    private readonly ObservableCollection<DnsServerRow> _dnsRows = [];
    private readonly ObservableCollection<AdapterRow> _adapterRows = [];
    private readonly DispatcherTimer _monitorTimer = new();
    private readonly DispatcherTimer _countdownTimer = new();
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _isChecking;
    private bool _isMonitoring;
    private bool _forceExit;
    private int _failureStreak;
    private DateTimeOffset? _nextCheckAt;
    private Point _dragStartPoint;
    private WifiProfile? _draggedProfile;

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
        RecentEventsList.ItemsSource = _recentEvents;
        DnsGrid.ItemsSource = _dnsRows;
        AdapterGrid.ItemsSource = _adapterRows;

        _monitorTimer.Tick += async (_, _) => await RunCheckAsync();
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (_, _) => UpdateMonitorStatus();
        _countdownTimer.Start();

        _notifyIcon = CreateNotifyIcon();
        ApplySettingsToUi();
        ApplyLanguage();
        RefreshDnsInfo();
        RefreshAdapterInfo();
        ShowPanel(StatusPanel);
        UpdateMonitorStatus();

        if (_settings.StartMonitoringOnLaunch)
        {
            _ = Dispatcher.BeginInvoke(async () => await StartMonitoringAsync());
        }

        if (_settings.StartMinimizedToTray)
        {
            HideToTray();
        }
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e) => await RunCheckAsync();
    private async void StartMonitor_Click(object sender, RoutedEventArgs e) => await StartMonitoringAsync();

    private void StopMonitor_Click(object sender, RoutedEventArgs e)
    {
        StopMonitoring();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(ScanWifiAsync);

    private async void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        if (NearbyGrid.SelectedItem is WifiNetwork network)
        {
            await RunUiActionAsync(() => AddWifiProfileAsync(network));
        }
    }

    private async void AddManual_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => AddWifiProfileAsync(null));
    }

    private async void SetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileGrid.SelectedItem is WifiProfile profile)
        {
            await RunUiActionAsync(() => ConfigureProfileCredentialAsync(profile));
        }
        else if (NearbyGrid.SelectedItem is WifiNetwork network)
        {
            await RunUiActionAsync(() => AddWifiProfileAsync(network));
        }
    }

    private async void NearbyGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GetRowItemAtPoint<WifiNetwork>(NearbyGrid, e.GetPosition(NearbyGrid)) is WifiNetwork network)
        {
            await RunUiActionAsync(() => AddWifiProfileAsync(network));
        }
    }

    private async void ProfileGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GetRowItemAtPoint<WifiProfile>(ProfileGrid, e.GetPosition(ProfileGrid)) is WifiProfile profile)
        {
            await RunUiActionAsync(() => ConfigureProfileCredentialAsync(profile));
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelectedProfile(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelectedProfile(1);

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileGrid.SelectedItem is not WifiProfile profile)
        {
            return;
        }

        _profiles.Remove(profile);
        RenumberProfiles();
        SaveSettings();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileGrid.SelectedItem is not WifiProfile profile)
        {
            return;
        }

        await RunUiActionAsync(async () =>
        {
            Log($"Connecting to {profile.Ssid}.");
            var ok = await _wifi.ConnectAsync(profile.Ssid);
            Log(ok ? $"Connection command sent: {profile.Ssid}" : $"Connection command failed: {profile.Ssid}");
        });
    }

    private void SavePolicy_Click(object sender, RoutedEventArgs e)
    {
        SavePolicyFromUi();
        SaveSettings();
        Log("Policy saved.");
        UpdateMonitorStatus();
    }

    private async void ToolsFlushDns_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () => await RunSystemNetworkCommandAsync("ipconfig.exe", "/flushdns", _localizer.T("FlushDns")));
        RefreshDnsInfo();
    }

    private void DnsRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshDnsInfo();
        SetToolOutput(_localizer.T("DnsRefreshed"));
    }

    private async void ToolsRenewDhcp_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () => await RunSystemNetworkCommandAsync("ipconfig.exe", "/renew", _localizer.T("RenewDhcp")));
        RefreshAdapterInfo();
    }

    private void AdapterRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAdapterInfo();
        SetToolOutput(_localizer.T("AdapterRefreshed"));
    }

    private void ToolsNetworkSettings_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:network-status",
            UseShellExecute = true
        });
        SetToolOutput(_localizer.T("OpenNetworkSettings"));
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

    private void CloseBehaviorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CloseBehaviorComboBox.SelectedItem is ComboBoxItem item && item.Tag is string behavior)
        {
            _settings.CloseBehavior = behavior;
            SaveSettings();
        }
    }

    private void SettingsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _settings.StartMonitoringOnLaunch = StartMonitoringOnLaunchCheckBox.IsChecked == true;
        _settings.StartMinimizedToTray = StartMinimizedToTrayCheckBox.IsChecked == true;
        SaveSettings();
    }

    private void ProfileEnabledToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        SaveSettings();
    }

    private void RootNavigation_SelectionChanged(Wpf.Ui.Controls.NavigationView sender, RoutedEventArgs args)
    {
        if (sender.SelectedItem is Wpf.Ui.Controls.NavigationViewItem item)
        {
            ShowPanelByTag(item.Tag?.ToString());
        }
    }

    private void NavigationItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.NavigationViewItem item)
        {
            ShowPanelByTag(item.Tag?.ToString());
        }
    }

    private void ShowPanelByTag(string? tag)
    {
        ShowPanel(tag switch
        {
            "WifiPanel" => WifiPanel,
            "PolicyPanel" => PolicyPanel,
            "ToolsPanel" => ToolsPanel,
            "DnsPanel" => DnsPanel,
            "NetworkPanel" => NetworkPanel,
            "LogPanel" => LogPanel,
            "SettingsPanel" => SettingsPanel,
            _ => StatusPanel
        });
    }

    private async Task StartMonitoringAsync()
    {
        SavePolicyFromUi();
        _monitorTimer.Interval = TimeSpan.FromSeconds(_settings.Policy.CheckIntervalSeconds);
        _monitorTimer.Start();
        _isMonitoring = true;
        _nextCheckAt = DateTimeOffset.Now.AddSeconds(_settings.Policy.CheckIntervalSeconds);
        Log("Monitor started.");
        UpdateMonitorStatus();
        await RunCheckAsync();
    }

    private void StopMonitoring()
    {
        _monitorTimer.Stop();
        _isMonitoring = false;
        _nextCheckAt = null;
        Log("Monitor stopped.");
        UpdateMonitorStatus();
    }

    private async Task RunCheckAsync()
    {
        if (_isChecking)
        {
            return;
        }

        _isChecking = true;
        CheckNowButton.IsEnabled = false;
        UpdateMonitorStatus();
        try
        {
            var currentSsid = await _wifi.GetCurrentSsidAsync();
            var tester = new ConnectivityTester(_settings.Policy);
            var report = await tester.RunFullCheckAsync(currentSsid);
            ApplyReport(report);
            _failureStreak = report.Ok ? 0 : _failureStreak + 1;
            Log($"{report.CheckedAt:yyyy-MM-dd HH:mm:ss}  {report.State}: {report.Summary}");
            SetToolOutput($"{StateText(report.State)}{Environment.NewLine}{report.Summary}");
        }
        catch (Exception ex)
        {
            _failureStreak++;
            Log(ex.Message);
        }
        finally
        {
            if (_isMonitoring)
            {
                _nextCheckAt = DateTimeOffset.Now.AddSeconds(_settings.Policy.CheckIntervalSeconds);
            }

            CheckNowButton.IsEnabled = true;
            _isChecking = false;
            UpdateMonitorStatus();
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
            SetToolOutput(string.Format(_localizer.T("ScanDone"), _nearbyNetworks.Count));
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

    private async Task AddWifiProfileAsync(WifiNetwork? network)
    {
        var dialog = new AddWifiDialog(_localizer, network) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Ssid))
        {
            return;
        }

        var ssid = dialog.Ssid.Trim();
        var existing = _profiles.FirstOrDefault(profile => profile.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            await ConfigureProfileCredentialAsync(existing);
            return;
        }

        var credentialSaved = false;
        if (dialog.SaveToWindows)
        {
            credentialSaved = await _wifi.SaveProfileAsync(ssid, dialog.Password, dialog.Authentication);
            Log(credentialSaved ? $"Wi-Fi profile saved to Windows: {ssid}" : $"Failed to save Wi-Fi profile: {ssid}");
        }

        _profiles.Add(new WifiProfile
        {
            Ssid = ssid,
            Priority = _profiles.Count + 1,
            CredentialSaved = credentialSaved,
            Security = dialog.Authentication,
            Enabled = true,
            AutoConnect = true
        });
        RenumberProfiles();
        SaveSettings();
    }

    private async Task ConfigureProfileCredentialAsync(WifiProfile profile)
    {
        var network = new WifiNetwork(
            profile.Ssid,
            0,
            string.IsNullOrWhiteSpace(profile.Security) ? "WPA2-Personal" : profile.Security,
            "",
            true,
            true);
        var dialog = new AddWifiDialog(_localizer, network) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Ssid))
        {
            return;
        }

        var ssid = dialog.Ssid.Trim();
        if (!ssid.Equals(profile.Ssid, StringComparison.OrdinalIgnoreCase)
            && _profiles.Any(item => item.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase)))
        {
            Log($"Wi-Fi profile already exists: {ssid}");
            return;
        }

        var credentialSaved = profile.CredentialSaved;
        if (dialog.SaveToWindows)
        {
            credentialSaved = await _wifi.SaveProfileAsync(ssid, dialog.Password, dialog.Authentication);
            Log(credentialSaved ? $"Wi-Fi credential saved to Windows: {ssid}" : $"Failed to save Wi-Fi credential: {ssid}");
        }

        profile.Ssid = ssid;
        profile.Security = dialog.Authentication;
        profile.CredentialSaved = credentialSaved;
        ProfileGrid.Items.Refresh();
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

    private void ProfileGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedProfile = GetRowItemAtPoint(e.GetPosition(ProfileGrid));
    }

    private void ProfileGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedProfile is null)
        {
            return;
        }

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(current.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            DragDrop.DoDragDrop(ProfileGrid, _draggedProfile, DragDropEffects.Move);
        }
    }

    private void ProfileGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(WifiProfile)))
        {
            return;
        }

        var source = (WifiProfile)e.Data.GetData(typeof(WifiProfile))!;
        var target = GetRowItemAtPoint(e.GetPosition(ProfileGrid));
        if (target is null || ReferenceEquals(source, target))
        {
            return;
        }

        var sourceIndex = _profiles.IndexOf(source);
        var targetIndex = _profiles.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        _profiles.Move(sourceIndex, targetIndex);
        RenumberProfiles();
        SaveSettings();
    }

    private WifiProfile? GetRowItemAtPoint(Point point)
    {
        return GetRowItemAtPoint<WifiProfile>(ProfileGrid, point);
    }

    private static T? GetRowItemAtPoint<T>(DataGrid grid, Point point) where T : class
    {
        var element = grid.InputHitTest(point) as DependencyObject;
        while (element is not null and not DataGridRow)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        return element is DataGridRow row ? row.Item as T : null;
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
            NetworkState.Healthy => new SolidColorBrush(Color.FromRgb(105, 209, 139)),
            NetworkState.Recovering => new SolidColorBrush(Color.FromRgb(93, 174, 120)),
            NetworkState.GatewayIssue or NetworkState.DnsIssue or NetworkState.InternetIssue => new SolidColorBrush(Color.FromRgb(217, 164, 65)),
            _ => new SolidColorBrush(Color.FromRgb(239, 107, 91))
        };
        SummaryText.Text = report.Summary;
        CurrentSsidText.Text = string.IsNullOrWhiteSpace(report.CurrentSsid) ? "-" : report.CurrentSsid;
        ProbeGrid.ItemsSource = report.Probes.Select(probe => new ProbeRow(
            LocalizeProbeName(probe.Name),
            probe.Ok ? _localizer.T("Pass") : _localizer.T("Fail"),
            probe.LatencyMs.HasValue ? $"{probe.LatencyMs.Value:F0} ms" : "-",
            LocalizeProbeDetail(probe.Detail)));
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

    private string LocalizeProbeName(string name)
    {
        if (name.Equals("Default gateway", StringComparison.OrdinalIgnoreCase))
        {
            return _localizer.T("DefaultGateway");
        }

        if (name.Equals("Gateway ping", StringComparison.OrdinalIgnoreCase))
        {
            return _localizer.T("GatewayPing");
        }

        return name;
    }

    private string LocalizeProbeDetail(string detail)
    {
        return detail.Equals("Resolved", StringComparison.OrdinalIgnoreCase)
            ? _localizer.T("Resolved")
            : detail;
    }

    private void ApplySettingsToUi()
    {
        SetTimeFields(_settings.Policy.CheckIntervalSeconds, IntervalTextBox, IntervalUnitComboBox);
        SetTimeFields(_settings.Policy.RecoveryCooldownSeconds, CooldownTextBox, CooldownUnitComboBox);
        FailuresTextBox.Text = _settings.Policy.ConsecutiveFailuresBeforeRecovery.ToString();
        AutoSwitchCheckBox.IsChecked = _settings.Policy.EnableAutoSwitch;
        SoftReconnectCheckBox.IsChecked = _settings.Policy.EnableCurrentWifiReconnect;
        DnsFlushCheckBox.IsChecked = _settings.Policy.EnableDnsFlush;
        StartMonitoringOnLaunchCheckBox.IsChecked = _settings.StartMonitoringOnLaunch;
        StartMinimizedToTrayCheckBox.IsChecked = _settings.StartMinimizedToTray;
        SelectComboByTag(LanguageComboBox, _settings.Language);
        SelectComboByTag(CloseBehaviorComboBox, _settings.CloseBehavior);
    }

    private static void SetTimeFields(int seconds, TextBox textBox, ComboBox comboBox)
    {
        var unit = seconds % 3600 == 0 ? 3600 : seconds % 60 == 0 ? 60 : 1;
        textBox.Text = Math.Max(1, seconds / unit).ToString();
        SelectComboByTag(comboBox, unit.ToString());
    }

    private void SavePolicyFromUi()
    {
        _settings.Policy.CheckIntervalSeconds = ReadTimeSeconds(IntervalTextBox, IntervalUnitComboBox, _settings.Policy.CheckIntervalSeconds);
        _settings.Policy.RecoveryCooldownSeconds = ReadTimeSeconds(CooldownTextBox, CooldownUnitComboBox, _settings.Policy.RecoveryCooldownSeconds);

        if (int.TryParse(FailuresTextBox.Text, out var failures))
        {
            _settings.Policy.ConsecutiveFailuresBeforeRecovery = failures;
        }

        _settings.Policy.EnableAutoSwitch = AutoSwitchCheckBox.IsChecked == true;
        _settings.Policy.EnableCurrentWifiReconnect = SoftReconnectCheckBox.IsChecked == true;
        _settings.Policy.EnableDnsFlush = DnsFlushCheckBox.IsChecked == true;
        _settings.Policy.Normalize();
        ApplySettingsToUi();
    }

    private static int ReadTimeSeconds(TextBox textBox, ComboBox comboBox, int fallback)
    {
        if (!int.TryParse(textBox.Text, out var value) || value < 1)
        {
            return fallback;
        }

        var multiplier = comboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var tag) ? tag : 1;
        return value * multiplier;
    }

    private static void SelectComboByTag(ComboBox comboBox, string tag)
    {
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private void SaveSettings()
    {
        _settings.Profiles = _profiles.ToList();
        _store.Save(_settings);
    }

    private void ApplyLanguage()
    {
        Title = _localizer.T("AppTitle");
        TitleBar.Title = _localizer.T("AppTitle");
        StatusNavButton.Content = _localizer.T("Status");
        WifiNavButton.Content = _localizer.T("Wifi");
        PolicyNavButton.Content = _localizer.T("Policy");
        ToolsNavButton.Content = _localizer.T("Tools");
        DnsNavButton.Content = _localizer.T("DnsTools");
        AdapterNavButton.Content = _localizer.T("AdapterTools");
        LogNavButton.Content = _localizer.T("Log");
        SettingsNavButton.Content = _localizer.T("Settings");
        CheckNowButton.Content = _localizer.T("CheckNow");
        StartMonitorButton.Content = _isMonitoring ? _localizer.T("Monitoring") : _localizer.T("StartMonitor");
        StopButton.Content = _localizer.T("Stop");
        HealthStateLabel.Text = _localizer.T("HealthState");
        MonitorStateLabel.Text = _localizer.T("MonitorStatus");
        NextCheckLabel.Text = _localizer.T("NextCheck");
        CurrentNetworkLabel.Text = _localizer.T("CurrentNetwork");
        FailureStreakLabel.Text = _localizer.T("FailureStreak");
        ProbeTitleText.Text = _localizer.T("CheckNow");
        RecentEventsTitle.Text = _localizer.T("RecentEvents");
        ProbeNameColumn.Header = _localizer.T("Probe");
        ProbeOkColumn.Header = _localizer.T("Result");
        ProbeLatencyColumn.Header = _localizer.T("Latency");
        ProbeDetailColumn.Header = _localizer.T("Detail");
        ScanButton.Content = _localizer.T("Scan");
        AddSelectedButton.Content = _localizer.T("AddSelected");
        AddManualButton.Content = _localizer.T("AddManual");
        MoveUpButton.Content = _localizer.T("MoveUp");
        MoveDownButton.Content = _localizer.T("MoveDown");
        RemoveButton.Content = _localizer.T("Remove");
        SetPasswordButton.Content = _localizer.T("SetPassword");
        ConnectButton.Content = _localizer.T("Connect");
        DragHintText.Text = _localizer.T("DoubleClickWifiHint");
        NearbyNetworksTitle.Text = _localizer.T("NearbyNetworks");
        PriorityListTitle.Text = _localizer.T("PriorityList");
        NearbySsidColumn.Header = _localizer.T("Ssid");
        NearbySignalColumn.Header = _localizer.T("Signal");
        NearbySecurityColumn.Header = _localizer.T("Security");
        ProfileDragColumn.Header = "";
        ProfileSsidColumn.Header = _localizer.T("Ssid");
        ProfilePriorityColumn.Header = _localizer.T("Priority");
        ProfileCredentialColumn.Header = _localizer.T("CredentialSaved");
        ProfileEnabledColumn.Header = _localizer.T("Enabled");
        ProfileFailuresColumn.Header = _localizer.T("Failures");
        PolicyTitle.Text = _localizer.T("Policy");
        IntervalLabel.Text = _localizer.T("IntervalValue");
        FailuresBeforeRecoveryLabel.Text = _localizer.T("FailuresBeforeRecovery");
        CooldownLabel.Text = _localizer.T("CooldownValue");
        AutoSwitchCheckBox.Content = _localizer.T("AutoSwitch");
        SoftReconnectCheckBox.Content = _localizer.T("SoftReconnect");
        DnsFlushCheckBox.Content = _localizer.T("DnsFlush");
        SavePolicyButton.Content = _localizer.T("Save");
        ToolsTitle.Text = _localizer.T("SystemNetworkTools");
        ToolsCheckButton.Content = _localizer.T("CheckNow");
        ToolsScanButton.Content = _localizer.T("Scan");
        ToolsNetworkSettingsButton.Content = _localizer.T("OpenNetworkSettings");
        HighRiskText.Text = _localizer.T("HighRiskReserved");
        DnsToolsTitle.Text = _localizer.T("DnsTools");
        DnsRefreshButton.Content = _localizer.T("RefreshStatus");
        DnsFlushButton.Content = _localizer.T("FlushDns");
        DnsToolsHint.Text = _localizer.T("DnsToolsHint");
        DnsAdapterColumn.Header = _localizer.T("Adapter");
        DnsServersColumn.Header = _localizer.T("DnsServers");
        DnsSourceColumn.Header = _localizer.T("Source");
        DnsStatusColumn.Header = _localizer.T("StatusColumn");
        AdapterToolsTitle.Text = _localizer.T("AdapterTools");
        AdapterRefreshButton.Content = _localizer.T("RefreshStatus");
        AdapterRenewDhcpButton.Content = _localizer.T("RenewDhcp");
        AdapterSettingsButton.Content = _localizer.T("OpenNetworkSettings");
        AdapterToolsHint.Text = _localizer.T("AdapterToolsHint");
        AdapterNameColumn.Header = _localizer.T("Adapter");
        AdapterTypeColumn.Header = _localizer.T("Type");
        AdapterStatusColumn.Header = _localizer.T("StatusColumn");
        AdapterIpv4Column.Header = "IPv4";
        AdapterGatewayColumn.Header = _localizer.T("Gateway");
        AdapterDnsColumn.Header = _localizer.T("DnsServers");
        SettingsTitle.Text = _localizer.T("Preferences");
        LanguageLabel.Text = _localizer.T("Language");
        TrayTitle.Text = _localizer.T("Tray");
        CloseBehaviorLabel.Text = _localizer.T("CloseBehavior");
        StartMonitoringOnLaunchCheckBox.Content = _localizer.T("StartMonitoringOnLaunch");
        StartMinimizedToTrayCheckBox.Content = _localizer.T("StartMinimizedToTray");
        ((ComboBoxItem)LanguageComboBox.Items[0]).Content = _localizer.T("Chinese");
        ((ComboBoxItem)LanguageComboBox.Items[1]).Content = _localizer.T("English");
        ((ComboBoxItem)IntervalUnitComboBox.Items[0]).Content = _localizer.T("Seconds");
        ((ComboBoxItem)IntervalUnitComboBox.Items[1]).Content = _localizer.T("Minutes");
        ((ComboBoxItem)IntervalUnitComboBox.Items[2]).Content = _localizer.T("Hours");
        ((ComboBoxItem)CooldownUnitComboBox.Items[0]).Content = _localizer.T("Seconds");
        ((ComboBoxItem)CooldownUnitComboBox.Items[1]).Content = _localizer.T("Minutes");
        ((ComboBoxItem)CooldownUnitComboBox.Items[2]).Content = _localizer.T("Hours");
        ((ComboBoxItem)CloseBehaviorComboBox.Items[0]).Content = _localizer.T("CloseAsk");
        ((ComboBoxItem)CloseBehaviorComboBox.Items[1]).Content = _localizer.T("CloseExit");
        ((ComboBoxItem)CloseBehaviorComboBox.Items[2]).Content = _localizer.T("CloseTray");
        if (HealthStateText.Text == "" || HealthStateText.Text == "尚未检测" || HealthStateText.Text == "Not checked")
        {
            HealthStateText.Text = _localizer.T("NotChecked");
        }

        UpdateMonitorStatus();
        UpdateTrayText();
    }

    private void ShowPanel(UIElement panel)
    {
        foreach (var item in new UIElement[] { StatusPanel, WifiPanel, PolicyPanel, ToolsPanel, DnsPanel, NetworkPanel, LogPanel, SettingsPanel })
        {
            item.Visibility = item == panel ? Visibility.Visible : Visibility.Collapsed;
        }

        PageTitleText.Text = panel == StatusPanel ? _localizer.T("Status")
            : panel == WifiPanel ? _localizer.T("Wifi")
            : panel == PolicyPanel ? _localizer.T("Policy")
            : panel == ToolsPanel ? _localizer.T("Tools")
            : panel == DnsPanel ? _localizer.T("DnsTools")
            : panel == NetworkPanel ? _localizer.T("AdapterTools")
            : panel == LogPanel ? _localizer.T("Log")
            : _localizer.T("Settings");
        PageSubtitleText.Text = panel == StatusPanel ? _localizer.T("Summary") : "";
    }

    private void UpdateMonitorStatus()
    {
        var monitorState = _isMonitoring ? _localizer.T("Monitoring") : _localizer.T("Stopped");
        var activityState = _isChecking ? _localizer.T("Checking") : _localizer.T("Ready");
        MonitorStateText.Text = monitorState;
        HeaderStateText.Text = activityState;
        StatusDot.Fill = _isChecking
            ? new SolidColorBrush(Color.FromRgb(217, 164, 65))
            : new SolidColorBrush(Color.FromRgb(142, 150, 137));
        StartMonitorButton.Content = _isMonitoring ? _localizer.T("Monitoring") : _localizer.T("StartMonitor");
        StartMonitorButton.IsEnabled = !_isMonitoring || !_isChecking;
        StopButton.IsEnabled = _isMonitoring;
        FailureStreakText.Text = _failureStreak.ToString();

        if (_isMonitoring && _nextCheckAt.HasValue)
        {
            var remaining = _nextCheckAt.Value - DateTimeOffset.Now;
            NextCheckText.Text = remaining <= TimeSpan.Zero ? "0s" : FormatDuration(remaining);
        }
        else
        {
            NextCheckText.Text = "-";
        }

        UpdateTrayText();
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private async Task RunSystemNetworkCommandAsync(string fileName, string arguments, string label)
    {
        Log($"{label}...");
        SetToolOutput($"{label}...");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = (await outputTask + await errorTask).Trim();
        var status = process.ExitCode == 0
            ? string.Format(_localizer.T("CommandDone"), label)
            : string.Format(_localizer.T("CommandFailed"), label, process.ExitCode);
        Log(status);

        if (!string.IsNullOrWhiteSpace(output))
        {
            Log(output);
        }

        SetToolOutput(string.IsNullOrWhiteSpace(output) ? status : $"{status}{Environment.NewLine}{Environment.NewLine}{output}");
    }

    private void SetToolOutput(string text)
    {
        if (ToolsPanel.Visibility == Visibility.Visible)
        {
            ToolsOutputText.Text = text;
        }

        if (DnsPanel.Visibility == Visibility.Visible)
        {
            DnsStatusText.Text = text;
        }

        if (NetworkPanel.Visibility == Visibility.Visible)
        {
            AdapterStatusText.Text = text;
        }
    }

    private void RefreshDnsInfo()
    {
        _dnsRows.Clear();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(item => item.OperationalStatus == OperationalStatus.Up)
            .ThenBy(item => item.Name))
        {
            var properties = networkInterface.GetIPProperties();
            var servers = properties.DnsAddresses
                .Where(address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                .Select(address => address.ToString())
                .ToList();

            if (servers.Count == 0 && networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            _dnsRows.Add(new DnsServerRow(
                networkInterface.Name,
                servers.Count == 0 ? "-" : string.Join(", ", servers),
                _localizer.T("SystemConfigured"),
                LocalizeOperationalStatus(networkInterface.OperationalStatus)));
        }

        DnsStatusText.Text = _dnsRows.Count == 0
            ? _localizer.T("NoDnsServers")
            : string.Format(_localizer.T("DnsServerCount"), _dnsRows.Count);
    }

    private void RefreshAdapterInfo()
    {
        _adapterRows.Clear();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(item => item.OperationalStatus == OperationalStatus.Up)
            .ThenBy(item => item.Name))
        {
            var properties = networkInterface.GetIPProperties();
            var ipv4 = properties.UnicastAddresses
                .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => item.Address.ToString())
                .ToList();
            var gateways = properties.GatewayAddresses
                .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => item.Address.ToString())
                .ToList();
            var dns = properties.DnsAddresses
                .Where(item => item.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => item.ToString())
                .ToList();

            _adapterRows.Add(new AdapterRow(
                networkInterface.Name,
                networkInterface.NetworkInterfaceType.ToString(),
                LocalizeOperationalStatus(networkInterface.OperationalStatus),
                ipv4.Count == 0 ? "-" : string.Join(", ", ipv4),
                gateways.Count == 0 ? "-" : string.Join(", ", gateways),
                dns.Count == 0 ? "-" : string.Join(", ", dns)));
        }

        AdapterStatusText.Text = string.Format(_localizer.T("AdapterCount"), _adapterRows.Count);
    }

    private string LocalizeOperationalStatus(OperationalStatus status)
    {
        return status switch
        {
            OperationalStatus.Up => _localizer.T("AdapterUp"),
            OperationalStatus.Down => _localizer.T("AdapterDown"),
            OperationalStatus.Testing => _localizer.T("AdapterTesting"),
            _ => status.ToString()
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return duration.TotalMinutes >= 1 ? $"{duration.Minutes}m {duration.Seconds}s" : $"{Math.Max(0, duration.Seconds)}s";
    }

    private void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
        LogTextBox.AppendText(line + Environment.NewLine);
        LogTextBox.ScrollToEnd();
        _recentEvents.Insert(0, line);
        while (_recentEvents.Count > 6)
        {
            _recentEvents.RemoveAt(_recentEvents.Count - 1);
        }
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location);
        var notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "NetGuardian"
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_localizer.T("ShowWindow"), null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add(_localizer.T("CheckNow"), null, (_, _) => Dispatcher.Invoke(async () => await RunCheckAsync()));
        menu.Items.Add(_localizer.T("StartMonitor"), null, (_, _) => Dispatcher.Invoke(async () => await StartMonitoringAsync()));
        menu.Items.Add(_localizer.T("Stop"), null, (_, _) => Dispatcher.Invoke(StopMonitoring));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_localizer.T("Exit"), null, (_, _) => Dispatcher.Invoke(ExitApplication));
        notifyIcon.ContextMenuStrip = menu;
        notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        return notifyIcon;
    }

    private void UpdateTrayText()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var state = _isMonitoring ? _localizer.T("Monitoring") : _localizer.T("Stopped");
        _notifyIcon.Text = $"NetGuardian - {state}";
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        Hide();
        _notifyIcon.Visible = true;
    }

    private void ExitApplication()
    {
        _forceExit = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Close();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.StartMinimizedToTray)
        {
            HideToTray();
        }
    }

    private bool _isCloseDialogShowing = false;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_forceExit)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            return;
        }

        if (_settings.CloseBehavior == "tray")
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (_settings.CloseBehavior == "ask")
        {
            e.Cancel = true;
            if (_isCloseDialogShowing) return;
            _isCloseDialogShowing = true;
            ShowCloseDialogAsync();
        }
    }

    private async void ShowCloseDialogAsync()
    {
        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = _localizer.T("ClosePromptTitle"),
            Content = _localizer.T("ClosePrompt"),
            PrimaryButtonText = "后台运行",
            SecondaryButtonText = "直接退出",
            CloseButtonText = "取消",
            IsPrimaryButtonEnabled = true,
            IsSecondaryButtonEnabled = true
        };

        var result = await messageBox.ShowDialogAsync();
        _isCloseDialogShowing = false;

        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            HideToTray();
        }
        else if (result == Wpf.Ui.Controls.MessageBoxResult.Secondary)
        {
            _forceExit = true;
            Application.Current.Shutdown();
        }
    }

    private sealed record ProbeRow(string Name, string ResultText, string LatencyText, string Detail);
    private sealed record DnsServerRow(string Adapter, string Servers, string Source, string Status);
    private sealed record AdapterRow(string Name, string Type, string Status, string Ipv4, string Gateway, string DnsServers);
}
