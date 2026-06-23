using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
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
using NetGuardian.Core.Network;
using NetGuardian.Core.Settings;
using NetGuardian.Core.Wifi;

namespace NetGuardian.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private const int MaxLogLines = 500;
    private const string ScheduledStartupTaskName = "NetGuardian";
    private static readonly TimeSpan WifiValidationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan WifiRestoreTimeout = TimeSpan.FromSeconds(10);
    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly Localizer _localizer = new();
    private readonly IWifiClient _wifi = new WifiClient();
    private readonly NetworkConfigurationService _networkConfiguration = new();
    private readonly WebsiteTestService _websiteTester = new();
    private readonly PublicIpInfoService _publicIpInfo = new();
    private readonly NetworkSpeedTestService _speedTester = new();
    private readonly ObservableCollection<NearbyWifiRow> _nearbyNetworks = [];
    private readonly ObservableCollection<WifiProfile> _profiles = [];
    private readonly ObservableCollection<string> _recentEvents = [];
    private readonly ObservableCollection<DnsServerRow> _dnsRows = [];
    private readonly ObservableCollection<DnsAdapterOption> _dnsAdapterOptions = [];
    private readonly ObservableCollection<AdapterRow> _adapterRows = [];
    private readonly ObservableCollection<QualityProbeRow> _qualityRows = [];
    private readonly ObservableCollection<WebsiteTestRow> _websiteRows = [];
    private readonly ObservableCollection<IpInfoRow> _ipInfoRows = [];
    private readonly ObservableCollection<DiagnosticReportRow> _reportRows = [];
    private readonly DispatcherTimer _monitorTimer = new();
    private readonly DispatcherTimer _countdownTimer = new();
    private readonly DispatcherTimer _sidebarSpeedTimer = new();
    private readonly Queue<double> _downloadSpeedSamples = new();
    private readonly Queue<double> _uploadSpeedSamples = new();
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _isChecking;
    private bool _isMonitoring;
    private bool _forceExit;
    private bool _isSyncingDnsControls;
    private bool _isSyncingWifiSelection;
    private bool _isSyncingScheduledStartup;
    private bool _isNotifyIconDisposed;
    private bool _resourcesCleaned;
    private int _logLineCount;
    private int _failureStreak;
    private DateTimeOffset? _nextCheckAt;
    private DateTimeOffset? _lastRecoveryAttemptAt;
    private DateTimeOffset? _lastSidebarSpeedSampleAt;
    private long? _lastSidebarReceivedBytes;
    private long? _lastSidebarSentBytes;
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
        DnsAdapterComboBox.ItemsSource = _dnsAdapterOptions;
        DnsAdapterComboBox.DisplayMemberPath = nameof(DnsAdapterOption.Name);
        AdapterGrid.ItemsSource = _adapterRows;
        QualityProbeGrid.ItemsSource = _qualityRows;
        WebsiteGrid.ItemsSource = _websiteRows;
        IpInfoGrid.ItemsSource = _ipInfoRows;
        ReportGrid.ItemsSource = _reportRows;

        _monitorTimer.Tick += MonitorTimer_Tick;
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (_, _) => UpdateMonitorStatus();
        _countdownTimer.Start();
        _sidebarSpeedTimer.Interval = TimeSpan.FromSeconds(1);
        _sidebarSpeedTimer.Tick += (_, _) => UpdateSidebarSpeed();
        _sidebarSpeedTimer.Start();

        _notifyIcon = CreateNotifyIcon();
        ApplySettingsToUi();
        ApplyLanguage();
        InitializeDnsControls();
        ResetQualityUi();
        ResetSpeedUi();
        ResetIpInfoUi();
        ResetReportUi();
        RefreshDnsInfo();
        RefreshAdapterInfo();
        ShowPanel(StatusPanel);
        UpdateMonitorStatus();
        UpdateSidebarSpeed();
        _ = Dispatcher.BeginInvoke(async () => await RunUiActionAsync(CleanupTemporaryWifiProfilesAsync));
        _ = Dispatcher.BeginInvoke(async () => await RunUiActionAsync(RefreshScheduledStartupSettingAsync));

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

    private async void MonitorTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            await RunCheckAsync();
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private void StopMonitor_Click(object sender, RoutedEventArgs e)
    {
        StopMonitoring();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunUiActionAsync(ScanWifiAsync);

    private async void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        if (NearbyGrid.SelectedItem is NearbyWifiRow network)
        {
            await RunUiActionAsync(() => AddWifiProfileAsync(network));
            return;
        }

        SetWifiSelectionMessage(_localizer.T("SelectNearbyWifiFirst"));
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
        else if (NearbyGrid.SelectedItem is NearbyWifiRow network)
        {
            await RunUiActionAsync(() => AddWifiProfileAsync(network));
            return;
        }

        SetWifiSelectionMessage(_localizer.T("SelectWifiFirst"));
    }

    private async void ImportPassword_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(ImportSelectedWifiPasswordAsync);
    }

    private async void NearbyGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GetRowItemAtPoint<NearbyWifiRow>(NearbyGrid, e.GetPosition(NearbyGrid)) is NearbyWifiRow network)
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

    private void NearbyGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingWifiSelection)
        {
            UpdateWifiSelectionUi();
            return;
        }

        if (NearbyGrid.SelectedItem is not null && ProfileGrid.SelectedItem is not null)
        {
            _isSyncingWifiSelection = true;
            ProfileGrid.SelectedItem = null;
            _isSyncingWifiSelection = false;
        }

        UpdateWifiSelectionUi();
    }

    private void ProfileGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingWifiSelection)
        {
            UpdateWifiSelectionUi();
            return;
        }

        if (ProfileGrid.SelectedItem is not null && NearbyGrid.SelectedItem is not null)
        {
            _isSyncingWifiSelection = true;
            NearbyGrid.SelectedItem = null;
            _isSyncingWifiSelection = false;
        }

        UpdateWifiSelectionUi();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelectedProfile(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelectedProfile(1);

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileGrid.SelectedItem is not WifiProfile profile)
        {
            SetWifiSelectionMessage(_localizer.T("SelectProfileFirst"));
            return;
        }

        _profiles.Remove(profile);
        RenumberProfiles();
        UpdateEmptyStates();
        SaveSettings();
        RefreshNearbyPriorityMarkers();
        UpdateWifiSelectionUi();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileGrid.SelectedItem is not WifiProfile profile)
        {
            SetWifiSelectionMessage(_localizer.T("SelectProfileFirst"));
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
        var status = string.Format(
            _localizer.T("PolicySavedDetails"),
            FormatDuration(TimeSpan.FromSeconds(_settings.Policy.CheckIntervalSeconds)),
            _settings.Policy.ConsecutiveFailuresBeforeRecovery,
            FormatDuration(TimeSpan.FromSeconds(_settings.Policy.RecoveryCooldownSeconds)));
        PolicyStatusText.Text = status;
        Log(status);
        UpdateMonitorStatus();
    }

    private async void ToolsFlushDns_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () => await RunSystemNetworkCommandAsync("ipconfig.exe", ["/flushdns"], _localizer.T("FlushDns")));
        RefreshDnsInfo();
    }

    private void DnsRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshDnsInfo();
        SetToolOutput(_localizer.T("DnsRefreshed"));
    }

    private void DnsAdapterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingDnsControls)
        {
            return;
        }

        FillDnsFieldsFromSelectedAdapter();
    }

    private void DnsPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingDnsControls)
        {
            return;
        }

        ApplyDnsPresetToFields();
    }

    private async void DnsApply_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(ApplyDnsSettingsAsync);
    }

    private async void ToolsRenewDhcp_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmActionAsync(_localizer.T("ConfirmDhcpTitle"), _localizer.T("ConfirmDhcpMessage")))
        {
            SetToolOutput(_localizer.T("ActionCancelled"));
            return;
        }

        await RunUiActionAsync(async () => await RunSystemNetworkCommandAsync("ipconfig.exe", ["/renew"], _localizer.T("RenewDhcp")));
        RefreshAdapterInfo();
    }

    private void AdapterRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAdapterInfo();
        SetToolOutput(_localizer.T("AdapterRefreshed"));
    }

    private async void QualityRun_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(RunQualityCheckAsync);
    }

    private async void SpeedRun_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(RunSpeedTestAsync);
    }

    private async void WebsiteRun_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunWebsiteTestAsync(_localizer.T("CustomWebsite"), WebsiteUrlTextBox.Text));
    }

    private async void WebsitePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string url)
        {
            WebsiteUrlTextBox.Text = url;
            await RunUiActionAsync(() => RunWebsiteTestAsync(button.Content?.ToString() ?? url, url));
        }
    }

    private async void IpInfoRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(RefreshPublicIpInfoAsync);
    }

    private async void ReportRun_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(RunDiagnosticReportAsync);
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

    private async void ScheduledStartup_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isSyncingScheduledStartup)
        {
            return;
        }

        await RunUiActionAsync(async () =>
        {
            var ok = false;
            var enable = ScheduledStartupCheckBox.IsChecked == true;
            try
            {
                ScheduledStartupCheckBox.IsEnabled = false;
                ok = enable
                    ? await SetScheduledStartupAsync(register: true)
                    : await SetScheduledStartupAsync(register: false);
            }
            finally
            {
                ScheduledStartupCheckBox.IsEnabled = true;
            }

            await RefreshScheduledStartupSettingAsync();
            if (!ok)
            {
                Log(enable ? _localizer.T("ScheduledStartupEnableFailed") : _localizer.T("ScheduledStartupDisableFailed"));
            }
        });
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
            "QualityPanel" => QualityPanel,
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
        if (!await _checkLock.WaitAsync(0))
        {
            return;
        }

        _isChecking = true;
        CheckNowButton.IsEnabled = false;
        UpdateMonitorStatus();
        try
        {
            var wifiDiagnostics = await _wifi.GetAdapterDiagnosticsAsync();
            if (ShouldTryEnableWifi(wifiDiagnostics))
            {
                Log(string.Format(_localizer.T("TryingEnableWifiAdapter"), wifiDiagnostics.AdapterName ?? "Wi-Fi"));
                var enabled = await _wifi.TryEnableWifiAdapterAsync(wifiDiagnostics.AdapterName);
                Log(enabled ? _localizer.T("WifiAdapterEnabled") : _localizer.T("WifiAdapterEnableFailed"));
                await Task.Delay(TimeSpan.FromSeconds(2));
                wifiDiagnostics = await _wifi.GetAdapterDiagnosticsAsync();
            }

            var currentSsid = await _wifi.GetCurrentSsidAsync();
            var tester = new ConnectivityTester(_settings.Policy);
            var report = await tester.RunFullCheckAsync(currentSsid, wifiDiagnostics);
            ApplyReport(report);
            _failureStreak = report.Ok ? 0 : _failureStreak + 1;
            Log($"{report.CheckedAt:yyyy-MM-dd HH:mm:ss}  {report.State}: {report.Summary}");
            SetToolOutput($"{StateText(report.State)}{Environment.NewLine}{LocalizeSummary(report.Summary)}");
            await TryRecoverIfNeededAsync(report);
        }
        catch (Exception ex)
        {
            _failureStreak++;
            Log(ex.Message);
        }
        finally
        {
            try
            {
                if (_isMonitoring)
                {
                    _nextCheckAt = DateTimeOffset.Now.AddSeconds(_settings.Policy.CheckIntervalSeconds);
                }

                CheckNowButton.IsEnabled = true;
                _isChecking = false;
                UpdateMonitorStatus();
            }
            finally
            {
                _checkLock.Release();
            }
        }
    }

    private async Task TryRecoverIfNeededAsync(ConnectivityReport report)
    {
        if (!_isMonitoring || report.Ok)
        {
            return;
        }

        var policy = _settings.Policy;
        if (_failureStreak < policy.ConsecutiveFailuresBeforeRecovery)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var cooldown = TimeSpan.FromSeconds(policy.RecoveryCooldownSeconds);
        if (_lastRecoveryAttemptAt is { } lastAttempt && now - lastAttempt < cooldown)
        {
            Log(string.Format(_localizer.T("RecoveryCooldownActive"), FormatDuration(cooldown - (now - lastAttempt))));
            return;
        }

        _lastRecoveryAttemptAt = now;
        await AttemptRecoveryAsync(report.CurrentSsid);
    }

    private async Task AttemptRecoveryAsync(string? currentSsid)
    {
        var policy = _settings.Policy;
        var attempted = false;
        Log(string.Format(_localizer.T("RecoveryStarted"), _failureStreak));

        if (policy.EnableDnsFlush)
        {
            attempted = true;
            Log(_localizer.T("RecoveryDnsFlush"));
            await RunSystemNetworkCommandAsync("ipconfig.exe", ["/flushdns"], _localizer.T("FlushDns"));
        }

        if (policy.EnableCurrentWifiReconnect && !string.IsNullOrWhiteSpace(currentSsid))
        {
            attempted = true;
            Log(string.Format(_localizer.T("RecoveryReconnectCurrent"), currentSsid));
            await _wifi.DisconnectAsync();
            await Task.Delay(TimeSpan.FromSeconds(1));
            var commandSent = await _wifi.ConnectAsync(currentSsid);
            var reconnected = commandSent && await WaitForSsidAsync(currentSsid, WifiRestoreTimeout);
            MarkProfileRecoveryResult(currentSsid, reconnected);
            if (reconnected)
            {
                Log(string.Format(_localizer.T("RecoveryWifiConnected"), currentSsid));
                SaveSettings();
                return;
            }
        }

        if (policy.EnableAutoSwitch)
        {
            foreach (var profile in _profiles
                         .Where(profile => profile.Enabled && profile.AutoConnect && profile.CredentialSaved)
                         .OrderBy(profile => profile.Priority))
            {
                if (SsidEquals(profile.Ssid, currentSsid))
                {
                    continue;
                }

                attempted = true;
                Log(string.Format(_localizer.T("RecoverySwitchWifi"), profile.Ssid));
                var commandSent = await _wifi.ConnectAsync(profile.Ssid);
                var connected = commandSent && await WaitForSsidAsync(profile.Ssid, WifiValidationTimeout);
                MarkProfileRecoveryResult(profile.Ssid, connected);
                if (connected)
                {
                    Log(string.Format(_localizer.T("RecoveryWifiConnected"), profile.Ssid));
                    SaveSettings();
                    return;
                }
            }
        }

        if (!attempted)
        {
            Log(_localizer.T("RecoveryNoActionsEnabled"));
            return;
        }

        SaveSettings();
        Log(_localizer.T("RecoveryFailed"));
    }

    private void MarkProfileRecoveryResult(string ssid, bool succeeded)
    {
        var profile = _profiles.FirstOrDefault(item => item.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return;
        }

        if (succeeded)
        {
            profile.LastSuccessAt = DateTimeOffset.Now;
            return;
        }

        profile.LastFailureAt = DateTimeOffset.Now;
        profile.FailureCount++;
        ProfileGrid.Items.Refresh();
    }

    private bool ShouldTryEnableWifi(WifiAdapterDiagnostics diagnostics)
    {
        if (!_settings.Policy.EnableWifiAdapterAutoEnable || !diagnostics.AdapterPresent)
        {
            return false;
        }

        if (diagnostics.HardwareRadioOn == false)
        {
            return false;
        }

        return !diagnostics.AdapterUp || diagnostics.SoftwareRadioOn == false;
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
                _nearbyNetworks.Add(CreateNearbyWifiRow(network));
            }

            UpdateEmptyStates();
            RefreshNearbyPriorityMarkers();
            UpdateWifiSelectionUi();
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

    private async Task AddWifiProfileAsync(NearbyWifiRow? network, string? initialPassword = null)
    {
        var dialog = new AddWifiDialog(_localizer, network is null ? null : ToWifiNetwork(network), initialPassword) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Ssid))
        {
            return;
        }

        var ssid = dialog.Ssid.Trim();
        var existing = _profiles.FirstOrDefault(profile => profile.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            await ConfigureProfileCredentialAsync(existing, initialPassword);
            return;
        }

        if (!dialog.SaveToWindows)
        {
            SetWifiSelectionMessage(_localizer.T("WifiCredentialMustSave"));
            Log(_localizer.T("WifiCredentialMustSave"));
            return;
        }

        if (!await ValidateAndSaveWifiProfileAsync(ssid, dialog.Password, dialog.Authentication))
        {
            return;
        }

        _profiles.Add(new WifiProfile
        {
            Ssid = ssid,
            Priority = _profiles.Count + 1,
            CredentialSaved = true,
            Security = dialog.Authentication,
            Enabled = true,
            AutoConnect = true
        });
        RenumberProfiles();
        UpdateEmptyStates();
        SaveSettings();
        RefreshNearbyPriorityMarkers();
        ProfileGrid.SelectedItem = _profiles.FirstOrDefault(profile => profile.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase));
        UpdateWifiSelectionUi();
    }

    private async Task ConfigureProfileCredentialAsync(WifiProfile profile, string? initialPassword = null)
    {
        var network = new WifiNetwork(
            profile.Ssid,
            0,
            string.IsNullOrWhiteSpace(profile.Security) ? "WPA2-Personal" : profile.Security,
            "",
            true,
            true);
        var dialog = new AddWifiDialog(_localizer, network, initialPassword) { Owner = this };
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

        if (!dialog.SaveToWindows)
        {
            SetWifiSelectionMessage(_localizer.T("WifiCredentialMustSave"));
            Log(_localizer.T("WifiCredentialMustSave"));
            return;
        }

        if (!await ValidateAndSaveWifiProfileAsync(ssid, dialog.Password, dialog.Authentication))
        {
            return;
        }

        profile.Ssid = ssid;
        profile.Security = dialog.Authentication;
        profile.CredentialSaved = true;
        ProfileGrid.Items.Refresh();
        SaveSettings();
        RefreshNearbyPriorityMarkers();
        UpdateWifiSelectionUi();
    }

    private async Task<bool> ValidateAndSaveWifiProfileAsync(string ssid, string? password, string authentication)
    {
        var tempProfileName = $"NetGuardian-Verify-{Guid.NewGuid():N}";
        var previousSsid = await _wifi.GetCurrentSsidAsync();
        var connected = false;
        var touchedConnection = false;

        if (!await ConfirmWifiValidationAsync(previousSsid, ssid))
        {
            SetWifiSelectionMessage(_localizer.T("ActionCancelled"));
            Log(_localizer.T("ActionCancelled"));
            return false;
        }

        SetWifiSelectionMessage(string.Format(_localizer.T("WifiCredentialValidating"), ssid));
        Log(string.Format(_localizer.T("WifiCredentialValidating"), ssid));

        try
        {
            var tempSaved = await _wifi.SaveProfileAsync(tempProfileName, ssid, password, authentication);
            if (!tempSaved)
            {
                SetWifiSelectionMessage(string.Format(_localizer.T("WifiCredentialTempSaveFailed"), ssid));
                Log(string.Format(_localizer.T("WifiCredentialTempSaveFailed"), ssid));
                return false;
            }

            if (SsidEquals(previousSsid, ssid))
            {
                touchedConnection = true;
                await _wifi.DisconnectAsync();
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            touchedConnection = true;
            var commandSent = await _wifi.ConnectProfileAsync(tempProfileName, ssid);
            if (!commandSent)
            {
                SetWifiSelectionMessage(string.Format(_localizer.T("WifiCredentialValidationFailed"), ssid));
                Log(string.Format(_localizer.T("WifiCredentialValidationFailed"), ssid));
                return false;
            }

            connected = await WaitForSsidAsync(ssid, WifiValidationTimeout);
            if (!connected)
            {
                SetWifiSelectionMessage(string.Format(_localizer.T("WifiCredentialValidationFailed"), ssid));
                Log(string.Format(_localizer.T("WifiCredentialValidationFailed"), ssid));
                return false;
            }

            var saved = await _wifi.SaveProfileAsync(ssid, password, authentication);
            if (!saved)
            {
                SetWifiSelectionMessage(string.Format(_localizer.T("WifiCredentialSaveFailed"), ssid));
                Log(string.Format(_localizer.T("WifiCredentialSaveFailed"), ssid));
                return false;
            }

            SetWifiSelectionMessage(string.Format(_localizer.T("WifiCredentialValidated"), ssid));
            Log(string.Format(_localizer.T("WifiCredentialValidated"), ssid));
            return true;
        }
        finally
        {
            await _wifi.DeleteProfileAsync(tempProfileName);
            if (touchedConnection || connected)
            {
                await RestoreWifiAfterValidationAsync(previousSsid, ssid, connected);
            }
        }
    }

    private async Task<bool> ConfirmWifiValidationAsync(string? currentSsid, string targetSsid)
    {
        var current = string.IsNullOrWhiteSpace(currentSsid) ? "-" : currentSsid;
        var message = string.Format(_localizer.T("WifiCredentialRemoteConfirmMessage"), targetSsid, current);
        return await ConfirmActionAsync(_localizer.T("WifiCredentialRemoteConfirmTitle"), message);
    }

    private async Task CleanupTemporaryWifiProfilesAsync()
    {
        var profileNames = await _wifi.GetProfileNamesAsync();
        var temporaryProfiles = profileNames
            .Where(name => name.StartsWith("NetGuardian-Verify-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (temporaryProfiles.Count == 0)
        {
            return;
        }

        var removed = 0;
        foreach (var profileName in temporaryProfiles)
        {
            if (await _wifi.DeleteProfileAsync(profileName))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            Log(string.Format(_localizer.T("TemporaryWifiProfilesCleaned"), removed));
        }
    }

    private async Task RestoreWifiAfterValidationAsync(string? previousSsid, string validatedSsid, bool validationConnected)
    {
        if (string.IsNullOrWhiteSpace(previousSsid))
        {
            if (validationConnected)
            {
                await _wifi.ConnectAsync(validatedSsid);
            }

            return;
        }

        if (SsidEquals(previousSsid, validatedSsid) && validationConnected)
        {
            return;
        }

        Log(string.Format(_localizer.T("WifiRestoringPrevious"), previousSsid));
        var commandSent = await _wifi.ConnectAsync(previousSsid);
        var restored = commandSent && await WaitForSsidAsync(previousSsid, WifiRestoreTimeout);
        if (!restored)
        {
            Log(string.Format(_localizer.T("WifiRestorePreviousFailed"), previousSsid));
        }
    }

    private async Task<bool> WaitForSsidAsync(string ssid, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.Now.Add(timeout);
        while (DateTimeOffset.Now < deadline)
        {
            var currentSsid = await _wifi.GetCurrentSsidAsync();
            if (SsidEquals(currentSsid, ssid))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return false;
    }

    private static bool SsidEquals(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task ImportSelectedWifiPasswordAsync()
    {
        if (ProfileGrid.SelectedItem is WifiProfile profile)
        {
            var password = await _wifi.GetSavedPasswordAsync(profile.Ssid);
            if (string.IsNullOrWhiteSpace(password))
            {
                Log(string.Format(_localizer.T("WifiPasswordImportFailed"), profile.Ssid));
                return;
            }

            Log(string.Format(_localizer.T("WifiPasswordImported"), profile.Ssid));
            await ConfigureProfileCredentialAsync(profile, password);
            return;
        }

        if (NearbyGrid.SelectedItem is NearbyWifiRow network)
        {
            var password = await _wifi.GetSavedPasswordAsync(network.Ssid);
            if (string.IsNullOrWhiteSpace(password))
            {
                Log(string.Format(_localizer.T("WifiPasswordImportFailed"), network.Ssid));
                return;
            }

            Log(string.Format(_localizer.T("WifiPasswordImported"), network.Ssid));
            await AddWifiProfileAsync(network, password);
            return;
        }

        Log(_localizer.T("SelectWifiForPasswordImport"));
    }

    private void MoveSelectedProfile(int direction)
    {
        if (ProfileGrid.SelectedItem is not WifiProfile profile)
        {
            SetWifiSelectionMessage(_localizer.T("SelectProfileFirst"));
            return;
        }

        var oldIndex = _profiles.IndexOf(profile);
        var newIndex = oldIndex + direction;
        if (newIndex < 0 || newIndex >= _profiles.Count)
        {
            SetWifiSelectionMessage(direction < 0 ? _localizer.T("AlreadyFirstProfile") : _localizer.T("AlreadyLastProfile"));
            return;
        }

        _profiles.Move(oldIndex, newIndex);
        RenumberProfiles();
        ProfileGrid.SelectedItem = profile;
        SaveSettings();
        RefreshNearbyPriorityMarkers();
        UpdateWifiSelectionUi();
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

    private NearbyWifiRow CreateNearbyWifiRow(WifiNetwork network)
    {
        var row = new NearbyWifiRow(
            network.Ssid,
            network.SignalPercent,
            network.Authentication,
            network.Encryption,
            network.IsSecure,
            network.IsConnectable);
        ApplyNearbyPriorityMarker(row);
        return row;
    }

    private WifiNetwork ToWifiNetwork(NearbyWifiRow row)
    {
        return new WifiNetwork(
            row.Ssid,
            row.SignalPercent,
            row.Authentication,
            row.Encryption,
            row.IsSecure,
            row.IsConnectable);
    }

    private void RefreshNearbyPriorityMarkers()
    {
        foreach (var row in _nearbyNetworks)
        {
            ApplyNearbyPriorityMarker(row);
        }

        NearbyGrid.Items.Refresh();
    }

    private void ApplyNearbyPriorityMarker(NearbyWifiRow row)
    {
        var profile = _profiles.FirstOrDefault(item => item.Ssid.Equals(row.Ssid, StringComparison.OrdinalIgnoreCase));
        row.IsInPriorityList = profile is not null;
        row.Priority = profile?.Priority;
        row.PriorityBadge = profile is null
            ? _localizer.T("NotInPriority")
            : string.Format(_localizer.T("InPriorityFormat"), profile.Priority);
    }

    private void UpdateWifiSelectionUi()
    {
        var nearby = NearbyGrid.SelectedItem as NearbyWifiRow;
        var profile = ProfileGrid.SelectedItem as WifiProfile;
        var hasNearby = nearby is not null;
        var hasProfile = profile is not null;

        if (hasNearby)
        {
            CurrentWifiSelectionText.Text = string.Format(
                _localizer.T("SelectedNearbyWifi"),
                DisplaySsid(nearby!.Ssid),
                nearby.SignalPercent,
                string.IsNullOrWhiteSpace(nearby.Authentication) ? "-" : nearby.Authentication);
            SetWifiSelectionVisual(true);
        }
        else if (hasProfile)
        {
            CurrentWifiSelectionText.Text = string.Format(
                _localizer.T("SelectedProfileWifi"),
                profile!.Priority,
                DisplaySsid(profile.Ssid),
                profile.Enabled ? _localizer.T("Enabled") : _localizer.T("Disabled"));
            SetWifiSelectionVisual(true);
        }
        else
        {
            CurrentWifiSelectionText.Text = _localizer.T("NoWifiSelected");
            SetWifiSelectionVisual(false);
        }

        AddSelectedButton.IsEnabled = hasNearby;
        SetPasswordButton.IsEnabled = hasNearby || hasProfile;
        ImportPasswordButton.IsEnabled = hasNearby || hasProfile;
        RemoveButton.IsEnabled = hasProfile;
        ConnectButton.IsEnabled = hasProfile;

        var profileIndex = hasProfile ? _profiles.IndexOf(profile!) : -1;
        MoveUpButton.IsEnabled = profileIndex > 0;
        MoveDownButton.IsEnabled = profileIndex >= 0 && profileIndex < _profiles.Count - 1;
    }

    private void SetWifiSelectionMessage(string message)
    {
        CurrentWifiSelectionText.Text = message;
        SetWifiSelectionVisual(false);
    }

    private void SetWifiSelectionVisual(bool hasSelection)
    {
        WifiSelectionBar.Background = new SolidColorBrush(hasSelection ? Color.FromRgb(30, 40, 58) : Color.FromRgb(23, 29, 37));
        WifiSelectionBar.BorderBrush = new SolidColorBrush(hasSelection ? Color.FromRgb(110, 168, 255) : Color.FromRgb(48, 57, 70));
    }

    private string DisplaySsid(string ssid)
    {
        return string.IsNullOrWhiteSpace(ssid) ? _localizer.T("HiddenWifi") : ssid;
    }

    private void ApplyReport(ConnectivityReport report)
    {
        HealthStateText.Text = StateText(report.State);
        HealthStateText.Foreground = report.State switch
        {
            NetworkState.Healthy => new SolidColorBrush(Color.FromRgb(110, 168, 255)),
            NetworkState.Recovering => new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            NetworkState.GatewayIssue or NetworkState.DnsIssue or NetworkState.InternetIssue => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
            _ => new SolidColorBrush(Color.FromRgb(244, 114, 137))
        };
        SummaryText.Text = LocalizeSummary(report.Summary);
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
            NetworkState.WifiDisabled => _localizer.T("WifiDisabled"),
            NetworkState.WifiDisconnected => _localizer.T("WifiDisconnected"),
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

        if (name.Equals("Wi-Fi adapter", StringComparison.OrdinalIgnoreCase))
        {
            return _localizer.T("WifiAdapter");
        }

        if (name.Equals("Wi-Fi status", StringComparison.OrdinalIgnoreCase))
        {
            return _localizer.T("WifiStatus");
        }

        if (name.Equals("Wi-Fi connection", StringComparison.OrdinalIgnoreCase))
        {
            return _localizer.T("WifiConnection");
        }

        if (name.Equals("Wi-Fi hardware radio", StringComparison.OrdinalIgnoreCase))
        {
            return _localizer.T("WifiHardwareRadio");
        }

        if (name.Equals("Wi-Fi software radio", StringComparison.OrdinalIgnoreCase))
        {
            return _localizer.T("WifiSoftwareRadio");
        }

        return name;
    }

    private string LocalizeProbeDetail(string detail)
    {
        return detail.Equals("Resolved", StringComparison.OrdinalIgnoreCase)
            ? _localizer.T("Resolved")
            : detail;
    }

    private string LocalizeSummary(string summary)
    {
        return summary switch
        {
            "No active IPv4 address." => _localizer.T("NoActiveIpv4"),
            "No Wi-Fi adapter detected." => _localizer.T("NoWifiAdapterDetected"),
            "Wi-Fi adapter is disabled." => _localizer.T("WifiAdapterDisabledSummary"),
            "Wi-Fi hardware radio is off." => _localizer.T("WifiHardwareRadioOff"),
            "Wi-Fi software radio is off." => _localizer.T("WifiSoftwareRadioOff"),
            "Wi-Fi is not connected." => _localizer.T("WifiNotConnected"),
            "Gateway is not reachable." => _localizer.T("GatewayNotReachable"),
            "DNS resolution failed." => _localizer.T("DnsResolutionFailed"),
            "Network is healthy." => _localizer.T("NetworkHealthySummary"),
            "Public HTTPS probes did not reach quorum." => _localizer.T("HttpsQuorumFailed"),
            _ => summary
        };
    }

    private void ApplySettingsToUi()
    {
        SetTimeFields(_settings.Policy.CheckIntervalSeconds, IntervalTextBox, IntervalUnitComboBox);
        SetTimeFields(_settings.Policy.RecoveryCooldownSeconds, CooldownTextBox, CooldownUnitComboBox);
        FailuresTextBox.Text = _settings.Policy.ConsecutiveFailuresBeforeRecovery.ToString();
        AutoSwitchCheckBox.IsChecked = _settings.Policy.EnableAutoSwitch;
        SoftReconnectCheckBox.IsChecked = _settings.Policy.EnableCurrentWifiReconnect;
        WifiAutoEnableCheckBox.IsChecked = _settings.Policy.EnableWifiAdapterAutoEnable;
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
        _settings.Policy.EnableWifiAdapterAutoEnable = WifiAutoEnableCheckBox.IsChecked == true;
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
        QualityNavButton.Content = _localizer.T("QualityTools");
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
        ImportPasswordButton.Content = _localizer.T("ImportPassword");
        ConnectButton.Content = _localizer.T("Connect");
        DragHintText.Text = _localizer.T("DoubleClickWifiHint");
        NearbyNetworksTitle.Text = _localizer.T("NearbyNetworks");
        PriorityListTitle.Text = _localizer.T("PriorityList");
        NearbyEmptyText.Text = _localizer.T("NoNearbyWifiRows");
        ProfileEmptyText.Text = _localizer.T("NoPriorityWifiRows");
        RecentEventsEmptyText.Text = _localizer.T("NoRecentEvents");
        NearbySsidColumn.Header = _localizer.T("Ssid");
        NearbySignalColumn.Header = _localizer.T("Signal");
        NearbySecurityColumn.Header = _localizer.T("Security");
        ProfileDragColumn.Header = "";
        ProfileSsidColumn.Header = _localizer.T("Ssid");
        ProfilePriorityColumn.Header = _localizer.T("Priority");
        ProfileCredentialColumn.Header = _localizer.T("CredentialSaved");
        ProfileEnabledColumn.Header = _localizer.T("Enabled");
        ProfileFailuresColumn.Header = _localizer.T("Failures");
        NearbyPriorityColumn.Header = _localizer.T("PriorityState");
        PolicyTitle.Text = _localizer.T("Policy");
        IntervalLabel.Text = _localizer.T("IntervalValue");
        FailuresBeforeRecoveryLabel.Text = _localizer.T("FailuresBeforeRecovery");
        CooldownLabel.Text = _localizer.T("CooldownValue");
        AutoSwitchCheckBox.Content = _localizer.T("AutoSwitch");
        SoftReconnectCheckBox.Content = _localizer.T("SoftReconnect");
        WifiAutoEnableCheckBox.Content = _localizer.T("WifiAutoEnable");
        DnsFlushCheckBox.Content = _localizer.T("DnsFlush");
        SavePolicyButton.Content = _localizer.T("Save");
        ToolsTitle.Text = _localizer.T("SystemNetworkTools");
        ToolsCheckButton.Content = _localizer.T("CheckNow");
        ToolsScanButton.Content = _localizer.T("Scan");
        ToolsNetworkSettingsButton.Content = _localizer.T("OpenNetworkSettings");
        HighRiskText.Text = _localizer.T("HighRiskReserved");
        QuickToolsTab.Header = _localizer.T("QuickTools");
        SpeedToolsTab.Header = _localizer.T("SpeedTools");
        WebsiteToolsTab.Header = _localizer.T("WebsiteTools");
        IpInfoToolsTab.Header = _localizer.T("IpInfoTools");
        ReportToolsTab.Header = _localizer.T("ReportTools");
        DnsToolsTitle.Text = _localizer.T("DnsTools");
        DnsRefreshButton.Content = _localizer.T("RefreshStatus");
        DnsFlushButton.Content = _localizer.T("FlushDns");
        DnsApplyButton.Content = _localizer.T("ApplyDns");
        DnsToolsHint.Text = _localizer.T("DnsToolsHint");
        DnsAdapterColumn.Header = _localizer.T("Adapter");
        DnsServersColumn.Header = _localizer.T("DnsServers");
        DnsSourceColumn.Header = _localizer.T("Source");
        DnsStatusColumn.Header = _localizer.T("StatusColumn");
        ((ComboBoxItem)DnsPresetComboBox.Items[0]).Content = _localizer.T("AutomaticDns");
        ((ComboBoxItem)DnsPresetComboBox.Items[1]).Content = _localizer.T("AliDns");
        ((ComboBoxItem)DnsPresetComboBox.Items[2]).Content = _localizer.T("TencentDns");
        ((ComboBoxItem)DnsPresetComboBox.Items[3]).Content = _localizer.T("CloudflareDns");
        ((ComboBoxItem)DnsPresetComboBox.Items[4]).Content = _localizer.T("GoogleDns");
        ((ComboBoxItem)DnsPresetComboBox.Items[5]).Content = _localizer.T("CustomDns");
        QualityToolsTitle.Text = _localizer.T("QualityTools");
        QualityRunButton.Content = _localizer.T("RunQualityCheck");
        QualityOpenSettingsButton.Content = _localizer.T("OpenNetworkSettings");
        QualityScoreLabel.Text = _localizer.T("QualityScore");
        QualityLatencyLabel.Text = _localizer.T("AverageLatency");
        QualityStateLabel.Text = _localizer.T("QualityStatus");
        QualityProbeColumn.Header = _localizer.T("Probe");
        QualityResultColumn.Header = _localizer.T("Result");
        QualityLatencyColumn.Header = _localizer.T("Latency");
        QualityDetailColumn.Header = _localizer.T("Detail");
        SpeedRunButton.Content = _localizer.T("RunSpeedTest");
        DownloadSpeedLabel.Text = _localizer.T("DownloadSpeed");
        UploadSpeedLabel.Text = _localizer.T("UploadSpeed");
        SpeedLatencyLabel.Text = _localizer.T("Latency");
        WebsiteRunButton.Content = _localizer.T("Test");
        WebsiteAppleButton.Content = "Apple";
        WebsiteGitHubButton.Content = "GitHub";
        WebsiteGoogleButton.Content = "Google";
        WebsiteYouTubeButton.Content = "YouTube";
        WebsiteNameColumn.Header = _localizer.T("Website");
        WebsiteStatusColumn.Header = _localizer.T("StatusColumn");
        WebsiteHttpColumn.Header = "HTTP";
        WebsiteDnsColumn.Header = "DNS";
        WebsiteLatencyColumn.Header = _localizer.T("Latency");
        WebsiteAddressColumn.Header = "IP";
        WebsiteDetailColumn.Header = _localizer.T("Detail");
        IpInfoRefreshButton.Content = _localizer.T("RefreshStatus");
        IpInfoNameColumn.Header = _localizer.T("Item");
        IpInfoValueColumn.Header = _localizer.T("Value");
        ReportRunButton.Content = _localizer.T("RunDiagnosticReport");
        ReportItemColumn.Header = _localizer.T("Item");
        ReportResultColumn.Header = _localizer.T("Result");
        ReportAdviceColumn.Header = _localizer.T("Advice");
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
        ScheduledStartupCheckBox.Content = _localizer.T("ScheduledStartup");
        ScheduledStartupStatusText.Text = ScheduledStartupCheckBox.IsChecked == true
            ? _localizer.T("ScheduledStartupEnabledStatus")
            : _localizer.T("ScheduledStartupDisabledStatus");
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
        SidebarSpeedTitle.Text = _localizer.T("RealtimeSpeed");
        if (HealthStateText.Text == "" || HealthStateText.Text == "尚未检测" || HealthStateText.Text == "Not checked")
        {
            HealthStateText.Text = _localizer.T("NotChecked");
        }

        ApplyTooltips();
        RefreshNearbyPriorityMarkers();
        UpdateWifiSelectionUi();
        UpdateEmptyStates();
        UpdateMonitorStatus();
        UpdateTrayText();
    }

    private void UpdateEmptyStates()
    {
        NearbyEmptyText.Visibility = _nearbyNetworks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProfileEmptyText.Visibility = _profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentEventsEmptyText.Visibility = _recentEvents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyTooltips()
    {
        SetTooltip(CheckNowButton, "CheckNowTip");
        SetTooltip(StartMonitorButton, "StartMonitorTip");
        SetTooltip(StopButton, "StopMonitorTip");
        SetTooltip(ScanButton, "ScanTip");
        SetTooltip(AddSelectedButton, "AddSelectedTip");
        SetTooltip(SetPasswordButton, "SetPasswordTip");
        SetTooltip(ImportPasswordButton, "ImportPasswordTip");
        SetTooltip(AddManualButton, "AddManualTip");
        SetTooltip(MoveUpButton, "MoveUpTip");
        SetTooltip(MoveDownButton, "MoveDownTip");
        SetTooltip(RemoveButton, "RemoveWifiTip");
        SetTooltip(ConnectButton, "ConnectWifiTip");
        SetTooltip(SavePolicyButton, "SavePolicyTip");
        SetTooltip(ToolsCheckButton, "CheckNowTip");
        SetTooltip(ToolsScanButton, "ScanTip");
        SetTooltip(ToolsNetworkSettingsButton, "OpenNetworkSettingsTip");
        SetTooltip(DnsRefreshButton, "DnsRefreshTip");
        SetTooltip(DnsFlushButton, "DnsFlushTip");
        SetTooltip(DnsApplyButton, "DnsApplyTip");
        SetTooltip(QualityRunButton, "QualityRunTip");
        SetTooltip(QualityOpenSettingsButton, "OpenNetworkSettingsTip");
        SetTooltip(SpeedRunButton, "SpeedRunTip");
        SetTooltip(WebsiteRunButton, "WebsiteRunTip");
        SetTooltip(IpInfoRefreshButton, "IpInfoRefreshTip");
        SetTooltip(ReportRunButton, "ReportRunTip");
        SetTooltip(AdapterRefreshButton, "AdapterRefreshTip");
        SetTooltip(AdapterRenewDhcpButton, "RenewDhcpTip");
        SetTooltip(AdapterSettingsButton, "OpenNetworkSettingsTip");
        SetTooltip(AutoSwitchCheckBox, "AutoSwitchTip");
        SetTooltip(SoftReconnectCheckBox, "SoftReconnectTip");
        SetTooltip(WifiAutoEnableCheckBox, "WifiAutoEnableTip");
        SetTooltip(DnsFlushCheckBox, "DnsFlushPolicyTip");
        SetTooltip(CloseBehaviorComboBox, "CloseBehaviorTip");
        SetTooltip(StartMonitoringOnLaunchCheckBox, "StartMonitoringOnLaunchTip");
        SetTooltip(StartMinimizedToTrayCheckBox, "StartMinimizedToTrayTip");
        SetTooltip(ScheduledStartupCheckBox, "ScheduledStartupTip");
    }

    private void SetTooltip(FrameworkElement element, string key)
    {
        element.ToolTip = _localizer.T(key);
        ToolTipService.SetShowOnDisabled(element, true);
    }

    private void ShowPanel(UIElement panel)
    {
        foreach (var item in new UIElement[] { StatusPanel, WifiPanel, PolicyPanel, ToolsPanel, DnsPanel, QualityPanel, NetworkPanel, LogPanel, SettingsPanel })
        {
            item.Visibility = item == panel ? Visibility.Visible : Visibility.Collapsed;
        }

        PageTitleText.Text = panel == StatusPanel ? _localizer.T("Status")
            : panel == WifiPanel ? _localizer.T("Wifi")
            : panel == PolicyPanel ? _localizer.T("Policy")
            : panel == ToolsPanel ? _localizer.T("Tools")
            : panel == DnsPanel ? _localizer.T("DnsTools")
            : panel == QualityPanel ? _localizer.T("QualityTools")
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
            ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
            : new SolidColorBrush(Color.FromRgb(154, 167, 184));
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

    private void UpdateSidebarSpeed()
    {
        var now = DateTimeOffset.Now;
        var (receivedBytes, sentBytes) = ReadNetworkByteTotals();
        var downloadBytesPerSecond = 0d;
        var uploadBytesPerSecond = 0d;

        if (_lastSidebarSpeedSampleAt.HasValue
            && _lastSidebarReceivedBytes.HasValue
            && _lastSidebarSentBytes.HasValue)
        {
            var elapsedSeconds = Math.Max(0.1, (now - _lastSidebarSpeedSampleAt.Value).TotalSeconds);
            var receivedDelta = Math.Max(0, receivedBytes - _lastSidebarReceivedBytes.Value);
            var sentDelta = Math.Max(0, sentBytes - _lastSidebarSentBytes.Value);
            downloadBytesPerSecond = receivedDelta / elapsedSeconds;
            uploadBytesPerSecond = sentDelta / elapsedSeconds;
        }

        _lastSidebarSpeedSampleAt = now;
        _lastSidebarReceivedBytes = receivedBytes;
        _lastSidebarSentBytes = sentBytes;

        PushSpeedSample(_downloadSpeedSamples, downloadBytesPerSecond);
        PushSpeedSample(_uploadSpeedSamples, uploadBytesPerSecond);
        SetRateText(SidebarDownloadSpeedText, SidebarDownloadUnitText, downloadBytesPerSecond);
        SetRateText(SidebarUploadSpeedText, SidebarUploadUnitText, uploadBytesPerSecond);
        SetRateText(SidebarCompactDownloadSpeedText, SidebarCompactDownloadUnitText, downloadBytesPerSecond);
        SetRateText(SidebarCompactUploadSpeedText, SidebarCompactUploadUnitText, uploadBytesPerSecond);
        DrawSidebarSpeedLines();
    }

    private static (long ReceivedBytes, long SentBytes) ReadNetworkByteTotals()
    {
        long receivedBytes = 0;
        long sentBytes = 0;
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == OperationalStatus.Up
                && item.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && item.NetworkInterfaceType != NetworkInterfaceType.Tunnel))
        {
            try
            {
                var statistics = networkInterface.GetIPv4Statistics();
                receivedBytes += statistics.BytesReceived;
                sentBytes += statistics.BytesSent;
            }
            catch (NetworkInformationException)
            {
            }
        }

        return (receivedBytes, sentBytes);
    }

    private static void PushSpeedSample(Queue<double> samples, double value)
    {
        const int sampleLimit = 28;
        samples.Enqueue(Math.Max(0, value));
        while (samples.Count > sampleLimit)
        {
            samples.Dequeue();
        }
    }

    private void DrawSidebarSpeedLines()
    {
        var max = _downloadSpeedSamples.Concat(_uploadSpeedSamples).DefaultIfEmpty(1).Max();
        max = Math.Max(1, max);
        SidebarDownloadLine.Points = BuildSparklinePoints(_downloadSpeedSamples, 158, 38, max);
        SidebarUploadLine.Points = BuildSparklinePoints(_uploadSpeedSamples, 158, 38, max);
    }

    private static PointCollection BuildSparklinePoints(IEnumerable<double> samples, double width, double height, double max)
    {
        var values = samples.ToList();
        var points = new PointCollection();
        if (values.Count == 0)
        {
            return points;
        }

        if (values.Count == 1)
        {
            points.Add(new Point(0, height));
            points.Add(new Point(width, height - (values[0] / max * height)));
            return points;
        }

        for (var index = 0; index < values.Count; index++)
        {
            var x = width * index / (values.Count - 1);
            var y = height - (values[index] / max * height);
            points.Add(new Point(x, Math.Clamp(y, 0, height)));
        }

        return points;
    }

    private static void SetRateText(TextBlock valueText, TextBlock unitText, double bytesPerSecond)
    {
        var (value, unit) = FormatRateParts(bytesPerSecond);
        valueText.Text = value;
        unitText.Text = unit;
    }

    private static (string Value, string Unit) FormatRateParts(double bytesPerSecond)
    {
        var units = new[] { "B/s", "KB/s", "MB/s", "GB/s" };
        var value = Math.Max(0, bytesPerSecond);
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return (value >= 10 || unitIndex == 0 ? $"{value:F0}" : $"{value:F1}", units[unitIndex]);
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

    private async Task<bool> ConfirmActionAsync(string title, string message)
    {
        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = _localizer.T("Continue"),
            CloseButtonText = _localizer.T("Cancel"),
            IsPrimaryButtonEnabled = true
        };

        var result = await messageBox.ShowDialogAsync();
        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    private async Task RefreshScheduledStartupSettingAsync()
    {
        var isRegistered = await IsScheduledStartupRegisteredAsync();
        _isSyncingScheduledStartup = true;
        try
        {
            ScheduledStartupCheckBox.IsChecked = isRegistered;
            ScheduledStartupStatusText.Text = isRegistered
                ? _localizer.T("ScheduledStartupEnabledStatus")
                : _localizer.T("ScheduledStartupDisabledStatus");
        }
        finally
        {
            _isSyncingScheduledStartup = false;
        }
    }

    private async Task<bool> IsScheduledStartupRegisteredAsync()
    {
        using var process = CreateHiddenProcess("schtasks.exe", ["/Query", "/TN", ScheduledStartupTaskName]);
        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    private async Task<bool> SetScheduledStartupAsync(bool register)
    {
        var root = FindProjectRoot();
        if (root is null)
        {
            ScheduledStartupStatusText.Text = _localizer.T("ScheduledStartupScriptMissing");
            return false;
        }

        var scriptName = register ? "register-netguardian-task.ps1" : "unregister-netguardian-task.ps1";
        var script = Path.Combine(root, "scripts", scriptName);
        if (!File.Exists(script))
        {
            ScheduledStartupStatusText.Text = _localizer.T("ScheduledStartupScriptMissing");
            return false;
        }

        ScheduledStartupStatusText.Text = register
            ? _localizer.T("ScheduledStartupEnabling")
            : _localizer.T("ScheduledStartupDisabling");

        var result = await RunPowerShellFileAsync(script, ["-TaskName", ScheduledStartupTaskName]);
        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            Log(result.Output);
        }

        ScheduledStartupStatusText.Text = result.Success
            ? register ? _localizer.T("ScheduledStartupEnabledStatus") : _localizer.T("ScheduledStartupDisabledStatus")
            : register ? _localizer.T("ScheduledStartupEnableFailed") : _localizer.T("ScheduledStartupDisableFailed");
        return result.Success;
    }

    private static string? FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var launcher = Path.Combine(directory.FullName, "Start-NetGuardian.vbs");
            var scripts = Path.Combine(directory.FullName, "scripts");
            if (File.Exists(launcher) && Directory.Exists(scripts))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static async Task<(bool Success, string Output)> RunPowerShellFileAsync(string script, string[] arguments)
    {
        var processArguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            script
        };
        processArguments.AddRange(arguments);

        using var process = CreateHiddenProcess("powershell.exe", processArguments);
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = (await outputTask + await errorTask).Trim();
        return (process.ExitCode == 0, output);
    }

    private static Process CreateHiddenProcess(string fileName, IEnumerable<string> arguments)
    {
        var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        return process;
    }

    private async Task RunSystemNetworkCommandAsync(string fileName, string[] arguments, string label)
    {
        Log($"{label}...");
        SetToolOutput($"{label}...");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

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

        if (QualityPanel.Visibility == Visibility.Visible)
        {
            QualityStatusText.Text = text;
        }

    }

    private void InitializeDnsControls()
    {
        _isSyncingDnsControls = true;
        DnsPresetComboBox.SelectedIndex = 0;
        _isSyncingDnsControls = false;
        ApplyDnsPresetToFields();
    }

    private async Task ApplyDnsSettingsAsync()
    {
        if (DnsAdapterComboBox.SelectedItem is not DnsAdapterOption adapter)
        {
            DnsStatusText.Text = _localizer.T("SelectAdapter");
            return;
        }

        var preset = GetSelectedDnsPreset();
        var isAutomatic = preset == "automatic";
        if (!isAutomatic)
        {
            var primary = DnsPrimaryTextBox.Text.Trim();
            var secondary = DnsSecondaryTextBox.Text.Trim();
            if (!IsValidIpv4(primary) || (!string.IsNullOrWhiteSpace(secondary) && !IsValidIpv4(secondary)))
            {
                DnsStatusText.Text = _localizer.T("InvalidDnsAddress");
                return;
            }
        }

        if (!await ConfirmActionAsync(_localizer.T("ConfirmDnsTitle"), _localizer.T("ConfirmDnsMessage")))
        {
            DnsStatusText.Text = _localizer.T("ActionCancelled");
            return;
        }

        DnsApplyButton.IsEnabled = false;
        DnsStatusText.Text = _localizer.T("ApplyingDns");
        try
        {
            var result = isAutomatic
                ? await _networkConfiguration.SetAutomaticDnsAsync(adapter.Name)
                : await _networkConfiguration.SetStaticDnsAsync(
                    adapter.Name,
                    DnsPrimaryTextBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DnsSecondaryTextBox.Text) ? null : DnsSecondaryTextBox.Text.Trim());

            var status = result.Success
                ? _localizer.T("DnsApplied")
                : string.Format(_localizer.T("DnsApplyFailed"), result.ExitCode);
            DnsStatusText.Text = status;
            Log(status);
            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                Log(result.Output);
            }

            RefreshDnsInfo();
        }
        finally
        {
            DnsApplyButton.IsEnabled = true;
        }
    }

    private void ApplyDnsPresetToFields()
    {
        var preset = GetSelectedDnsPreset();
        var servers = GetPresetDnsServers(preset);
        var isCustom = preset == "custom";

        DnsPrimaryTextBox.IsEnabled = isCustom;
        DnsSecondaryTextBox.IsEnabled = isCustom;
        if (servers is null)
        {
            DnsPrimaryTextBox.Text = "";
            DnsSecondaryTextBox.Text = "";
            return;
        }

        DnsPrimaryTextBox.Text = servers.Value.Primary;
        DnsSecondaryTextBox.Text = servers.Value.Secondary;
    }

    private void FillDnsFieldsFromSelectedAdapter()
    {
        if (DnsAdapterComboBox.SelectedItem is not DnsAdapterOption adapter)
        {
            return;
        }

        var row = _dnsRows.FirstOrDefault(item => item.Adapter.Equals(adapter.Name, StringComparison.OrdinalIgnoreCase));
        if (row is null || row.Servers == "-")
        {
            SelectDnsPreset("automatic");
            return;
        }

        var servers = row.Servers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(IsValidIpv4)
            .ToList();
        if (servers.Count == 0)
        {
            SelectDnsPreset("automatic");
            return;
        }

        _isSyncingDnsControls = true;
        DnsPrimaryTextBox.Text = servers[0];
        DnsSecondaryTextBox.Text = servers.Count > 1 ? servers[1] : "";
        SelectDnsPresetByServers(DnsPrimaryTextBox.Text, DnsSecondaryTextBox.Text);
        var isCustom = GetSelectedDnsPreset() == "custom";
        DnsPrimaryTextBox.IsEnabled = isCustom;
        DnsSecondaryTextBox.IsEnabled = isCustom;
        _isSyncingDnsControls = false;
    }

    private void SelectDnsPresetByServers(string primary, string secondary)
    {
        foreach (var key in new[] { "ali", "tencent", "cloudflare", "google" })
        {
            var preset = GetPresetDnsServers(key);
            if (preset.HasValue
                && preset.Value.Primary == primary
                && string.Equals(preset.Value.Secondary, secondary, StringComparison.OrdinalIgnoreCase))
            {
                SelectDnsPreset(key);
                return;
            }
        }

        SelectDnsPreset("custom");
    }

    private void SelectDnsPreset(string key)
    {
        foreach (ComboBoxItem item in DnsPresetComboBox.Items)
        {
            if (item.Tag?.ToString() == key)
            {
                DnsPresetComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private string GetSelectedDnsPreset()
    {
        return DnsPresetComboBox.SelectedItem is ComboBoxItem item
            ? item.Tag?.ToString() ?? "automatic"
            : "automatic";
    }

    private static (string Primary, string Secondary)? GetPresetDnsServers(string preset)
    {
        return preset switch
        {
            "ali" => ("223.5.5.5", "223.6.6.6"),
            "tencent" => ("119.29.29.29", "182.254.116.116"),
            "cloudflare" => ("1.1.1.1", "1.0.0.1"),
            "google" => ("8.8.8.8", "8.8.4.4"),
            "custom" => ("", ""),
            _ => null
        };
    }

    private static bool IsValidIpv4(string value)
    {
        return IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork;
    }

    private void RefreshDnsInfo()
    {
        var selectedAdapter = (DnsAdapterComboBox.SelectedItem as DnsAdapterOption)?.Name;
        _dnsAdapterOptions.Clear();
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

            if (networkInterface.OperationalStatus == OperationalStatus.Up)
            {
                _dnsAdapterOptions.Add(new DnsAdapterOption(networkInterface.Name));
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

        if (_dnsAdapterOptions.Count > 0)
        {
            var match = _dnsAdapterOptions.FirstOrDefault(item => item.Name.Equals(selectedAdapter, StringComparison.OrdinalIgnoreCase));
            DnsAdapterComboBox.SelectedItem = match ?? _dnsAdapterOptions[0];
            FillDnsFieldsFromSelectedAdapter();
        }
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

    private async Task RunQualityCheckAsync()
    {
        QualityRunButton.IsEnabled = false;
        QualityStatusText.Text = _localizer.T("Checking");
        try
        {
            var currentSsid = await _wifi.GetCurrentSsidAsync();
            var wifiDiagnostics = await _wifi.GetAdapterDiagnosticsAsync();
            var tester = new ConnectivityTester(_settings.Policy);
            var report = await tester.RunFullCheckAsync(currentSsid, wifiDiagnostics);
            ApplyQualityReport(report);
            Log($"{_localizer.T("QualityTools")}: {QualityScoreText.Text} - {report.Summary}");
        }
        finally
        {
            QualityRunButton.IsEnabled = true;
        }
    }

    private void ApplyQualityReport(ConnectivityReport report)
    {
        var score = CalculateQualityScore(report);
        var successfulLatencies = report.Probes
            .Where(probe => probe.Ok && probe.LatencyMs.HasValue)
            .Select(probe => probe.LatencyMs!.Value)
            .ToList();

        QualityScoreText.Text = $"{score}/100";
        QualityScoreText.Foreground = score switch
        {
            >= 85 => new SolidColorBrush(Color.FromRgb(110, 168, 255)),
            >= 60 => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
            _ => new SolidColorBrush(Color.FromRgb(244, 114, 137))
        };
        QualityLatencyText.Text = successfulLatencies.Count == 0
            ? "-"
            : $"{successfulLatencies.Average():F0} ms";
        QualityStateText.Text = $"{QualityStateTextForScore(score)} / {StateText(report.State)}";
        QualityStatusText.Text = LocalizeSummary(report.Summary);

        _qualityRows.Clear();
        foreach (var probe in report.Probes)
        {
            _qualityRows.Add(new QualityProbeRow(
                LocalizeProbeName(probe.Name),
                probe.Ok ? _localizer.T("Pass") : _localizer.T("Fail"),
                probe.LatencyMs.HasValue ? $"{probe.LatencyMs.Value:F0} ms" : "-",
                LocalizeProbeDetail(probe.Detail)));
        }
    }

    private int CalculateQualityScore(ConnectivityReport report)
    {
        var score = report.State switch
        {
            NetworkState.Healthy => 100,
            NetworkState.InternetIssue => 65,
            NetworkState.DnsIssue => 45,
            NetworkState.GatewayIssue => 35,
            NetworkState.WifiDisconnected => 15,
            NetworkState.WifiDisabled => 0,
            NetworkState.Disconnected => 0,
            _ => 25
        };

        var failedProbeCount = report.Probes.Count(probe => !probe.Ok);
        score -= failedProbeCount * 6;

        var averageLatency = report.Probes
            .Where(probe => probe.Ok && probe.LatencyMs.HasValue)
            .Select(probe => probe.LatencyMs!.Value)
            .DefaultIfEmpty(0)
            .Average();
        score -= averageLatency switch
        {
            <= 0 => 0,
            <= 80 => 0,
            <= 180 => 5,
            <= 350 => 12,
            _ => 22
        };

        return Math.Clamp(score, 0, 100);
    }

    private string QualityStateTextForScore(int score)
    {
        return score switch
        {
            >= 85 => _localizer.T("QualityExcellent"),
            >= 70 => _localizer.T("QualityGood"),
            >= 50 => _localizer.T("QualityWeak"),
            _ => _localizer.T("QualityPoor")
        };
    }

    private void ResetQualityUi()
    {
        QualityScoreText.Text = "-";
        QualityLatencyText.Text = "-";
        QualityStateText.Text = _localizer.T("NotChecked");
        QualityStatusText.Text = _localizer.T("NotChecked");
    }

    private async Task RunSpeedTestAsync()
    {
        SpeedRunButton.IsEnabled = false;
        SpeedStatusText.Text = _localizer.T("SpeedTesting");
        UpdateSpeedGauge(0, _localizer.T("SpeedTesting"));
        try
        {
            var progress = new Progress<NetworkSpeedTestProgress>(ApplySpeedProgress);
            var result = await _speedTester.RunAsync(progress);
            DownloadSpeedText.Text = FormatMbps(result.DownloadMbps);
            UploadSpeedText.Text = FormatMbps(result.UploadMbps);
            SpeedLatencyText.Text = result.LatencyMs.HasValue ? $"{result.LatencyMs.Value:F0} ms" : "-";
            UpdateSpeedGauge(Math.Max(result.DownloadMbps, result.UploadMbps), _localizer.T("SpeedTestComplete"));
            SpeedStatusText.Text = string.Format(_localizer.T("SpeedTestDone"), result.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            Log($"{_localizer.T("SpeedTools")}: {DownloadSpeedText.Text} / {UploadSpeedText.Text} / {SpeedLatencyText.Text}");
        }
        catch (Exception ex)
        {
            SpeedStatusText.Text = ex.Message;
            Log(ex.Message);
        }
        finally
        {
            SpeedRunButton.IsEnabled = true;
        }
    }

    private void ApplySpeedProgress(NetworkSpeedTestProgress progress)
    {
        if (progress.LatencyMs.HasValue)
        {
            SpeedLatencyText.Text = $"{progress.LatencyMs.Value:F0} ms";
        }

        if (progress.Mbps.HasValue)
        {
            if (progress.Phase.Equals("Upload", StringComparison.OrdinalIgnoreCase))
            {
                UploadSpeedText.Text = FormatMbps(progress.Mbps.Value);
                UpdateSpeedGauge(progress.Mbps.Value, _localizer.T("UploadSpeed"));
                return;
            }

            DownloadSpeedText.Text = FormatMbps(progress.Mbps.Value);
            UpdateSpeedGauge(progress.Mbps.Value, _localizer.T("DownloadSpeed"));
        }
    }

    private async Task RunWebsiteTestAsync(string name, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            WebsiteStatusText.Text = _localizer.T("EnterWebsiteUrl");
            return;
        }

        WebsiteRunButton.IsEnabled = false;
        WebsiteStatusText.Text = _localizer.T("WebsiteTesting");
        try
        {
            var result = await _websiteTester.TestAsync(name, url);
            var row = new WebsiteTestRow(
                string.IsNullOrWhiteSpace(name) ? result.Host : name,
                result.Success ? _localizer.T("Pass") : _localizer.T("Fail"),
                result.StatusCode?.ToString() ?? "-",
                FormatLatency(result.DnsLatencyMs),
                FormatLatency(result.HttpLatencyMs),
                string.IsNullOrWhiteSpace(result.Address) ? "-" : result.Address,
                string.IsNullOrWhiteSpace(result.Detail) ? result.Url : result.Detail);
            _websiteRows.Insert(0, row);
            while (_websiteRows.Count > 20)
            {
                _websiteRows.RemoveAt(_websiteRows.Count - 1);
            }

            WebsiteStatusText.Text = string.Format(_localizer.T("WebsiteTestDone"), row.Name, row.Status, row.HttpLatency);
            Log($"{_localizer.T("WebsiteTools")}: {row.Name} {row.Status} {row.HttpLatency}");
        }
        catch (Exception ex)
        {
            WebsiteStatusText.Text = ex.Message;
            Log(ex.Message);
        }
        finally
        {
            WebsiteRunButton.IsEnabled = true;
        }
    }

    private async Task RefreshPublicIpInfoAsync()
    {
        IpInfoRefreshButton.IsEnabled = false;
        IpInfoStatusText.Text = _localizer.T("Checking");
        try
        {
            var info = await _publicIpInfo.GetAsync();
            _ipInfoRows.Clear();
            AddIpInfoRow("PublicIp", info.Ip);
            AddIpInfoRow("CountryRegion", $"{info.Country} ({info.CountryCode})");
            AddIpInfoRow("City", string.Join(", ", new[] { info.Region, info.City }.Where(item => !string.IsNullOrWhiteSpace(item) && item != "-")));
            AddIpInfoRow("Isp", info.Isp);
            AddIpInfoRow("Organization", info.Organization);
            AddIpInfoRow("AutonomousSystem", info.AutonomousSystem);
            AddIpInfoRow("Timezone", info.Timezone);
            AddIpInfoRow("Coordinates", $"{info.Longitude:F4}, {info.Latitude:F4}");
            AddIpInfoRow("LastRefresh", info.RefreshedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            IpInfoStatusText.Text = string.Format(_localizer.T("IpInfoRefreshed"), info.Ip);
            Log($"{_localizer.T("IpInfoTools")}: {info.Ip}");
        }
        catch (Exception ex)
        {
            IpInfoStatusText.Text = ex.Message;
            Log(ex.Message);
        }
        finally
        {
            IpInfoRefreshButton.IsEnabled = true;
        }
    }

    private async Task RunDiagnosticReportAsync()
    {
        ReportRunButton.IsEnabled = false;
        ReportStatusText.Text = _localizer.T("Checking");
        try
        {
            var currentSsid = await _wifi.GetCurrentSsidAsync();
            var wifiDiagnostics = await _wifi.GetAdapterDiagnosticsAsync();
            var tester = new ConnectivityTester(_settings.Policy);
            var report = await tester.RunFullCheckAsync(currentSsid, wifiDiagnostics);
            var score = CalculateQualityScore(report);

            _reportRows.Clear();
            AddReportRow("HealthState", $"{score}/100 - {StateText(report.State)}", AdviceForState(report.State));
            AddReportRow("CurrentNetwork", string.IsNullOrWhiteSpace(report.CurrentSsid) ? "-" : report.CurrentSsid, _localizer.T("CheckWifiPriority"));
            AddReportRow("IPv4", string.IsNullOrWhiteSpace(report.Ipv4) ? "-" : report.Ipv4, string.IsNullOrWhiteSpace(report.Ipv4) ? _localizer.T("CheckAdapter") : _localizer.T("NoActionNeeded"));
            AddReportRow("Gateway", string.IsNullOrWhiteSpace(report.Gateway) ? "-" : report.Gateway, report.State == NetworkState.GatewayIssue ? _localizer.T("CheckRouter") : _localizer.T("NoActionNeeded"));
            AddReportRow("DnsTools", ProbeSummary(report, "DNS"), report.State == NetworkState.DnsIssue ? _localizer.T("TryDnsSettings") : _localizer.T("NoActionNeeded"));
            AddReportRow("Internet", ProbeSummary(report, "HTTPS"), report.State == NetworkState.InternetIssue ? _localizer.T("CheckProxyOrIsp") : _localizer.T("NoActionNeeded"));
            AddReportRow("FailureStreak", _failureStreak.ToString(), _failureStreak > 0 ? _localizer.T("WatchNextCheck") : _localizer.T("NoActionNeeded"));

            ReportStatusText.Text = $"{StateText(report.State)} - {LocalizeSummary(report.Summary)}";
            Log($"{_localizer.T("ReportTools")}: {ReportStatusText.Text}");
        }
        catch (Exception ex)
        {
            ReportStatusText.Text = ex.Message;
            Log(ex.Message);
        }
        finally
        {
            ReportRunButton.IsEnabled = true;
        }
    }

    private void AddIpInfoRow(string key, string value)
    {
        _ipInfoRows.Add(new IpInfoRow(_localizer.T(key), string.IsNullOrWhiteSpace(value) ? "-" : value));
    }

    private void AddReportRow(string key, string result, string advice)
    {
        _reportRows.Add(new DiagnosticReportRow(_localizer.T(key), result, advice));
    }

    private string AdviceForState(NetworkState state)
    {
        return state switch
        {
            NetworkState.Healthy => _localizer.T("NoActionNeeded"),
            NetworkState.WifiDisabled => _localizer.T("CheckWifiSwitch"),
            NetworkState.WifiDisconnected => _localizer.T("ConnectWifi"),
            NetworkState.Disconnected => _localizer.T("CheckAdapter"),
            NetworkState.GatewayIssue => _localizer.T("CheckRouter"),
            NetworkState.DnsIssue => _localizer.T("TryDnsSettings"),
            NetworkState.InternetIssue => _localizer.T("CheckProxyOrIsp"),
            _ => _localizer.T("RunAgainLater")
        };
    }

    private string ProbeSummary(ConnectivityReport report, string prefix)
    {
        var probes = report.Probes.Where(probe => probe.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (probes.Count == 0)
        {
            return "-";
        }

        return $"{probes.Count(probe => probe.Ok)}/{probes.Count}";
    }

    private void ResetSpeedUi()
    {
        DownloadSpeedText.Text = "-";
        UploadSpeedText.Text = "-";
        SpeedLatencyText.Text = "-";
        SpeedStatusText.Text = _localizer.T("NotChecked");
        UpdateSpeedGauge(0, _localizer.T("NotChecked"));
    }

    private void ResetIpInfoUi()
    {
        IpInfoStatusText.Text = _localizer.T("NotChecked");
    }

    private void ResetReportUi()
    {
        ReportStatusText.Text = _localizer.T("NotChecked");
    }

    private static string FormatMbps(double mbps)
    {
        return $"{mbps:F1} Mbps";
    }

    private static string FormatLatency(double? latencyMs)
    {
        return latencyMs.HasValue ? $"{latencyMs.Value:F0} ms" : "-";
    }

    private void UpdateSpeedGauge(double mbps, string phase)
    {
        var progress = NormalizeGaugeSpeed(mbps);
        SpeedGaugeArc.Data = progress <= 0 ? Geometry.Empty : CreateGaugeArc(progress);
        var needlePoint = GaugePoint(200 + (140 * progress), 124);
        SpeedGaugeNeedle.X2 = needlePoint.X;
        SpeedGaugeNeedle.Y2 = needlePoint.Y;
        SpeedGaugeValueText.Text = mbps > 0 ? $"{mbps:F1}" : "-";
        SpeedGaugePhaseText.Text = phase;
    }

    private static double NormalizeGaugeSpeed(double mbps)
    {
        if (mbps <= 0)
        {
            return 0;
        }

        return Math.Clamp(Math.Log10(mbps + 1) / Math.Log10(1001), 0, 1);
    }

    private static Geometry CreateGaugeArc(double progress)
    {
        var start = GaugePoint(200, 135);
        var end = GaugePoint(200 + (140 * progress), 135);
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(135, 135),
            0,
            false,
            SweepDirection.Clockwise,
            true));
        return new PathGeometry([figure]);
    }

    private static Point GaugePoint(double angleDegrees, double radius)
    {
        const double centerX = 210;
        const double centerY = 210;
        var radians = angleDegrees * Math.PI / 180;
        return new Point(centerX + (Math.Cos(radians) * radius), centerY + (Math.Sin(radians) * radius));
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
        _logLineCount++;
        TrimLogTextBox();
        LogTextBox.ScrollToEnd();
        _recentEvents.Insert(0, line);
        while (_recentEvents.Count > 6)
        {
            _recentEvents.RemoveAt(_recentEvents.Count - 1);
        }

        UpdateEmptyStates();
    }

    private void TrimLogTextBox()
    {
        while (_logLineCount > MaxLogLines)
        {
            var text = LogTextBox.Text;
            var firstLineEnd = text.IndexOf(Environment.NewLine, StringComparison.Ordinal);
            if (firstLineEnd < 0)
            {
                _logLineCount = 1;
                return;
            }

            LogTextBox.Text = text[(firstLineEnd + Environment.NewLine.Length)..];
            LogTextBox.CaretIndex = LogTextBox.Text.Length;
            _logLineCount--;
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
        if (!_isNotifyIconDisposed)
        {
            _notifyIcon.Visible = true;
        }
    }

    private void ExitApplication()
    {
        _forceExit = true;
        CleanupResources();
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
            CleanupResources();
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
            _ = ShowCloseDialogAsync();
            return;
        }

        CleanupResources();
    }

    private async Task ShowCloseDialogAsync()
    {
        try
        {
            var messageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = _localizer.T("ClosePromptTitle"),
                Content = _localizer.T("ClosePrompt"),
                PrimaryButtonText = _localizer.T("CloseTray"),
                SecondaryButtonText = _localizer.T("CloseExit"),
                CloseButtonText = _localizer.T("Cancel"),
                IsPrimaryButtonEnabled = true,
                IsSecondaryButtonEnabled = true
            };

            var result = await messageBox.ShowDialogAsync();

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
        catch (Exception ex)
        {
            Log(ex.Message);
        }
        finally
        {
            _isCloseDialogShowing = false;
        }
    }

    private void CleanupResources()
    {
        if (_resourcesCleaned)
        {
            return;
        }

        _resourcesCleaned = true;
        _monitorTimer.Stop();
        _countdownTimer.Stop();
        _sidebarSpeedTimer.Stop();
        DisposeNotifyIcon();
    }

    private void DisposeNotifyIcon()
    {
        if (_isNotifyIconDisposed)
        {
            return;
        }

        _isNotifyIconDisposed = true;
        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Icon?.Dispose();
            _notifyIcon.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private sealed record ProbeRow(string Name, string ResultText, string LatencyText, string Detail);
    private sealed record QualityProbeRow(string Name, string ResultText, string LatencyText, string Detail);
    private sealed record WebsiteTestRow(string Name, string Status, string HttpStatus, string DnsLatency, string HttpLatency, string Address, string Detail);
    private sealed record IpInfoRow(string Name, string Value);
    private sealed record DiagnosticReportRow(string Item, string Result, string Advice);
    private sealed record DnsAdapterOption(string Name);
    private sealed record DnsServerRow(string Adapter, string Servers, string Source, string Status);
    private sealed record AdapterRow(string Name, string Type, string Status, string Ipv4, string Gateway, string DnsServers);

    private sealed class NearbyWifiRow(
        string ssid,
        int signalPercent,
        string authentication,
        string encryption,
        bool isSecure,
        bool isConnectable)
    {
        public string Ssid { get; } = ssid;
        public int SignalPercent { get; } = signalPercent;
        public string Authentication { get; } = authentication;
        public string Encryption { get; } = encryption;
        public bool IsSecure { get; } = isSecure;
        public bool IsConnectable { get; } = isConnectable;
        public bool IsInPriorityList { get; set; }
        public int? Priority { get; set; }
        public string PriorityBadge { get; set; } = "";
    }
}
