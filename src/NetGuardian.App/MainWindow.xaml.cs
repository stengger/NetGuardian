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
using NetGuardian.App.Presentation;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Logging;
using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.Network;
using NetGuardian.Core.Recovery;
using NetGuardian.Core.Runtime;
using NetGuardian.Core.Settings;
using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;

namespace NetGuardian.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private const int MaxLogLines = 500;
    private const string ScheduledStartupTaskName = "NetGuardian";
    private static readonly TimeSpan WifiValidationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan WifiRestoreTimeout = TimeSpan.FromSeconds(10);
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly Localizer _localizer;
    private readonly IWifiClient _wifi;
    private readonly NetworkConfigurationService _networkConfiguration;
    private readonly WebsiteTestService _websiteTester;
    private readonly PublicIpInfoService _publicIpInfo;
    private readonly NetworkSpeedTestService _speedTester;
    private readonly MonitoringEngine _engine;
    private readonly IConnectivityTester _connectivityTester;
    private readonly ISystemCommandRunner _commandRunner;
    private MonitoringLease? _monitorLease;
    private PolicyEditFeedback? _policyFeedback;
    private readonly ILogSink _fileLog;
    private readonly EventJournal _journal;
    private readonly Wpf.Ui.ISnackbarService _snackbar;
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
    private readonly DispatcherTimer _countdownTimer = new();
    private readonly DispatcherTimer _sidebarSpeedTimer = new();
    private readonly Queue<double> _downloadSpeedSamples = new();
    private readonly Queue<double> _uploadSpeedSamples = new();
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Views.TrayContextMenu _trayMenu = new();
    private TrayMenuHost? _trayMenuHost;
    private readonly WindowWorkArea _windowWorkArea;
    private bool _forceExit;
    private bool _isSyncingDnsControls;
    private bool _isApplyingDns;
    private bool _isSyncingWifiSelection;
    private bool _isSyncingScheduledStartup;
    private bool _isSyncingHighRiskToggle;
    private bool _isFirstRun;
    private bool _isImmediateCheckRunning;
    private CancellationTokenSource? _immediateCheckCancellation;
    private readonly ConnectivityReportSelection _reportSelection = new();
    private bool _isNotifyIconDisposed;
    private bool _resourcesCleaned;
    private long? _lastSidebarSpeedTimestamp;
    private readonly NetworkTrafficSampler _trafficSampler = new();
    private Point _dragStartPoint;
    private WifiProfile? _draggedProfile;

    public MainWindow(
        SettingsStore store,
        Localizer localizer,
        IWifiClient wifi,
        IConnectivityTester connectivityTester,
        ISystemCommandRunner commandRunner,
        NetworkConfigurationService networkConfiguration,
        WebsiteTestService websiteTester,
        PublicIpInfoService publicIpInfo,
        NetworkSpeedTestService speedTester,
        ILogSink logSink,
        EventJournal journal,
        Wpf.Ui.ISnackbarService snackbarService)
    {
        _store = store;
        _fileLog = logSink;
        _journal = journal;
        _snackbar = snackbarService;
        _localizer = localizer;
        _eventLogStore = new EventLogStore(localizer, MaxLogLines);
        _wifi = wifi;
        _connectivityTester = connectivityTester;
        _commandRunner = commandRunner;
        _networkConfiguration = networkConfiguration;
        _websiteTester = websiteTester;
        _publicIpInfo = publicIpInfo;
        _speedTester = speedTester;

        InitializeComponent();
        _windowWorkArea = new WindowWorkArea(this);
        WifiListsGrid.SizeChanged += (_, _) => ResponsiveLayout.ApplyWifi(this);
        _isFirstRun = !File.Exists(_store.Path);
        _settings = _store.Load();
        _localizer.SetLanguage(_settings.Language);

        _engine = new MonitoringEngine(
            _wifi,
            connectivityTester,
            commandRunner,
            () => _settings.Policy,
            () => _profiles.ToList(),
            highRiskService: new BudgetedRecoveryService(new HighRiskRecoveryService(commandRunner),
                new RecoveryBudget(Path.GetDirectoryName(_store.Path)!),
                () => TimeSpan.FromMinutes(_settings.Policy.HighRiskCooldownMinutes)),
            networkChanges: new NetworkChangeSource())
        {
            WifiValidationTimeout = WifiValidationTimeout,
            WifiRestoreTimeout = WifiRestoreTimeout
        };
        _engine.EngineEvent += Engine_Event;
        _engine.CheckCompleted += Engine_CheckCompleted;
        _engine.ProfileRecoveryResult += Engine_ProfileRecoveryResult;
        _engine.StateChanged += Engine_StateChanged;

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
        InitializeLogView();

        _snackbar.SetSnackbarPresenter(RootSnackbar);

        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (_, _) => { UpdateMonitorStatus(); RefreshCurrentWifiIfDue(); };
        _countdownTimer.Start();
        _sidebarSpeedTimer.Interval = TimeSpan.FromSeconds(1);
        _sidebarSpeedTimer.Tick += (_, _) => UpdateSidebarSpeed();
        _sidebarSpeedTimer.Start();

        _notifyIcon = CreateNotifyIcon();
        ApplySettingsToUi();
        ApplyLanguage();
        _policyFeedback = new PolicyEditFeedback(PolicyPanel, PolicyStatusText, _localizer, CapturePolicyInputs);
        InitializeDnsControls();
        ResetQualityUi();
        InitializeSpeedTest();
        ResetSpeedUi();
        ResetIpInfoUi();
        ResetReportUi();
        RefreshDnsInfo();
        RefreshAdapterInfo();
        RootNavigation.SelectPanel("StatusPanel");
        UpdateMonitorStatus();
        UpdateSidebarSpeed();
        UpdateWeeklySummary();
        UpdateFirstRunGuide();
        _ = Dispatcher.BeginInvoke(async () =>
        {
            await RunUiActionAsync(CleanupTemporaryWifiProfilesAsync);
            await RunUiActionAsync(ImportSavedWifiProfilesAsync);
            if (!_resourcesCleaned && _settings.StartMonitoringOnLaunch) await StartMonitoringAsync();
        });
        _ = Dispatcher.BeginInvoke(async () => await RunUiActionAsync(RefreshScheduledStartupSettingAsync));

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

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(ScanWifiAsync);
    }

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
            return;
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

        var removedIndex = _profiles.IndexOf(profile);
        if (!_settings.ExcludedWifiSsids.Contains(profile.Ssid, StringComparer.Ordinal))
            _settings.ExcludedWifiSsids.Add(profile.Ssid);
        _profiles.Remove(profile);
        RenumberProfiles();
        UpdateEmptyStates();
        SaveSettings();
        RefreshNearbyPriorityMarkers();
        UpdateWifiSelectionUi();

        ShowUndoDeleteSnackbar(profile, removedIndex);
    }

    /// <summary>Shows a Snackbar offering to restore a just-deleted Wi-Fi profile at its original position.</summary>
    private void ShowUndoDeleteSnackbar(WifiProfile removed, int originalIndex)
    {
        var snackbar = new Wpf.Ui.Controls.Snackbar(RootSnackbar)
        {
            Title = _localizer.T("WifiProfileDeletedTitle"),
            Content = string.Format(_localizer.T("WifiProfileDeletedMessage"), DisplaySsid(removed.Ssid)),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Caution,
            Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Delete24),
            Timeout = TimeSpan.FromSeconds(5)
        };

        var undoButton = new Wpf.Ui.Controls.Button
        {
            Content = _localizer.T("Undo"),
            Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ArrowUndo24),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        undoButton.Click += (_, _) =>
        {
            if (!_profiles.Contains(removed))
            {
                var insertAt = Math.Clamp(originalIndex, 0, _profiles.Count);
                _settings.ExcludedWifiSsids.RemoveAll(ssid => SsidEquals(ssid, removed.Ssid));
                _profiles.Insert(insertAt, removed);
                RenumberProfiles();
                UpdateEmptyStates();
                SaveSettings();
                RefreshNearbyPriorityMarkers();
                ProfileGrid.SelectedItem = removed;
                UpdateWifiSelectionUi();
            }

            undoButton.IsEnabled = false;
        };

        var layout = new StackPanel();
        layout.Children.Add(new TextBlock
        {
            Text = string.Format(_localizer.T("WifiProfileDeletedMessage"), DisplaySsid(removed.Ssid)),
            TextWrapping = TextWrapping.Wrap
        });
        layout.Children.Add(undoButton);
        snackbar.Content = layout;

        snackbar.Show();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var profile = ProfileGrid.SelectedItem as WifiProfile;
        if (profile is null && NearbyGrid.SelectedItem is NearbyWifiRow nearby)
            profile = _profiles.FirstOrDefault(item => SsidEquals(item.Ssid, nearby.Ssid));
        if (profile is null)
        {
            SetWifiSelectionMessage(_localizer.T("SelectProfileFirst"));
            return;
        }
        if (!profile.CredentialSaved)
        {
            SetWifiSelectionMessage(_localizer.T("WifiConnectNeedCredential"));
            return;
        }

        string? completedMessage = null;
        await RunUiActionAsync(async () =>
        {
            await RunExclusiveNetworkActionAsync(async token =>
            {
                var connecting = string.Format(_localizer.T("ConnectingTo"), profile.Ssid);
                SetWifiSelectionMessage(connecting);
                Log(connecting);
                var result = await new ManualWifiConnector(_wifi, _connectivityTester).ConnectAsync(profile, _settings.Policy, token);
                if (_resourcesCleaned) return result.Connected;
                if (result.Report is { } report) ApplyReport(report, ConnectivityReportOrigin.Manual);
                var key = !result.Connected ? string.IsNullOrWhiteSpace(result.Error) ? "ManualWifiFailed" : "ManualWifiRejected"
                    : result.Report?.Ok == true ? "ManualWifiConnected" : "ManualWifiNoInternet";
                var message = string.Format(_localizer.T(key), profile.Ssid);
                if (!string.IsNullOrWhiteSpace(result.Error)) message += " " + result.Error;
                completedMessage = message;
                SetWifiSelectionMessage(message);
                Log(message, result.Connected && result.Report?.Ok == true ? UiLogLevel.Information : UiLogLevel.Warning);
                ShowFeedback(_localizer.T("Connect"), message, result.Connected && result.Report?.Ok == true
                    ? FeedbackSeverity.Success : FeedbackSeverity.Warning);
                return result.Connected;
            });
        });
        if (!_resourcesCleaned && completedMessage is not null) SetWifiSelectionMessage(completedMessage);
    }

    private void SavePolicy_Click(object sender, RoutedEventArgs e)
    {
        var candidate = ReadPolicyFromUi();
        _settings.Profiles = _profiles.ToList();
        PolicySaveTransaction.Commit(_settings, candidate, snapshot => _store.Save(snapshot));
        ApplySettingsToUi();
        _engine.UpdateSchedule();
        var status = string.Format(
            _localizer.T("PolicySavedDetails"),
            FormatDuration(TimeSpan.FromSeconds(_settings.Policy.CheckIntervalSeconds)),
            _settings.Policy.ConsecutiveFailuresBeforeRecovery,
            FormatDuration(TimeSpan.FromSeconds(_settings.Policy.RecoveryCooldownSeconds)));
        _policyFeedback?.MarkSaved(status);
        Log(status);
        ShowFeedback(_localizer.T("PolicySavedTitle"), status, FeedbackSeverity.Success);
        UpdateMonitorStatus();
    }

    private async void ToolsFlushDns_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () => await RunSystemNetworkCommandAsync("ipconfig.exe", ["/flushdns"], _localizer.T("FlushDns")));
        var commandStatus = DnsStatusText.Text;
        var refreshed = RefreshDnsInfo();
        DnsStatusText.Text = refreshed ? commandStatus : $"{commandStatus} {_localizer.T("NetworkReadFailed")}";
    }

    private void DnsRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshDnsInfo();
    }

    private void DnsAdapterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingDnsControls)
        {
            return;
        }

        FillDnsFieldsFromSelectedAdapter();
        SelectDnsPreset("none");
        ApplyDnsPresetToFields();
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
        if (_isApplyingDns) return;
        _isApplyingDns = true;
        DnsEditor.IsEnabled = false;
        DnsRefreshButton.IsEnabled = false;
        DnsFlushButton.IsEnabled = false;
        DnsApplyButton.IsEnabled = false;
        try
        {
            await RunUiActionAsync(ApplyDnsSettingsAsync);
        }
        finally
        {
            _isApplyingDns = false;
            DnsEditor.IsEnabled = true;
            DnsRefreshButton.IsEnabled = true;
            DnsFlushButton.IsEnabled = true;
            UpdateDnsApplyAvailability();
        }
    }

    private async void ToolsRenewDhcp_Click(object sender, RoutedEventArgs e)
    {
        if (AdapterTargetComboBox.SelectedItem is not NetworkAdapterCandidate target)
        {
            SetDhcpOperationMessage("SelectDhcpAdapter", false);
            return;
        }
        using (SetBusy(AdapterRenewDhcpButton))
        {
            AdapterTargetComboBox.IsEnabled = false;
            AdapterRefreshButton.IsEnabled = false;
            try
            {
                if (!await ConfirmActionAsync(_localizer.T("ConfirmDhcpTitle"),
                    string.Format(_localizer.T("ConfirmTargetDhcp"), target.Name)))
                {
                    SetDhcpOperationMessage("ActionCancelled", false);
                    return;
                }
                await RunUiActionAsync(async () => await RunExclusiveNetworkActionAsync(async () =>
                {
                    var commandStarted = false;
                    try
                    {
                        var current = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(adapter => adapter.Id == target.Id);
                        if (current is null || !NetworkAdapterSelection.CanRenewDhcp(target, ReadDhcpCandidate(current)))
                        {
                            SetDhcpOperationMessage("NetworkTargetChanged", false);
                            return false;
                        }
                        SetDhcpOperationMessage("DhcpRenewingTarget", false, target.Name);
                        commandStarted = true;
                        var result = await _networkConfiguration.RenewDhcpAsync(target.Name);
                        var refreshed = RefreshAdapterInfo();
                        var status = SetDhcpOperationMessage(result.Success ? "DhcpTargetDone" : "DhcpTargetFailed", !refreshed, target.Name, result.ExitCode);
                        NetworkLog(status, result.Success ? UiLogLevel.Information : UiLogLevel.Error);
                        if (!string.IsNullOrWhiteSpace(result.Output)) NetworkLog(result.Output);
                        ShowFeedback(_localizer.T(result.Success ? "CommandCompletedTitle" : "ActionFailedTitle"), status,
                            result.Success && refreshed ? FeedbackSeverity.Success : FeedbackSeverity.Warning);
                        return result.Success;
                    }
                    catch (Exception ex) when (ex is NetworkInformationException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException or OperationCanceledException)
                    {
                        var refreshed = RefreshAdapterInfo();
                        var status = SetDhcpOperationMessage(commandStarted ? "DhcpInterruptedCheckCurrent" : "NetworkTargetChanged", !refreshed, target.Name);
                        NetworkLog($"{target.Name}: {ex.Message}", UiLogLevel.Error);
                        ShowFeedback(_localizer.T("ActionFailedTitle"), status, FeedbackSeverity.Warning);
                        return false;
                    }
                }));
            }
            finally
            {
                AdapterTargetComboBox.IsEnabled = true;
                AdapterRefreshButton.IsEnabled = true;
            }
        }
        AdapterRenewDhcpButton.IsEnabled = !_isNetworkActionRunning && AdapterTargetComboBox.Items.Count > 0;
    }

    private void AdapterRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAdapterInfo();
    }

    private async void QualityRun_Click(object sender, RoutedEventArgs e)
    {
        await RunQualityCheckAsync();
    }

    private async void WebsiteRun_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => RunWebsiteTestAsync(_localizer.T("CustomWebsite"), WebsiteUrlTextBox.Text));
    }

    private async void WebsitePreset_Click(object sender, RoutedEventArgs e)
    {
        if (!_websiteSession.IsBusy && sender is Button button && button.Tag is string url)
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

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ThemeComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string themeMode)
        {
            return;
        }

        if (_settings.Theme == themeMode)
        {
            return;
        }

        _settings.Theme = themeMode;
        SaveSettings();
        App.ApplyConfiguredTheme(themeMode);
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

        var enable = ScheduledStartupCheckBox.IsChecked == true;
        if (!enable
            && !await ConfirmActionAsync(
                _localizer.T("ScheduledStartupDisableConfirmTitle"),
                _localizer.T("ScheduledStartupDisableConfirmMessage")))
        {
            _isSyncingScheduledStartup = true;
            ScheduledStartupCheckBox.IsChecked = true;
            _isSyncingScheduledStartup = false;
            return;
        }

        await RunUiActionAsync(async () =>
        {
            var ok = false;
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

    private void ToolsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != ToolsTabControl || ToolsPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        PanelNavigationPresenter.Show(this, "ToolsPanel", _localizer);
    }

    private void ShowPanelByTag(string? tag)
    {
        PanelNavigationPresenter.Show(this, tag, _localizer);
        RenderCurrentWifi();
        if (tag == "WifiPanel") _ = RefreshCurrentWifiAsync();
    }

    private async Task StartMonitoringAsync(bool checkImmediately = true)
    {
        if (_isNetworkActionRunning || _resourcesCleaned) return;
        // 开始监控始终使用已保存策略，不隐式应用表单中的未保存编辑。
        _monitorLease ??= MonitoringLease.TryAcquire(GuardianPaths.MachineDirectory);
        if (_monitorLease is null)
        {
            var background = GuardianStatusStore.Read(GuardianPaths.MachineDirectory);
            var presentation = MonitorStatusPresentation.Create(false, false, background, DateTimeOffset.UtcNow, _localizer);
            var message = _localizer.T(presentation.HasBackground ? "BackgroundAlreadyActiveHint" : "MonitorLeaseUnavailableHint");
            Log(message);
            ShowFeedback(_localizer.T(presentation.HasBackground ? "BackgroundMonitoring" : "MonitorLeaseUnavailable"), message, FeedbackSeverity.Warning);
            UpdateMonitorStatus();
            return;
        }
        _engine.Start(checkImmediately ? null : TimeSpan.FromSeconds(30));
        if (checkImmediately) await RunCheckAsync();
    }

    private void StopMonitoring()
    {
        _engine.Stop();
        // 已开始的恢复收尾结束前不能将控制权交给另一进程。
        _ = ReleaseMonitorLeaseWhenIdleAsync();
    }

    private async Task ReleaseMonitorLeaseWhenIdleAsync()
    {
        while (_engine.IsChecking) await Task.Delay(100);
        if (!_engine.IsMonitoring)
        {
            _monitorLease?.Dispose();
            _monitorLease = null;
        }
    }

    private async Task RunCheckAsync()
    {
        if (_isImmediateCheckRunning || _engine.IsChecking || _resourcesCleaned || _isNetworkActionRunning) return;
        using var cancellation = new CancellationTokenSource();
        _immediateCheckCancellation = cancellation;
        _isImmediateCheckRunning = true;
        UpdateMonitorStatus();
        try
        {
            if (_engine.IsMonitoring)
            {
                await _engine.CheckNowAsync();
            }
            else
            {
                // 手动检测只读，不能绕过后台控制锁自动启用网卡。
                var diagnostics = await _wifi.GetAdapterDiagnosticsAsync(cancellation.Token);
                var ssid = await _wifi.GetCurrentSsidAsync(cancellation.Token);
                var report = await _connectivityTester.RunFullCheckAsync(_settings.Policy, ssid, diagnostics, cancellation.Token);
                if (!_resourcesCleaned) ApplyReport(report, ConnectivityReportOrigin.Manual);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_resourcesCleaned)
            {
                Log(ex.Message, UiLogLevel.Error);
                ShowFeedback(_localizer.T("ActionFailedTitle"), ex.Message, FeedbackSeverity.Warning);
            }
        }
        finally
        {
            _isImmediateCheckRunning = false;
            _immediateCheckCancellation = null;
            if (!_resourcesCleaned) UpdateMonitorStatus();
        }
    }

    private void Engine_StateChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            CheckNowButton.IsEnabled = !_engine.IsChecking;
            UpdateMonitorStatus();
        });
    }

    private void Engine_CheckCompleted(object? sender, CheckCompletedEventArgs e)
    {
        _journal.RecordCheck(e.Report.State, e.Report.Ok);
        Dispatcher.BeginInvoke(() =>
        {
            ApplyReport(e.Report);
            UpdateWeeklySummary();
            EngineLog($"{e.Report.CheckedAt:yyyy-MM-dd HH:mm:ss}  {_localizer.T(e.Report.IsLightweight ? "LightweightCheck" : "FullDiagnosticCheck")} · {StateText(e.Report.State)}: {LocalizeSummary(e.Report.Summary)}", e.Report.Ok ? UiLogLevel.Information : UiLogLevel.Warning);
            SetToolOutput($"{StateText(e.Report.State)}{Environment.NewLine}{LocalizeSummary(e.Report.Summary)}");
        });
    }

    private void Engine_ProfileRecoveryResult(object? sender, ProfileRecoveryResultEventArgs e)
    {
        Dispatcher.BeginInvoke(() => MarkProfileRecoveryResult(e.Ssid, e.Succeeded));
    }

    private void Engine_Event(object? sender, EngineEventArgs e)
    {
        Dispatcher.BeginInvoke(() => HandleEngineEvent(e));
    }

    private void HandleEngineEvent(EngineEventArgs e)
    {
        var args = e.Args;
        switch (e.Kind)
        {
            case EngineEventKind.MonitorStarted:
                EngineLog(_localizer.T("MonitorStartedLog"));
                break;
            case EngineEventKind.MonitorStopped:
                EngineLog(_localizer.T("MonitorStoppedLog"));
                break;
            case EngineEventKind.TryingEnableWifiAdapter:
                EngineLog(string.Format(_localizer.T("TryingEnableWifiAdapter"), args[0]));
                break;
            case EngineEventKind.WifiAdapterEnabled:
                EngineLog(_localizer.T("WifiAdapterEnabled"));
                break;
            case EngineEventKind.WifiAdapterEnableFailed:
                EngineLog(_localizer.T("WifiAdapterEnableFailed"), UiLogLevel.Error);
                break;
            case EngineEventKind.CheckError:
                EngineLog(args[0]?.ToString() ?? _localizer.T("CheckFailedLog"), UiLogLevel.Error);
                break;
            case EngineEventKind.RecoveryCooldownActive:
                EngineLog(string.Format(_localizer.T("RecoveryCooldownActive"), FormatDuration((TimeSpan)args[0]!)));
                break;
            case EngineEventKind.RecoveryStarted:
                _journal.RecordRecoveryStarted(args[0] is int streak ? streak : 0);
                EngineLog(string.Format(_localizer.T("RecoveryStarted"), args[0]));
                break;
            case EngineEventKind.RecoveryDnsFlush:
                EngineLog(_localizer.T("RecoveryDnsFlush"));
                break;
            case EngineEventKind.RecoveryDnsFlushCompleted:
            {
                var label = _localizer.T("FlushDns");
                EngineLog(args[0] is true
                    ? string.Format(_localizer.T("CommandDone"), label)
                    : string.Format(_localizer.T("CommandFailed"), label, args[1]),
                    args[0] is true ? UiLogLevel.Information : UiLogLevel.Warning);
                if (args[2] is string output && !string.IsNullOrWhiteSpace(output))
                {
                    EngineLog(output, args[0] is true ? UiLogLevel.Information : UiLogLevel.Warning);
                }

                break;
            }
            case EngineEventKind.RecoveryReconnectCurrent:
                EngineLog(string.Format(_localizer.T("RecoveryReconnectCurrent"), args[0]));
                break;
            case EngineEventKind.RecoverySwitchWifi:
                EngineLog(string.Format(_localizer.T("RecoverySwitchWifi"), args[0]));
                break;
            case EngineEventKind.RecoveryWifiConnected:
            {
                _journal.RecordRecoveryResult(true, args[0]?.ToString());
                var connectedMessage = string.Format(_localizer.T("RecoveryWifiConnected"), args[0]);
                EngineLog(connectedMessage);
                if (!IsVisible || WindowState == WindowState.Minimized)
                {
                    ShowTrayBalloon(connectedMessage, Forms.ToolTipIcon.Info);
                }

                SaveSettings();
                UpdateWeeklySummary();
                break;
            }
            case EngineEventKind.RecoveryNoActionsEnabled:
                EngineLog(_localizer.T("RecoveryNoActionsEnabled"));
                break;
            case EngineEventKind.RecoveryFailed:
            {
                _journal.RecordRecoveryResult(false, null);
                SaveSettings();
                var failedMessage = _localizer.T("RecoveryFailed");
                EngineLog(failedMessage, UiLogLevel.Warning);
                if (!IsVisible || WindowState == WindowState.Minimized)
                {
                    ShowTrayBalloon(failedMessage, Forms.ToolTipIcon.Error);
                }

                UpdateWeeklySummary();
                break;
            }
            case EngineEventKind.HighRiskCooldownActive:
                EngineLog(string.Format(_localizer.T("HighRiskCooldownActive"), FormatDuration((TimeSpan)args[0]!)));
                break;
            case EngineEventKind.HighRiskActionStarted:
            {
                var message = string.Format(_localizer.T("HighRiskActionStarted"), args[0]);
                EngineLog(message, UiLogLevel.Warning);
                ShowTrayBalloon(message);
                break;
            }
            case EngineEventKind.HighRiskActionCompleted:
            {
                var action = args[0]?.ToString() ?? "";
                var commandOk = args[1] is true;
                var networkOk = args[2] is true;
                _journal.RecordHighRisk(action, commandOk, networkOk);
                HighRiskAlertWriter.Write(new HighRiskAlert(
                    DateTimeOffset.Now, action, commandOk, networkOk,
                    args[3]?.ToString(), args[4]?.ToString()));
                var message = string.Format(
                    _localizer.T("HighRiskActionDone"),
                    action,
                    _localizer.T(commandOk ? "HighRiskCmdOk" : "HighRiskCmdFailed"),
                    _localizer.T(networkOk ? "HighRiskNetRecovered" : "HighRiskNetStillDown"));
                EngineLog(message, commandOk && networkOk ? UiLogLevel.Information : UiLogLevel.Warning);
                ShowTrayBalloon(message);
                UpdateWeeklySummary();
                break;
            }
        }
    }

    private void HighRiskCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingHighRiskToggle || _settings.Policy.EnableHighRiskRecovery)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            _localizer.T("HighRiskConfirmText"),
            _localizer.T("HighRiskConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            _isSyncingHighRiskToggle = true;
            HighRiskCheckBox.IsChecked = false;
            _isSyncingHighRiskToggle = false;
        }
    }

    private void ShowTrayBalloon(string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Warning)
    {
        try
        {
            if (!_isNotifyIconDisposed)
            {
                _notifyIcon.ShowBalloonTip(5000, _localizer.T("AppTitle"), message, icon);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static List<string> ParseProbeHosts(string text)
    {
        return text
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private void UpdateFirstRunGuide()
    {
        if (_isFirstRun && _profiles.Count > 0 && _engine.IsMonitoring)
        {
            _isFirstRun = false;
        }

        FirstRunGuideText.Visibility = _isFirstRun ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateWeeklySummary()
    {
        try
        {
            var summary = _journal.GetSummary();
            WeeklyOutagesText.Text = summary.OutageEpisodes.ToString();
            WeeklyRateText.Text = DashboardPresentation.SuccessRate(summary.RecoverySuccesses, summary.RecoveryAttempts);
            WeeklySummaryText.Text = string.Format(
                _localizer.T("WeeklySummary"),
                summary.OutageEpisodes,
                summary.RecoverySuccesses,
                summary.RecoveryAttempts);
        }
        catch (Exception ex)
        {
            WeeklySummaryText.Text = ex.Message;
            WeeklyOutagesText.Text = WeeklyRateText.Text = "—";
        }
    }

    private void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                ShowNewFolderButton = true
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK)
            {
                return;
            }

            var sourceDirectories = new[] { _fileLog is FileLogSink sink ? sink.Directory : null, _journal.Directory }
                .Where(directory => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                .Select(directory => directory!)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var copied = 0;
            foreach (var directory in sourceDirectories)
            {
                foreach (var pattern in new[] { "app-*.log", "events-*.jsonl", "crash.log" })
                {
                    foreach (var file in Directory.EnumerateFiles(directory, pattern))
                    {
                        File.Copy(file, System.IO.Path.Combine(dialog.SelectedPath, System.IO.Path.GetFileName(file)), overwrite: true);
                        copied++;
                    }
                }
            }

            Log(copied > 0
                ? string.Format(_localizer.T("ExportLogsDone"), dialog.SelectedPath)
                : _localizer.T("ExportLogsNone"));
        }
        catch (Exception ex)
        {
            Log(string.Format(_localizer.T("ExportLogsFailed"), ex.Message));
        }
    }

    private void MarkProfileRecoveryResult(string ssid, bool succeeded)
    {
        var profile = _profiles.FirstOrDefault(item => SsidEquals(item.Ssid, ssid));
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

    private async Task AddWifiProfileAsync(NearbyWifiRow? network, string? initialPassword = null)
    {
        if (_isNetworkActionRunning || _resourcesCleaned) return;
        var dialog = new AddWifiDialog(_localizer, network is null ? null : ToWifiNetwork(network), initialPassword) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Ssid))
        {
            return;
        }

        var ssid = dialog.Ssid;
        var existing = _profiles.FirstOrDefault(profile => SsidEquals(profile.Ssid, ssid));
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
        _settings.ExcludedWifiSsids.RemoveAll(item => SsidEquals(item, ssid));
        RenumberProfiles();
        UpdateEmptyStates();
        SaveSettings();
        RefreshNearbyPriorityMarkers();
        ProfileGrid.SelectedItem = _profiles.FirstOrDefault(profile => SsidEquals(profile.Ssid, ssid));
        UpdateWifiSelectionUi();
    }

    private async Task ConfigureProfileCredentialAsync(WifiProfile profile, string? initialPassword = null)
    {
        if (_isNetworkActionRunning || _resourcesCleaned) return;
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

        var ssid = dialog.Ssid;
        if (!SsidEquals(ssid, profile.Ssid)
            && _profiles.Any(item => SsidEquals(item.Ssid, ssid)))
        {
            Log(string.Format(_localizer.T("WifiProfileExists"), ssid));
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
        profile.WindowsProfileName = ssid;
        profile.Security = dialog.Authentication;
        profile.CredentialSaved = true;
        ProfileGrid.Items.Refresh();
        SaveSettings();
        RefreshNearbyPriorityMarkers();
        UpdateWifiSelectionUi();
    }

    private Task<bool> ValidateAndSaveWifiProfileAsync(string ssid, string? password, string authentication) =>
        RunExclusiveNetworkActionAsync(() => ValidateAndSaveWifiProfileCoreAsync(ssid, password, authentication));

    private async Task<bool> ValidateAndSaveWifiProfileCoreAsync(string ssid, string? password, string authentication)
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
        var commandSent = await ConnectSavedWifiAsync(previousSsid);
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
        return RecoveryPlanner.SsidEquals(left, right);
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
        var profile = _profiles.FirstOrDefault(item => SsidEquals(item.Ssid, row.Ssid));
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

        var actions = WifiActionAvailability.Create(_profiles, profile, nearby?.Ssid, _isNetworkActionRunning);
        AddSelectedButton.IsEnabled = actions.CanAdd;
        SetPasswordButton.IsEnabled = ImportPasswordButton.IsEnabled = actions.CanEdit;
        RemoveButton.IsEnabled = !_isNetworkActionRunning && hasProfile;
        ConnectButton.IsEnabled = actions.CanConnect;
        ConnectButton.ToolTip = _localizer.T(_isNetworkActionRunning ? "ManualNetworkRunning"
            : actions.ConnectTarget is null ? "SelectProfileFirst"
            : actions.ConnectTarget.CredentialSaved ? "ConnectWifiTip" : "WifiConnectNeedCredential");
        System.Windows.Automation.AutomationProperties.SetHelpText(ConnectButton, ConnectButton.ToolTip.ToString());
        ToolTipService.SetShowOnDisabled(ConnectButton, true);
        if (_isNetworkActionRunning && _networkActionStatusKey is not null)
            CurrentWifiSelectionText.Text = _localizer.T(_networkActionStatusKey);

        var profileIndex = hasProfile ? _profiles.IndexOf(profile!) : -1;
        MoveUpButton.IsEnabled = !_isNetworkActionRunning && profileIndex > 0;
        MoveDownButton.IsEnabled = !_isNetworkActionRunning && profileIndex >= 0 && profileIndex < _profiles.Count - 1;
    }

    private void SetWifiSelectionMessage(string message)
    {
        CurrentWifiSelectionText.Text = message;
        SetWifiSelectionVisual(false);
    }

    private void SetWifiSelectionVisual(bool hasSelection)
    {
        AppThemePolicy.BindBrush(WifiSelectionBar, Border.BackgroundProperty,
            hasSelection ? "RowSelectedBrush" : "ControlFillColorDefaultBrush");
        AppThemePolicy.BindBrush(WifiSelectionBar, Border.BorderBrushProperty,
            hasSelection ? "StatusHealthyBrush" : "CardStrokeColorDefaultBrush");
    }

    private enum FeedbackSeverity
    {
        Info,
        Success,
        Warning,
        Danger
    }

    /// <summary>Shows a transient Snackbar with severity-mapped appearance and icon.</summary>
    private void ShowFeedback(string title, string message, FeedbackSeverity severity)
    {
        var (appearance, symbol) = severity switch
        {
            FeedbackSeverity.Success => (Wpf.Ui.Controls.ControlAppearance.Success, Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24),
            FeedbackSeverity.Warning => (Wpf.Ui.Controls.ControlAppearance.Caution, Wpf.Ui.Controls.SymbolRegular.Warning24),
            FeedbackSeverity.Danger => (Wpf.Ui.Controls.ControlAppearance.Danger, Wpf.Ui.Controls.SymbolRegular.DismissCircle24),
            _ => (Wpf.Ui.Controls.ControlAppearance.Info, Wpf.Ui.Controls.SymbolRegular.Info24)
        };

        var timeout = severity == FeedbackSeverity.Success
            ? TimeSpan.FromSeconds(2)
            : TimeSpan.FromSeconds(5);

        _snackbar.Show(title, message, appearance, new Wpf.Ui.Controls.SymbolIcon(symbol), timeout);
    }

    /// <summary>Called by the global exception handler to surface an unhandled error to the user.</summary>
    public void NotifyUnhandledError()
    {
        try
        {
            ShowFeedback(_localizer.T("UnhandledErrorTitle"), _localizer.T("UnhandledErrorMessage"), FeedbackSeverity.Danger);
        }
        catch
        {
        }
    }

    /// <summary>Swaps a button's content for an inline ProgressRing + original label while a long task runs,
    /// disabling it; restores the original content and enabled state when done. Safe to nest per-button.</summary>
    private IDisposable SetBusy(System.Windows.Controls.Primitives.ButtonBase button)
    {
        var originalContent = button.Content;
        var wasEnabled = button.IsEnabled;

        var ring = new Wpf.Ui.Controls.ProgressRing
        {
            IsIndeterminate = true,
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = originalContent?.ToString() ?? "",
            VerticalAlignment = VerticalAlignment.Center
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(ring);
        panel.Children.Add(label);

        button.Content = panel;
        button.IsEnabled = false;

        return new BusyScope(() =>
        {
            button.Content = originalContent;
            button.IsEnabled = wasEnabled;
        });
    }

    private sealed class BusyScope(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;

        public void Dispose()
        {
            _onDispose?.Invoke();
            _onDispose = null;
        }
    }

    private string DisplaySsid(string ssid)
    {
        return string.IsNullOrWhiteSpace(ssid) ? _localizer.T("HiddenWifi") : ssid;
    }

    private void ApplyReport(ConnectivityReport report, ConnectivityReportOrigin origin = ConnectivityReportOrigin.Foreground, bool forceRefresh = false)
    {
        if (_resourcesCleaned) return;
        var unchanged = _reportSelection.Report?.CheckedAt == report.CheckedAt && _reportSelection.Origin == origin;
        if (!_reportSelection.TryAccept(report, origin) || unchanged && !forceRefresh) return;
        HealthStateText.Text = StateText(report.State);
        RenderDashboardMetrics(report);
        AppThemePolicy.BindBrush(HealthStateText, TextBlock.ForegroundProperty, report.State switch
        {
            NetworkState.Healthy => "StatusHealthyBrush",
            NetworkState.Recovering => "StatusWarningBrush",
            NetworkState.GatewayIssue or NetworkState.DnsIssue or NetworkState.InternetIssue => "StatusWarningBrush",
            _ => "StatusDangerBrush"
        });
        SummaryText.Text = LocalizeSummary(report.Summary);
        ReportContextText.Text = ConnectivityReportPresentation.Context(report, origin, _localizer);
        ReportContextText.Visibility = Visibility.Visible;
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
            NetworkState.AdapterFault => _localizer.T("AdapterFault"),
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
            "Lightweight internet check succeeded." => _localizer.T("LightweightCheckSucceeded"),
            "Lightweight internet check failed." => _localizer.T("LightweightCheckFailed"),
            "Public HTTPS probes did not reach quorum." => _localizer.T("HttpsQuorumFailed"),
            _ => summary
        };
    }

    private void ApplySettingsToUi()
    {
        using var policyLoading = _policyFeedback?.SuppressChanges();
        SetTimeFields(_settings.Policy.CheckIntervalSeconds, IntervalTextBox, IntervalUnitComboBox);
        SetTimeFields(_settings.Policy.RecoveryCooldownSeconds, CooldownTextBox, CooldownUnitComboBox);
        FailuresTextBox.Value = _settings.Policy.ConsecutiveFailuresBeforeRecovery;
        AutoSwitchCheckBox.IsChecked = _settings.Policy.EnableAutoSwitch;
        SoftReconnectCheckBox.IsChecked = _settings.Policy.EnableCurrentWifiReconnect;
        WifiAutoEnableCheckBox.IsChecked = _settings.Policy.EnableWifiAdapterAutoEnable;
        DnsFlushCheckBox.IsChecked = _settings.Policy.EnableDnsFlush;
        _isSyncingHighRiskToggle = true;
        HighRiskCheckBox.IsChecked = _settings.Policy.EnableHighRiskRecovery;
        _isSyncingHighRiskToggle = false;
        DnsProbeHostsTextBox.Text = string.Join(", ", _settings.Policy.DnsProbeHosts);
        HttpsProbeHostsTextBox.Text = string.Join(", ", _settings.Policy.HttpsProbeHosts);
        StartMonitoringOnLaunchCheckBox.IsChecked = _settings.StartMonitoringOnLaunch;
        StartMinimizedToTrayCheckBox.IsChecked = _settings.StartMinimizedToTray;
        SelectComboByTag(LanguageComboBox, _settings.Language);
        SelectComboByTag(ThemeComboBox, _settings.Theme);
        SelectComboByTag(CloseBehaviorComboBox, _settings.CloseBehavior);
    }

    private static void SetTimeFields(int seconds, Wpf.Ui.Controls.NumberBox numberBox, ComboBox comboBox)
    {
        var unit = seconds % 3600 == 0 ? 3600 : seconds % 60 == 0 ? 60 : 1;
        numberBox.Value = Math.Max(1, seconds / unit);
        SelectComboByTag(comboBox, unit.ToString());
    }

    private MonitorPolicy ReadPolicyFromUi()
    {
        // 在独立候选上编辑，磁盘写入成功前不改变引擎正在读取的策略对象。
        var policy = System.Text.Json.JsonSerializer.Deserialize<MonitorPolicy>(System.Text.Json.JsonSerializer.Serialize(_settings.Policy))!;
        policy.CheckIntervalSeconds = ReadTimeSeconds(IntervalTextBox, IntervalUnitComboBox, policy.CheckIntervalSeconds);
        policy.RecoveryCooldownSeconds = ReadTimeSeconds(CooldownTextBox, CooldownUnitComboBox, policy.RecoveryCooldownSeconds);

        if (FailuresTextBox.Value is double failures)
        {
            policy.ConsecutiveFailuresBeforeRecovery = (int)Math.Round(failures);
        }

        policy.EnableAutoSwitch = AutoSwitchCheckBox.IsChecked == true;
        policy.EnableCurrentWifiReconnect = SoftReconnectCheckBox.IsChecked == true;
        policy.EnableWifiAdapterAutoEnable = WifiAutoEnableCheckBox.IsChecked == true;
        policy.EnableDnsFlush = DnsFlushCheckBox.IsChecked == true;
        policy.EnableHighRiskRecovery = HighRiskCheckBox.IsChecked == true;
        policy.DnsProbeHosts = ParseProbeHosts(DnsProbeHostsTextBox.Text);
        policy.HttpsProbeHosts = ParseProbeHosts(HttpsProbeHostsTextBox.Text);
        policy.Normalize();
        return policy;
    }

    private string CapturePolicyInputs() => System.Text.Json.JsonSerializer.Serialize(new
    {
        IntervalTextBox.Value,
        IntervalUnit = (IntervalUnitComboBox.SelectedItem as ComboBoxItem)?.Tag,
        Failures = FailuresTextBox.Value,
        Cooldown = CooldownTextBox.Value,
        CooldownUnit = (CooldownUnitComboBox.SelectedItem as ComboBoxItem)?.Tag,
        AutoSwitchCheckBox.IsChecked,
        Reconnect = SoftReconnectCheckBox.IsChecked,
        AutoEnable = WifiAutoEnableCheckBox.IsChecked,
        FlushDns = DnsFlushCheckBox.IsChecked,
        HighRisk = HighRiskCheckBox.IsChecked,
        Dns = DnsProbeHostsTextBox.Text,
        Https = HttpsProbeHostsTextBox.Text
    });

    private static int ReadTimeSeconds(Wpf.Ui.Controls.NumberBox numberBox, ComboBox comboBox, int fallback)
    {
        if (numberBox.Value is not double raw || raw < 1)
        {
            return fallback;
        }

        var value = (int)Math.Round(raw);
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
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(_localizer.Language);
        Title = _localizer.T("AppTitle");
        Resources["TableDetailLabel"] = _localizer.T("Detail");
        Resources["TableCopyLabel"] = _localizer.T("SpeedCopy");
        Resources["TableDragLabel"] = _localizer.T("DragHint");
        TitleBar.Title = _localizer.T("AppTitle");
        StatusNavButton.Content = _localizer.T("Status");
        WifiNavButton.Content = _localizer.T("Wifi");
        PolicyNavButton.Content = _localizer.T("Policy");
        ToolsNavButton.Content = _localizer.T("Tools");
        DnsNavButton.Content = _localizer.T("DnsTools");
        QualityNavButton.Content = _localizer.T("QualityTools");
        AdapterNavButton.Content = _localizer.T("AdapterTools");
        LogNavButton.Content = _localizer.T("Log");
        ExportLogsButton.Content = _localizer.T("ExportLogs");
        RefreshLogLanguage();
        SettingsNavButton.Content = _localizer.T("Settings");
        CheckNowButton.Content = _localizer.T("CheckNow");
        StartMonitorButton.Content = _engine.IsMonitoring ? _localizer.T("Monitoring") : _localizer.T("StartMonitor");
        StopButton.Content = _localizer.T("Stop");
        HealthStateLabel.Text = _localizer.T("HealthState");
        WeeklyPeriodText.Text = _localizer.T("UiLastSevenDays");
        WeeklyOutagesLabel.Text = _localizer.T("UiOutages");
        WeeklyRateLabel.Text = _localizer.T("UiRecoveryRate");
        RenderDashboardMetrics(_reportSelection.Report);
        MonitorStateLabel.Text = _localizer.T("MonitorStatus");
        NextCheckLabel.Text = _localizer.T("NextCheck");
        CurrentNetworkLabel.Text = _localizer.T("CurrentNetwork");
        FailureStreakLabel.Text = _localizer.T("FailureStreak");
        ProbeTitleText.Text = _localizer.T("CheckNow");
        RecentEventsTitle.Text = _localizer.T("RecentEvents");
        UpdateWeeklySummary();
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
        WebsiteEmptyText.Text = _localizer.T("NoWebsiteRows");
        IpInfoEmptyText.Text = _localizer.T("NoIpInfoRows");
        ReportEmptyText.Text = _localizer.T("NoReportRows");
        DnsEmptyText.Text = _localizer.T("NoDnsRows");
        AdapterEmptyText.Text = _localizer.T("NoAdapterRows");
        QualityEmptyText.Text = _localizer.T("NoQualityRows");
        ((CredentialStatusConverter)Resources["CredentialStatusConverter"]).Localizer = _localizer;
        ProfileGrid.Items.Refresh();
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
        HighRiskCheckBox.Content = _localizer.T("HighRiskRecovery");
        HighRiskTipText.Text = _localizer.T("HighRiskTip");
        DnsProbeHostsLabel.Text = _localizer.T("DnsProbeHostsLabel");
        HttpsProbeHostsLabel.Text = _localizer.T("HttpsProbeHostsLabel");
        ProbeTargetsTipText.Text = _localizer.T("ProbeTargetsTip");
        FirstRunGuideText.Text = _localizer.T("FirstRunGuide");
        SavePolicyButton.Content = _localizer.T("Save");
        _policyFeedback?.RefreshLanguage();
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
        DnsTargetLabel.Text = _localizer.T("DnsTargetLabel");
        DnsPlanLabel.Text = _localizer.T("DnsPlanLabel");
        DnsPrimaryLabel.Text = _localizer.T("DnsPrimaryLabel");
        DnsSecondaryLabel.Text = _localizer.T("DnsSecondaryLabel");
        AdapterTargetLabel.Text = _localizer.T("AdapterTargetLabel");
        AdapterTargetComboBox.DisplayMemberPath = nameof(NetworkAdapterCandidate.Name);
        DnsAdapterColumn.Header = _localizer.T("Adapter");
        DnsServersColumn.Header = _localizer.T("DnsServersAll");
        DnsSourceColumn.Header = _localizer.T("Source");
        DnsStatusColumn.Header = _localizer.T("StatusColumn");
        foreach (ComboBoxItem item in DnsPresetComboBox.Items)
            item.Content = _localizer.T(item.Tag?.ToString() switch
            {
                "automatic" => "AutomaticDns", "ali" => "AliDns", "tencent" => "TencentDns",
                "cloudflare" => "CloudflareDns", "google" => "GoogleDns", "custom" => "CustomDns", _ => "SelectDnsPlan"
            });
        RelocalizeNetworkInfo();
        FillDnsFieldsFromSelectedAdapter();
        if (_dnsReadFailed) DnsCurrentText.Text = DnsEmptyText.Text = _localizer.T("NetworkReadFailed");
        if (_adapterReadFailed) AdapterEmptyText.Text = _localizer.T("NetworkReadFailed");
        DnsStatusText.Text = _dnsOperationMessage?.Format(_localizer) ?? (_dnsReadFailed ? _localizer.T("NetworkReadFailed")
            : _dnsRows.Count == 0 ? _localizer.T("NoDnsServers") : string.Format(_localizer.T("DnsServerCount"), _dnsRows.Count));
        AdapterStatusText.Text = _dhcpOperationMessage?.Format(_localizer) ?? (_adapterReadFailed ? _localizer.T("NetworkReadFailed")
            : AdapterTargetComboBox.Items.Count == 0 ? _localizer.T("NoDhcpAdapter") : string.Format(_localizer.T("AdapterCount"), _adapterRows.Count));
        QualityToolsTitle.Text = _localizer.T("QualityCheckTitle");
        QualityScopeText.Text = _localizer.T("QualityCheckScope");
        QualityRunButton.Content = _localizer.T("RunQualityCheck");
        QualityCancelButton.Content = _localizer.T("Cancel");
        QualityOpenSettingsButton.Content = _localizer.T("OpenNetworkSettings");
        QualityGatewayLabel.Text = _localizer.T("QualityGatewayTiming");
        QualityDnsLabel.Text = _localizer.T("QualityDnsTiming");
        QualityHttpsLabel.Text = _localizer.T("QualityHttpsTiming");
        QualityProbeColumn.Header = _localizer.T("Probe");
        QualityResultColumn.Header = _localizer.T("Result");
        QualityLatencyColumn.Header = _localizer.T("ProbeElapsed");
        QualityDetailColumn.Header = _localizer.T("Detail");
        RefreshQualityUi();
        RefreshSpeedView();
        WebsiteRunButton.Content = _localizer.T("Test");
        WebsiteAppleButton.Content = "Apple";
        WebsiteGitHubButton.Content = "GitHub";
        WebsiteGoogleButton.Content = "Google";
        WebsiteYouTubeButton.Content = "YouTube";
        WebsiteNameColumn.Header = _localizer.T("Website");
        WebsiteStatusColumn.Header = _localizer.T("StatusColumn");
        WebsiteHttpColumn.Header = "HTTP";
        WebsiteDnsColumn.Header = _localizer.T("WebsiteDnsTime");
        WebsiteLatencyColumn.Header = _localizer.T("WebsiteHttpTime");
        WebsiteAddressColumn.Header = "IP";
        WebsiteDetailColumn.Header = _localizer.T("Detail");
        IpInfoRefreshButton.Content = _localizer.T("QueryPublicIpInfo");
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
        AdapterDnsColumn.Header = _localizer.T("DnsServersAll");
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
        ThemeLabel.Text = _localizer.T("ThemeMode");
        ((ComboBoxItem)ThemeComboBox.Items[0]).Content = _localizer.T("ThemeAuto");
        ((ComboBoxItem)ThemeComboBox.Items[1]).Content = _localizer.T("ThemeLight");
        ((ComboBoxItem)ThemeComboBox.Items[2]).Content = _localizer.T("ThemeDark");
        ((ComboBoxItem)IntervalUnitComboBox.Items[0]).Content = _localizer.T("Seconds");
        ((ComboBoxItem)IntervalUnitComboBox.Items[1]).Content = _localizer.T("Minutes");
        ((ComboBoxItem)IntervalUnitComboBox.Items[2]).Content = _localizer.T("Hours");
        ((ComboBoxItem)CooldownUnitComboBox.Items[0]).Content = _localizer.T("Seconds");
        ((ComboBoxItem)CooldownUnitComboBox.Items[1]).Content = _localizer.T("Minutes");
        ((ComboBoxItem)CooldownUnitComboBox.Items[2]).Content = _localizer.T("Hours");
        ((ComboBoxItem)CloseBehaviorComboBox.Items[0]).Content = _localizer.T("CloseAsk");
        ((ComboBoxItem)CloseBehaviorComboBox.Items[1]).Content = _localizer.T("CloseExit");
        ((ComboBoxItem)CloseBehaviorComboBox.Items[2]).Content = _localizer.T("CloseTray");
        SidebarSpeedTitle.Text = _localizer.T("InterfaceTraffic");
        SidebarSpeedScopeText.Text = _localizer.T("InterfaceTrafficSummary");
        SidebarUploadLegend.Text = "↑ " + _localizer.T("UploadSpeed");
        SidebarDownloadLegend.Text = "↓ " + _localizer.T("DownloadSpeed");
        if (HealthStateText.Text == "" || HealthStateText.Text == "尚未检测" || HealthStateText.Text == "Not checked")
        {
            HealthStateText.Text = _localizer.T("NotChecked");
        }

        ApplyTooltips();
        if (_reportSelection.Report is { } currentReport) ApplyReport(currentReport, _reportSelection.Origin, forceRefresh: true);
        RefreshNearbyPriorityMarkers();
        UpdateWifiSelectionUi();
        RefreshWifiScanUi();
        RefreshWebsiteUi();
        RefreshIpToolUi();
        RefreshDiagnosticReportUi();
        UpdateEmptyStates();
        UpdateMonitorStatus();
        UpdateTrayText();
        ShowPanelByTag((RootNavigation.SelectedItem as Wpf.Ui.Controls.NavigationViewItem)?.Tag?.ToString());
    }

    private void UpdateEmptyStates()
    {
        NearbyEmptyText.Visibility = _nearbyNetworks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProfileEmptyText.Visibility = _profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentEventsEmptyText.Visibility = _recentEvents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WebsiteEmptyText.Visibility = _websiteRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        IpInfoEmptyText.Visibility = _ipInfoRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReportEmptyText.Visibility = _reportRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DnsEmptyText.Visibility = _dnsRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QualityEmptyText.Visibility = _qualityRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AdapterEmptyText.Visibility = _adapterRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        SetTooltip(IntervalTextBox, "AdaptiveIntervalTip");
        SetTooltip(ToolsCheckButton, "CheckNowTip");
        SetTooltip(ToolsScanButton, "ScanTip");
        SetTooltip(ToolsNetworkSettingsButton, "OpenNetworkSettingsTip");
        SetTooltip(DnsRefreshButton, "DnsRefreshTip");
        SetTooltip(DnsFlushButton, "DnsFlushTip");
        SetTooltip(DnsApplyButton, "DnsApplyTip");
        SetTooltip(QualityRunButton, "QualityRunTip");
        SetTooltip(QualityOpenSettingsButton, "OpenNetworkSettingsTip");
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

    private void UpdateMonitorStatus()
    {
        var background = GuardianStatusStore.Read(GuardianPaths.MachineDirectory);
        var presentation = MonitorStatusPresentation.Create(_engine.IsMonitoring, _engine.IsChecking || _isImmediateCheckRunning,
            background, DateTimeOffset.UtcNow, _localizer, _isNetworkActionRunning);
        MonitorStateText.Text = presentation.OwnerText;
        HeaderStateText.Text = presentation.ActivityText;
        System.Windows.Automation.AutomationProperties.SetName(StatusDot, presentation.ActivityText);
        AppThemePolicy.BindBrush(StatusDot, System.Windows.Shapes.Shape.FillProperty, presentation.BrushKey);
        HeaderStateText.ToolTip = presentation.Hint;
        CheckNowButton.ToolTip = presentation.CanCheck ? _localizer.T("CheckNowTip") : presentation.Hint;
        StartMonitorButton.ToolTip = presentation.Hint;
        StopButton.ToolTip = presentation.Hint;
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(StartMonitorButton, true);
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(StopButton, true);
        StartMonitorButton.Content = _localizer.T(_engine.IsMonitoring ? "Monitoring" : "StartMonitor");
        StartMonitorButton.IsEnabled = presentation.CanStart;
        StopButton.IsEnabled = presentation.CanStop;
        CheckNowButton.IsEnabled = presentation.CanCheck;
        CheckNowButton.Content = _localizer.T(_isImmediateCheckRunning ? "Checking" : "CheckNow");
        UpdateTrayText(presentation);
        if (presentation.HasBackground && background is not null)
        {
            if (!_isNetworkActionRunning && background.State != "ManualControl" && background.Report is { } report)
                ApplyReport(report, ConnectivityReportOrigin.Background);
            FailureStreakText.Text = background.FailureStreak.ToString();
            NextCheckText.Text = background.NextCheckAt.HasValue
                ? FormatDuration(background.NextCheckAt.Value > DateTimeOffset.Now ? background.NextCheckAt.Value - DateTimeOffset.Now : TimeSpan.Zero)
                : "—";
            return;
        }
        FailureStreakText.Text = _engine.FailureStreak.ToString();

        if (_engine.IsMonitoring && _engine.NextCheckAt.HasValue)
        {
            var remaining = _engine.NextCheckAt.Value - DateTimeOffset.Now;
            NextCheckText.Text = remaining <= TimeSpan.Zero ? "0s" : FormatDuration(remaining);
        }
        else
        {
            NextCheckText.Text = "-";
        }

        UpdateFirstRunGuide();
    }

    private void UpdateSidebarSpeed()
    {
        var now = Stopwatch.GetTimestamp();
        var rate = _trafficSampler.Sample(ReadNetworkByteCounters(), _lastSidebarSpeedTimestamp is { } last
            ? Stopwatch.GetElapsedTime(last, now) : TimeSpan.Zero);
        _lastSidebarSpeedTimestamp = now;
        var scope = _localizer.T("InterfaceTrafficScope");
        if (rate is null)
        {
            _downloadSpeedSamples.Clear();
            _uploadSpeedSamples.Clear();
            SidebarDownloadSpeedText.Text = SidebarUploadSpeedText.Text = "—";
            SidebarDownloadUnitText.Text = SidebarUploadUnitText.Text = "";
            SidebarCompactDownloadText.Text = SidebarCompactUploadText.Text = "—";
            SetTrafficTooltips($"{_localizer.T("TrafficAwaitingSample")}\n{scope}");
            DrawSidebarSpeedLines();
            return;
        }
        var downloadBytesPerSecond = rate.DownloadBytesPerSecond;
        var uploadBytesPerSecond = rate.UploadBytesPerSecond;

        PushSpeedSample(_downloadSpeedSamples, downloadBytesPerSecond);
        PushSpeedSample(_uploadSpeedSamples, uploadBytesPerSecond);
        SetRateText(SidebarDownloadSpeedText, SidebarDownloadUnitText, downloadBytesPerSecond);
        SetRateText(SidebarUploadSpeedText, SidebarUploadUnitText, uploadBytesPerSecond);
        SidebarCompactDownloadText.Text = FormatRateCompact(downloadBytesPerSecond);
        SidebarCompactUploadText.Text = FormatRateCompact(uploadBytesPerSecond);
        SetTrafficTooltips($"↑ {FormatRate(uploadBytesPerSecond)}   ↓ {FormatRate(downloadBytesPerSecond)}\n{string.Format(_localizer.T("TrafficSampledInterfaces"), rate.SampledInterfaces, rate.CurrentInterfaces)}\n{scope}");
        DrawSidebarSpeedLines();
    }

    private void SetTrafficTooltips(string text)
    {
        foreach (var card in new[] { SidebarSpeedCard, SidebarSpeedCompact })
        {
            if (card.ToolTip is ToolTip { Content: TextBlock content }) content.Text = text;
            else card.ToolTip = new ToolTip { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 } };
            System.Windows.Automation.AutomationProperties.SetHelpText(card, text);
        }
    }

    private static IReadOnlyList<NetworkByteCounter> ReadNetworkByteCounters()
    {
        var counters = new List<NetworkByteCounter>();
        NetworkInterface[] interfaces;
        try { interfaces = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return counters; }
        foreach (var networkInterface in interfaces
            .Where(item => item.OperationalStatus == OperationalStatus.Up
                && item.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && item.NetworkInterfaceType != NetworkInterfaceType.Tunnel))
        {
            try
            {
                var statistics = networkInterface.GetIPv4Statistics();
                counters.Add(new(networkInterface.Id, statistics.BytesReceived, statistics.BytesSent));
            }
            catch (NetworkInformationException)
            {
            }
        }

        return counters;
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
        SidebarDownloadLine.Points = BuildSparklinePoints(_downloadSpeedSamples, (double)FindResource("TrafficChartWidth"), (double)FindResource("TrafficChartHeight"), max);
        SidebarUploadLine.Points = BuildSparklinePoints(_uploadSpeedSamples, (double)FindResource("TrafficChartWidth"), (double)FindResource("TrafficChartHeight"), max);
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

    private static string FormatRate(double bytesPerSecond)
    {
        var (value, unit) = FormatRateParts(bytesPerSecond);
        return $"{value} {unit}";
    }

    private static string FormatRateCompact(double bytesPerSecond)
    {
        var (value, unit) = FormatRateParts(bytesPerSecond);
        return value + (unit switch
        {
            "KiB/s" => "Ki",
            "MiB/s" => "Mi",
            "GiB/s" => "Gi",
            _ => "B"
        });
    }

    private static (string Value, string Unit) FormatRateParts(double bytesPerSecond)
    {
        var units = new[] { "B/s", "KiB/s", "MiB/s", "GiB/s" };
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
            Log(ex.Message, UiLogLevel.Error);
            ShowFeedback(_localizer.T("ActionFailedTitle"), ex.Message, FeedbackSeverity.Danger);
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
        await RunExclusiveNetworkActionAsync(async () =>
        {
            await RunSystemNetworkCommandCoreAsync(fileName, arguments, label);
            return true;
        });
    }

    private async Task RunSystemNetworkCommandCoreAsync(string fileName, string[] arguments, string label)
    {
        NetworkLog($"{label}...");
        SetToolOutput($"{label}...");
        var result = await _commandRunner.RunAsync(fileName, arguments);
        var output = result.Output;
        var status = result.Ok
            ? string.Format(_localizer.T("CommandDone"), label)
            : string.Format(_localizer.T("CommandFailed"), label, result.ExitCode);
        NetworkLog(status, result.Ok ? UiLogLevel.Information : UiLogLevel.Error);

        if (!string.IsNullOrWhiteSpace(output))
        {
            NetworkLog(output);
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
        await RunExclusiveNetworkActionAsync(async () => { await ApplyDnsSettingsCoreAsync(); return true; });
    }

    private async Task ApplyDnsSettingsCoreAsync()
    {
        if (DnsAdapterComboBox.SelectedItem is not DnsAdapterOption adapter)
        {
            SetDnsOperationMessage("SelectAdapter", false);
            return;
        }

        var preset = GetSelectedDnsPreset();
        if (preset == "none")
        {
            SetDnsOperationMessage("SelectDnsPlan", false);
            return;
        }
        var isAutomatic = preset == "automatic";
        var primary = DnsPrimaryTextBox.Text.Trim();
        var secondary = DnsSecondaryTextBox.Text.Trim();
        if (!isAutomatic)
        {
            if (!IsValidIpv4(primary) || (!string.IsNullOrWhiteSpace(secondary) && !IsValidIpv4(secondary)))
            {
                SetDnsOperationMessage("InvalidDnsAddress", false);
                return;
            }
        }

        var plan = isAutomatic ? _localizer.T("AutomaticDns") : string.Join(", ", new[] { primary, secondary }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!await ConfirmActionAsync(_localizer.T("ConfirmDnsTitle"),
            string.Format(_localizer.T("ConfirmTargetDns"), adapter.Name, plan)))
        {
            SetDnsOperationMessage("ActionCancelled", false);
            return;
        }

        if (!NetworkTargetStillMatches(adapter.Id, adapter.Name))
        {
            SetDnsOperationMessage("NetworkTargetChanged", false);
            return;
        }
        SetDnsOperationMessage("ApplyingDns", false);
        try
        {
            var result = isAutomatic
                ? await _networkConfiguration.SetAutomaticDnsAsync(adapter.Name)
                : await _networkConfiguration.SetStaticDnsAsync(
                    adapter.Name,
                    primary, string.IsNullOrWhiteSpace(secondary) ? null : secondary);

            var refreshed = RefreshDnsInfo();
            var status = SetDnsOperationMessage(result.Success ? "DnsTargetApplied" : result.WasCanceled ? "DnsPartialCanceled" : result.MayHaveChanged ? "DnsMayHaveChanged" : "DnsApplyFailed",
                !refreshed, result.Success ? [adapter.Name] : [result.ExitCode]);
            NetworkLog(status, result.Success ? UiLogLevel.Information : UiLogLevel.Error);
            if (result.Success)
            {
                ShowFeedback(_localizer.T("DnsAppliedTitle"), status, refreshed ? FeedbackSeverity.Success : FeedbackSeverity.Warning);
            }
            else
            {
                ShowFeedback(_localizer.T("ActionFailedTitle"), status, FeedbackSeverity.Danger);
            }

            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                NetworkLog(result.Output);
            }

        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or OperationCanceledException)
        {
            var refreshed = RefreshDnsInfo();
            SetDnsOperationMessage("DnsInterruptedCheckCurrent", !refreshed);
            NetworkLog(ex.Message, UiLogLevel.Error);
            ShowFeedback(_localizer.T("ActionFailedTitle"), DnsStatusText.Text, FeedbackSeverity.Warning);
        }
    }

    private void ApplyDnsPresetToFields()
    {
        var preset = GetSelectedDnsPreset();
        var servers = GetPresetDnsServers(preset);
        var isCustom = preset == "custom";

        DnsPrimaryTextBox.IsEnabled = isCustom;
        DnsSecondaryTextBox.IsEnabled = isCustom;
        UpdateDnsApplyAvailability();
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
            DnsCurrentText.Text = _localizer.T("SelectDnsTarget");
            UpdateDnsApplyAvailability();
            return;
        }
        var row = _dnsRows.FirstOrDefault(item => item.Adapter.Equals(adapter.Name, StringComparison.OrdinalIgnoreCase));
        DnsCurrentText.Text = string.Format(_localizer.T("DnsCurrentAddresses"), row?.Servers ?? "—");
        UpdateDnsApplyAvailability();
    }

    private void UpdateDnsApplyAvailability()
    {
        DnsApplyButton.IsEnabled = !_isApplyingDns && !_isNetworkActionRunning
            && DnsAdapterComboBox.SelectedItem is DnsAdapterOption && GetSelectedDnsPreset() != "none";
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
            ? item.Tag?.ToString() ?? "none"
            : "none";
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

    private void RefreshDnsInfoCore()
    {
        var selectedAdapter = (DnsAdapterComboBox.SelectedItem as DnsAdapterOption)?.Id;
        _isSyncingDnsControls = true;
        try
        {
            _dnsAdapterOptions.Clear();
            _dnsRows.Clear();
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(item => item.OperationalStatus == OperationalStatus.Up)
                .ThenBy(item => item.Name))
            {
                var properties = networkInterface.GetIPProperties();
                var servers = NetworkAdapterPresentation.Addresses(properties.DnsAddresses);

                if (servers == "—" && networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (networkInterface.OperationalStatus == OperationalStatus.Up)
                {
                    _dnsAdapterOptions.Add(new DnsAdapterOption(networkInterface.Id, networkInterface.Name,
                        properties.GatewayAddresses.Any(item => item.Address.AddressFamily == AddressFamily.InterNetwork && !item.Address.Equals(IPAddress.Any))));
                }

                _dnsRows.Add(new DnsServerRow(
                    networkInterface.Name,
                    servers,
                    _localizer.T("DnsOriginUnknown"),
                    LocalizeOperationalStatus(networkInterface.OperationalStatus), networkInterface.OperationalStatus));
            }

            DnsStatusText.Text = _dnsRows.Count == 0
                ? _localizer.T("NoDnsServers")
                : string.Format(_localizer.T("DnsServerCount"), _dnsRows.Count);

            var chosenId = NetworkAdapterSelection.Choose(_dnsAdapterOptions.Select(item =>
                new NetworkAdapterCandidate(item.Id, item.Name, true, item.HasDefaultGateway)), selectedAdapter);
            DnsAdapterComboBox.SelectedItem = _dnsAdapterOptions.FirstOrDefault(item => item.Id == chosenId);
            if (selectedAdapter != chosenId) { SelectDnsPreset("none"); ApplyDnsPresetToFields(); }
        }
        finally { _isSyncingDnsControls = false; }
        FillDnsFieldsFromSelectedAdapter();
        UpdateEmptyStates();
    }

    private void RefreshAdapterInfoCore()
    {
        var previousId = (AdapterTargetComboBox.SelectedItem as NetworkAdapterCandidate)?.Id;
        var dhcpTargets = new List<NetworkAdapterCandidate>();
        _adapterRows.Clear();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(item => item.OperationalStatus == OperationalStatus.Up)
            .ThenBy(item => item.Name))
        {
            var properties = networkInterface.GetIPProperties();
            if (networkInterface.OperationalStatus == OperationalStatus.Up && networkInterface.Supports(NetworkInterfaceComponent.IPv4))
            {
                var ipv4Properties = properties.GetIPv4Properties();
                if (ipv4Properties?.IsDhcpEnabled == true)
                    dhcpTargets.Add(new(networkInterface.Id, networkInterface.Name, true,
                        properties.GatewayAddresses.Any(item => item.Address.AddressFamily == AddressFamily.InterNetwork && !item.Address.Equals(IPAddress.Any))));
            }
            var ipv4 = properties.UnicastAddresses
                .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => item.Address.ToString())
                .ToList();
            var gateways = properties.GatewayAddresses
                .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => item.Address.ToString())
                .ToList();
            var dns = NetworkAdapterPresentation.Addresses(properties.DnsAddresses);

            _adapterRows.Add(new AdapterRow(
                networkInterface.Name,
                NetworkAdapterPresentation.Type(networkInterface.NetworkInterfaceType, _localizer),
                LocalizeOperationalStatus(networkInterface.OperationalStatus),
                ipv4.Count == 0 ? "-" : string.Join(", ", ipv4),
                gateways.Count == 0 ? "-" : string.Join(", ", gateways),
                dns, networkInterface.NetworkInterfaceType, networkInterface.OperationalStatus));
        }

        AdapterStatusText.Text = string.Format(_localizer.T("AdapterCount"), _adapterRows.Count);
        AdapterTargetComboBox.ItemsSource = dhcpTargets;
        var chosenId = NetworkAdapterSelection.Choose(dhcpTargets, previousId);
        AdapterTargetComboBox.SelectedItem = dhcpTargets.FirstOrDefault(item => item.Id == chosenId);
        if (dhcpTargets.Count == 0) AdapterStatusText.Text = _localizer.T("NoDhcpAdapter");
        UpdateEmptyStates();
    }

    private static bool NetworkTargetStillMatches(string id, string name) => NetworkInterface.GetAllNetworkInterfaces()
        .Any(adapter => adapter.Id == id && adapter.Name == name && adapter.OperationalStatus == OperationalStatus.Up);

    private static NetworkAdapterCandidate ReadDhcpCandidate(NetworkInterface adapter)
    {
        var supportsIpv4 = adapter.Supports(NetworkInterfaceComponent.IPv4);
        return new(adapter.Id, adapter.Name, adapter.OperationalStatus == OperationalStatus.Up, false, supportsIpv4,
            supportsIpv4 ? adapter.GetIPProperties().GetIPv4Properties()?.IsDhcpEnabled : null);
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

    private void ResetIpInfoUi()
    {
        IpInfoStatusText.Text = _localizer.T("NotChecked");
    }

    private void ResetReportUi()
    {
        ReportStatusText.Text = _localizer.T("NotChecked");
    }

    private static string FormatLatency(double? latencyMs)
    {
        return latencyMs is { } value && double.IsFinite(value) && value >= 0 ? $"{value:F0} ms" : "-";
    }

    private string LocalizeOperationalStatus(OperationalStatus status)
    {
        return NetworkAdapterPresentation.Status(status, _localizer);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return duration.TotalMinutes >= 1 ? $"{duration.Minutes}m {duration.Seconds}s" : $"{Math.Max(0, duration.Seconds)}s";
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
        _trayMenuHost = new TrayMenuHost(this, _trayMenu);
        _trayMenu.CommandRequested += command => _ = RunUiActionAsync(() => RunTrayCommandAsync(command));
        notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button != Forms.MouseButtons.Right) return;
            Dispatcher.Invoke(() =>
            {
                if (_resourcesCleaned || _isNotifyIconDisposed) return;
                UpdateTrayText();
                _trayMenuHost.ShowAtCursor();
            });
        };
        notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ShowFromTray);
        };
        return notifyIcon;
    }

    private async Task RunTrayCommandAsync(string command)
    {
        if (_resourcesCleaned) return;
        switch (command)
        {
            case "ShowWindow": ShowFromTray(); break;
            case "CheckNow": await RunCheckAsync(); break;
            case "StartMonitor": await StartMonitoringAsync(); break;
            case "Stop": StopMonitoring(); break;
            case "Exit": ExitApplication(); break;
        }
    }

    private void UpdateTrayText(MonitorStatusPresentation? presentation = null)
    {
        if (_notifyIcon is null || _isNotifyIconDisposed)
        {
            return;
        }

        presentation ??= MonitorStatusPresentation.Create(_engine.IsMonitoring, _engine.IsChecking || _isImmediateCheckRunning,
            GuardianStatusStore.Read(GuardianPaths.MachineDirectory), DateTimeOffset.UtcNow, _localizer);
        TrayMonitorPresenter.Apply(_notifyIcon, _trayMenu, presentation, _localizer);
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

    private bool _isExiting;

    private async void ExitApplication()
    {
        if (_isExiting) return;
        _isExiting = true;
        _networkActionCancellation?.Cancel();
        if (_networkActionCompletion is { } actionCompletion) await actionCompletion;
        _engine.Stop();
        await ReleaseMonitorLeaseWhenIdleAsync();
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
            DeferCloseAction(this, e, HideToTray);
            return;
        }

        if (_settings.CloseBehavior == "ask")
        {
            e.Cancel = true;
            if (_isCloseDialogShowing) return;
            _isCloseDialogShowing = true;
            DeferCloseAction(this, e, () => _ = ShowCloseDialogAsync());
            return;
        }

        DeferCloseAction(this, e, ExitApplication);
    }

    private static void DeferCloseAction(Window window, CancelEventArgs e, Action action)
    {
        e.Cancel = true;
        // 等 Closing 返回后再关闭、隐藏或询问，避免窗口状态操作重入。
        _ = window.Dispatcher.BeginInvoke(action);
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
                ExitApplication();
            }
        }
        catch (Exception ex)
        {
            Log(ex.Message, UiLogLevel.Error);
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
        _currentWifiLifetime.Cancel();
        _networkActionCancellation?.Cancel();
        _windowWorkArea.Dispose();
        _engine.Stop();
        _engine.Dispose();
        _ = ReleaseMonitorLeaseWhenIdleAsync();
        _countdownTimer.Stop();
        _sidebarSpeedTimer.Stop();
        CleanupSpeedTest();
        _wifiScan.Cancel();
        _websiteSession.Cancel();
        _ipSession.Cancel();
        _diagnosticSession.Cancel();
        _qualityCancellation?.Cancel();
        _immediateCheckCancellation?.Cancel();
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
            _trayMenuHost?.Dispose();
            _notifyIcon.Icon?.Dispose();
            _notifyIcon.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private sealed record ProbeRow(string Name, string ResultText, string LatencyText, string Detail);
    private sealed record QualityProbeRow(string Name, string ResultText, string LatencyText, string Detail);
    private sealed record WebsiteTestRow(string Name, string Status, string HttpStatus, string DnsLatency, string HttpLatency, string Address, string Detail, string TimeText);
    private sealed record IpInfoRow(string Name, string Value);
    private sealed record DiagnosticReportRow(string Item, string Result, string Advice);
    private sealed record DnsAdapterOption(string Id, string Name, bool HasDefaultGateway);
    private sealed record DnsServerRow(string Adapter, string Servers, string Source, string Status, OperationalStatus LinkState);
    private sealed record AdapterRow(string Name, string Type, string Status, string Ipv4, string Gateway, string DnsServers,
        NetworkInterfaceType InterfaceType, OperationalStatus LinkState);

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
