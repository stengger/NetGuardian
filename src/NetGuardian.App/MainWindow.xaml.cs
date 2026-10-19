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
    private bool _forceExit;
    private bool _isSyncingDnsControls;
    private bool _isApplyingDns;
    private bool _isSyncingWifiSelection;
    private bool _isSyncingScheduledStartup;
    private bool _isSyncingHighRiskToggle;
    private bool _isFirstRun;
    private bool _isNotifyIconDisposed;
    private bool _resourcesCleaned;
    private int _logLineCount;
    private DateTimeOffset? _lastSidebarSpeedSampleAt;
    private long? _lastSidebarReceivedBytes;
    private long? _lastSidebarSentBytes;
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
        _wifi = wifi;
        _connectivityTester = connectivityTester;
        _commandRunner = commandRunner;
        _networkConfiguration = networkConfiguration;
        _websiteTester = websiteTester;
        _publicIpInfo = publicIpInfo;
        _speedTester = speedTester;

        InitializeComponent();
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
                () => TimeSpan.FromMinutes(_settings.Policy.HighRiskCooldownMinutes)))
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

        _snackbar.SetSnackbarPresenter(RootSnackbar);

        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (_, _) => UpdateMonitorStatus();
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
        ShowPanel(StatusPanel);
        UpdateMonitorStatus();
        UpdateSidebarSpeed();
        UpdateWeeklySummary();
        UpdateFirstRunGuide();
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

    private void StopMonitor_Click(object sender, RoutedEventArgs e)
    {
        StopMonitoring();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        using (SetBusy((System.Windows.Controls.Primitives.ButtonBase)sender))
        {
            await RunUiActionAsync(ScanWifiAsync);
        }
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
        if (ProfileGrid.SelectedItem is not WifiProfile profile)
        {
            SetWifiSelectionMessage(_localizer.T("SelectProfileFirst"));
            return;
        }

        await RunUiActionAsync(async () =>
        {
            await RunExclusiveNetworkActionAsync(async () =>
            {
                Log(string.Format(_localizer.T("ConnectingTo"), profile.Ssid));
                var ok = await _wifi.ConnectAsync(profile.Ssid);
                Log(string.Format(_localizer.T(ok ? "ConnectCommandSent" : "ConnectCommandFailed"), profile.Ssid));
                return ok;
            });
        });
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
            AdapterStatusText.Text = _localizer.T("SelectDhcpAdapter");
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
                    AdapterStatusText.Text = _localizer.T("ActionCancelled");
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
                            AdapterStatusText.Text = _localizer.T("NetworkTargetChanged");
                            return false;
                        }
                        AdapterStatusText.Text = string.Format(_localizer.T("DhcpRenewingTarget"), target.Name);
                        commandStarted = true;
                        var result = await _networkConfiguration.RenewDhcpAsync(target.Name);
                        var refreshed = TryRefreshNetworkConfiguration(RefreshAdapterInfo);
                        var status = string.Format(_localizer.T(result.Success ? "DhcpTargetDone" : "DhcpTargetFailed"), target.Name, result.ExitCode);
                        if (!refreshed) status += " " + _localizer.T("NetworkReadFailed");
                        AdapterStatusText.Text = status;
                        Log(status);
                        if (!string.IsNullOrWhiteSpace(result.Output)) Log(result.Output);
                        ShowFeedback(_localizer.T(result.Success ? "CommandCompletedTitle" : "ActionFailedTitle"), status,
                            result.Success && refreshed ? FeedbackSeverity.Success : FeedbackSeverity.Warning);
                        return result.Success;
                    }
                    catch (Exception ex) when (ex is NetworkInformationException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException or OperationCanceledException)
                    {
                        var refreshed = TryRefreshNetworkConfiguration(RefreshAdapterInfo);
                        var status = string.Format(_localizer.T(commandStarted ? "DhcpInterruptedCheckCurrent" : "NetworkTargetChanged"), target.Name);
                        if (!refreshed) status += " " + _localizer.T("NetworkReadFailed");
                        AdapterStatusText.Text = status;
                        Log($"{target.Name}: {ex.Message}");
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
    }

    private void AdapterRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAdapterInfo();
        SetToolOutput(_localizer.T("AdapterRefreshed"));
    }

    private async void QualityRun_Click(object sender, RoutedEventArgs e)
    {
        using (SetBusy((System.Windows.Controls.Primitives.ButtonBase)sender))
        {
            await RunUiActionAsync(RunQualityCheckAsync);
        }
    }

    private async void WebsiteRun_Click(object sender, RoutedEventArgs e)
    {
        using (SetBusy((System.Windows.Controls.Primitives.ButtonBase)sender))
        {
            await RunUiActionAsync(() => RunWebsiteTestAsync(_localizer.T("CustomWebsite"), WebsiteUrlTextBox.Text));
        }
    }

    private async void WebsitePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string url)
        {
            WebsiteUrlTextBox.Text = url;
            using (SetBusy(button))
            {
                await RunUiActionAsync(() => RunWebsiteTestAsync(button.Content?.ToString() ?? url, url));
            }
        }
    }

    private async void IpInfoRefresh_Click(object sender, RoutedEventArgs e)
    {
        using (SetBusy((System.Windows.Controls.Primitives.ButtonBase)sender))
        {
            await RunUiActionAsync(RefreshPublicIpInfoAsync);
        }
    }

    private async void ReportRun_Click(object sender, RoutedEventArgs e)
    {
        using (SetBusy((System.Windows.Controls.Primitives.ButtonBase)sender))
        {
            await RunUiActionAsync(RunDiagnosticReportAsync);
        }
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
        _engine.Start();
        await RunCheckAsync();
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
        try
        {
            if (_engine.IsMonitoring)
            {
                await _engine.CheckNowAsync();
            }
            else
            {
                // 手动检测只读，不能绕过后台控制锁自动启用网卡。
                var diagnostics = await _wifi.GetAdapterDiagnosticsAsync();
                var ssid = await _wifi.GetCurrentSsidAsync();
                ApplyReport(await _connectivityTester.RunFullCheckAsync(_settings.Policy, ssid, diagnostics));
            }
        }
        catch (Exception ex)
        {
            Log(ex.Message);
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
            Log($"{e.Report.CheckedAt:yyyy-MM-dd HH:mm:ss}  {e.Report.State}: {e.Report.Summary}");
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
                Log(_localizer.T("MonitorStartedLog"));
                break;
            case EngineEventKind.MonitorStopped:
                Log(_localizer.T("MonitorStoppedLog"));
                break;
            case EngineEventKind.TryingEnableWifiAdapter:
                Log(string.Format(_localizer.T("TryingEnableWifiAdapter"), args[0]));
                break;
            case EngineEventKind.WifiAdapterEnabled:
                Log(_localizer.T("WifiAdapterEnabled"));
                break;
            case EngineEventKind.WifiAdapterEnableFailed:
                Log(_localizer.T("WifiAdapterEnableFailed"));
                break;
            case EngineEventKind.CheckError:
                Log(args[0]?.ToString() ?? _localizer.T("CheckFailedLog"));
                break;
            case EngineEventKind.RecoveryCooldownActive:
                Log(string.Format(_localizer.T("RecoveryCooldownActive"), FormatDuration((TimeSpan)args[0]!)));
                break;
            case EngineEventKind.RecoveryStarted:
                _journal.RecordRecoveryStarted(args[0] is int streak ? streak : 0);
                Log(string.Format(_localizer.T("RecoveryStarted"), args[0]));
                break;
            case EngineEventKind.RecoveryDnsFlush:
                Log(_localizer.T("RecoveryDnsFlush"));
                break;
            case EngineEventKind.RecoveryDnsFlushCompleted:
            {
                var label = _localizer.T("FlushDns");
                Log(args[0] is true
                    ? string.Format(_localizer.T("CommandDone"), label)
                    : string.Format(_localizer.T("CommandFailed"), label, args[1]));
                if (args[2] is string output && !string.IsNullOrWhiteSpace(output))
                {
                    Log(output);
                }

                break;
            }
            case EngineEventKind.RecoveryReconnectCurrent:
                Log(string.Format(_localizer.T("RecoveryReconnectCurrent"), args[0]));
                break;
            case EngineEventKind.RecoverySwitchWifi:
                Log(string.Format(_localizer.T("RecoverySwitchWifi"), args[0]));
                break;
            case EngineEventKind.RecoveryWifiConnected:
            {
                _journal.RecordRecoveryResult(true, args[0]?.ToString());
                var connectedMessage = string.Format(_localizer.T("RecoveryWifiConnected"), args[0]);
                Log(connectedMessage);
                if (!IsVisible || WindowState == WindowState.Minimized)
                {
                    ShowTrayBalloon(connectedMessage, Forms.ToolTipIcon.Info);
                }

                SaveSettings();
                UpdateWeeklySummary();
                break;
            }
            case EngineEventKind.RecoveryNoActionsEnabled:
                Log(_localizer.T("RecoveryNoActionsEnabled"));
                break;
            case EngineEventKind.RecoveryFailed:
            {
                _journal.RecordRecoveryResult(false, null);
                SaveSettings();
                var failedMessage = _localizer.T("RecoveryFailed");
                Log(failedMessage);
                if (!IsVisible || WindowState == WindowState.Minimized)
                {
                    ShowTrayBalloon(failedMessage, Forms.ToolTipIcon.Error);
                }

                UpdateWeeklySummary();
                break;
            }
            case EngineEventKind.HighRiskCooldownActive:
                Log(string.Format(_localizer.T("HighRiskCooldownActive"), FormatDuration((TimeSpan)args[0]!)));
                break;
            case EngineEventKind.HighRiskActionStarted:
            {
                var message = string.Format(_localizer.T("HighRiskActionStarted"), args[0]);
                Log(message);
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
                Log(message);
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
            WeeklySummaryText.Text = string.Format(
                _localizer.T("WeeklySummary"),
                summary.OutageEpisodes,
                summary.RecoverySuccesses,
                summary.RecoveryAttempts);
        }
        catch (Exception ex)
        {
            WeeklySummaryText.Text = ex.Message;
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
        profile.Security = dialog.Authentication;
        profile.CredentialSaved = true;
        ProfileGrid.Items.Refresh();
        SaveSettings();
        RefreshNearbyPriorityMarkers();
        UpdateWifiSelectionUi();
    }

    private Task<bool> ValidateAndSaveWifiProfileAsync(string ssid, string? password, string authentication) =>
        RunExclusiveNetworkActionAsync(() => ValidateAndSaveWifiProfileCoreAsync(ssid, password, authentication));

    private async Task<bool> RunExclusiveNetworkActionAsync(Func<Task<bool>> action)
    {
        var resume = _engine.IsMonitoring;
        if (resume)
        {
            _engine.Stop();
            await ReleaseMonitorLeaseWhenIdleAsync();
        }
        try
        {
            using var lease = MonitoringLease.TryAcquire(GuardianPaths.MachineDirectory);
            if (lease is null)
            {
                ShowFeedback("后台正在守护", "为避免并发切网，请先在维护窗口停止后台任务，再执行手动网络修改。配置策略仍可正常保存。", FeedbackSeverity.Warning);
                return false;
            }
            return await action();
        }
        finally { if (resume && !_resourcesCleaned && !_isExiting) await StartMonitoringAsync(); }
    }

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
        WifiSelectionBar.Background = hasSelection
            ? ThemeBrush("RowSelectedBrush")
            : ThemeBrush("ControlFillColorDefaultBrush");
        WifiSelectionBar.BorderBrush = hasSelection
            ? ThemeBrush("StatusHealthyBrush")
            : ThemeBrush("CardStrokeColorDefaultBrush");
    }

    // Resolves a brush from the merged theme dictionaries at call time so that
    // colors re-resolve against the active palette after a light/dark switch.
    private Brush ThemeBrush(string resourceKey)
    {
        return TryFindResource(resourceKey) as Brush
            ?? Application.Current.TryFindResource(resourceKey) as Brush
            ?? Brushes.Gray;
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

    private void ApplyReport(ConnectivityReport report)
    {
        HealthStateText.Text = StateText(report.State);
        HealthStateText.Foreground = report.State switch
        {
            NetworkState.Healthy => ThemeBrush("StatusHealthyBrush"),
            NetworkState.Recovering => ThemeBrush("StatusHealthyBrush"),
            NetworkState.GatewayIssue or NetworkState.DnsIssue or NetworkState.InternetIssue => ThemeBrush("StatusWarningBrush"),
            _ => ThemeBrush("StatusDangerBrush")
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
        ExportLogsButton.Content = _localizer.T("ExportLogs");
        SettingsNavButton.Content = _localizer.T("Settings");
        CheckNowButton.Content = _localizer.T("CheckNow");
        StartMonitorButton.Content = _engine.IsMonitoring ? _localizer.T("Monitoring") : _localizer.T("StartMonitor");
        StopButton.Content = _localizer.T("Stop");
        HealthStateLabel.Text = _localizer.T("HealthState");
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
        DnsServersColumn.Header = _localizer.T("DnsServers");
        DnsSourceColumn.Header = _localizer.T("Source");
        DnsStatusColumn.Header = _localizer.T("StatusColumn");
        foreach (ComboBoxItem item in DnsPresetComboBox.Items)
            item.Content = _localizer.T(item.Tag?.ToString() switch
            {
                "automatic" => "AutomaticDns", "ali" => "AliDns", "tencent" => "TencentDns",
                "cloudflare" => "CloudflareDns", "google" => "GoogleDns", "custom" => "CustomDns", _ => "SelectDnsPlan"
            });
        FillDnsFieldsFromSelectedAdapter();
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
        RefreshSpeedView();
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
        var background = GuardianStatusStore.Read(GuardianPaths.MachineDirectory);
        var presentation = MonitorStatusPresentation.Create(_engine.IsMonitoring, _engine.IsChecking, background, DateTimeOffset.UtcNow, _localizer);
        MonitorStateText.Text = presentation.OwnerText;
        HeaderStateText.Text = presentation.ActivityText;
        System.Windows.Automation.AutomationProperties.SetName(StatusDot, presentation.ActivityText);
        StatusDot.Fill = ThemeBrush(presentation.BrushKey);
        HeaderStateText.ToolTip = presentation.Hint;
        StartMonitorButton.ToolTip = presentation.Hint;
        StopButton.ToolTip = presentation.Hint;
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(StartMonitorButton, true);
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(StopButton, true);
        StartMonitorButton.Content = _localizer.T(_engine.IsMonitoring ? "Monitoring" : "StartMonitor");
        StartMonitorButton.IsEnabled = presentation.CanStart;
        StopButton.IsEnabled = presentation.CanStop;
        if (presentation.HasBackground && background is not null)
        {
            if (background.Report is { } report) ApplyReport(report);
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

        UpdateTrayText();
        UpdateFirstRunGuide();
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
        SidebarCompactDownloadText.Text = FormatRateCompact(downloadBytesPerSecond);
        SidebarCompactUploadText.Text = FormatRateCompact(uploadBytesPerSecond);
        SidebarSpeedCompact.ToolTip = $"\u2191 {FormatRate(uploadBytesPerSecond)}   \u2193 {FormatRate(downloadBytesPerSecond)}";
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

    private static string FormatRate(double bytesPerSecond)
    {
        var (value, unit) = FormatRateParts(bytesPerSecond);
        return $"{value} {unit}";
    }

    private static string FormatRateCompact(double bytesPerSecond)
    {
        var (value, unit) = FormatRateParts(bytesPerSecond);
        return value + unit switch
        {
            "KB/s" => "K",
            "MB/s" => "M",
            "GB/s" => "G",
            _ => "B"
        };
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
        Log($"{label}...");
        SetToolOutput($"{label}...");
        var result = await _commandRunner.RunAsync(fileName, arguments);
        var output = result.Output;
        var status = result.Ok
            ? string.Format(_localizer.T("CommandDone"), label)
            : string.Format(_localizer.T("CommandFailed"), label, result.ExitCode);
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
        await RunExclusiveNetworkActionAsync(async () => { await ApplyDnsSettingsCoreAsync(); return true; });
    }

    private async Task ApplyDnsSettingsCoreAsync()
    {
        if (DnsAdapterComboBox.SelectedItem is not DnsAdapterOption adapter)
        {
            DnsStatusText.Text = _localizer.T("SelectAdapter");
            return;
        }

        var preset = GetSelectedDnsPreset();
        if (preset == "none")
        {
            DnsStatusText.Text = _localizer.T("SelectDnsPlan");
            return;
        }
        var isAutomatic = preset == "automatic";
        var primary = DnsPrimaryTextBox.Text.Trim();
        var secondary = DnsSecondaryTextBox.Text.Trim();
        if (!isAutomatic)
        {
            if (!IsValidIpv4(primary) || (!string.IsNullOrWhiteSpace(secondary) && !IsValidIpv4(secondary)))
            {
                DnsStatusText.Text = _localizer.T("InvalidDnsAddress");
                return;
            }
        }

        var plan = isAutomatic ? _localizer.T("AutomaticDns") : string.Join(", ", new[] { primary, secondary }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!await ConfirmActionAsync(_localizer.T("ConfirmDnsTitle"),
            string.Format(_localizer.T("ConfirmTargetDns"), adapter.Name, plan)))
        {
            DnsStatusText.Text = _localizer.T("ActionCancelled");
            return;
        }

        if (!NetworkTargetStillMatches(adapter.Id, adapter.Name))
        {
            DnsStatusText.Text = _localizer.T("NetworkTargetChanged");
            return;
        }
        DnsStatusText.Text = _localizer.T("ApplyingDns");
        try
        {
            var result = isAutomatic
                ? await _networkConfiguration.SetAutomaticDnsAsync(adapter.Name)
                : await _networkConfiguration.SetStaticDnsAsync(
                    adapter.Name,
                    primary, string.IsNullOrWhiteSpace(secondary) ? null : secondary);

            var refreshed = TryRefreshNetworkConfiguration(RefreshDnsInfo);
            var status = result.Success ? string.Format(_localizer.T("DnsTargetApplied"), adapter.Name)
                : string.Format(_localizer.T(result.WasCanceled ? "DnsPartialCanceled" : result.MayHaveChanged ? "DnsMayHaveChanged" : "DnsApplyFailed"), result.ExitCode);
            if (!refreshed) status += " " + _localizer.T("NetworkReadFailed");
            DnsStatusText.Text = status;
            Log(status);
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
                Log(result.Output);
            }

        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or OperationCanceledException)
        {
            TryRefreshNetworkConfiguration(RefreshDnsInfo);
            DnsStatusText.Text = _localizer.T("DnsInterruptedCheckCurrent");
            Log(ex.Message);
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
        DnsApplyButton.IsEnabled = !_isApplyingDns && DnsAdapterComboBox.SelectedItem is DnsAdapterOption && GetSelectedDnsPreset() != "none";
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

    private void RefreshDnsInfo()
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
                    _dnsAdapterOptions.Add(new DnsAdapterOption(networkInterface.Id, networkInterface.Name,
                        properties.GatewayAddresses.Any(item => item.Address.AddressFamily == AddressFamily.InterNetwork && !item.Address.Equals(IPAddress.Any))));
                }

                _dnsRows.Add(new DnsServerRow(
                    networkInterface.Name,
                    servers.Count == 0 ? "-" : string.Join(", ", servers),
                    _localizer.T("DnsOriginUnknown"),
                    LocalizeOperationalStatus(networkInterface.OperationalStatus)));
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

    private void RefreshAdapterInfo()
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

    private bool TryRefreshNetworkConfiguration(Action refresh)
    {
        try { refresh(); return true; }
        catch (Exception ex) when (ex is NetworkInformationException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log($"{_localizer.T("NetworkReadFailed")} {ex.Message}");
            return false;
        }
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
            >= 85 => ThemeBrush("StatusHealthyBrush"),
            >= 60 => ThemeBrush("StatusWarningBrush"),
            _ => ThemeBrush("StatusDangerBrush")
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
            AddReportRow("FailureStreak", _engine.FailureStreak.ToString(), _engine.FailureStreak > 0 ? _localizer.T("WatchNextCheck") : _localizer.T("NoActionNeeded"));

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
        return latencyMs.HasValue ? $"{latencyMs.Value:F0} ms" : "-";
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
        _fileLog.Write(message);
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

        var state = _engine.IsMonitoring ? _localizer.T("Monitoring") : _localizer.T("Stopped");
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

    private bool _isExiting;

    private async void ExitApplication()
    {
        if (_isExiting) return;
        _isExiting = true;
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
        _engine.Stop();
        _engine.Dispose();
        _ = ReleaseMonitorLeaseWhenIdleAsync();
        _countdownTimer.Stop();
        _sidebarSpeedTimer.Stop();
        CleanupSpeedTest();
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
    private sealed record DnsAdapterOption(string Id, string Name, bool HasDefaultGateway);
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
