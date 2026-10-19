using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Network;

namespace NetGuardian.App.Views;

public partial class SpeedTestView : UserControl
{
    public event EventHandler? RunRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? CopyRequested;
    public string ResultSummary { get; private set; } = "";
    private int _displayedRunId = -1;

    public SpeedTestView() => InitializeComponent();

    public void Update(SpeedTestSession session, Localizer localizer, TimeSpan elapsed, bool cancelling = false)
    {
        string T(string key) => localizer.T(key);
        TitleText.Text = T("SpeedPageTitle");
        IntroText.Text = T("SpeedPageIntro");
        DownloadLabel.Text = "↓  " + T("DownloadSpeed");
        UploadLabel.Text = "↑  " + T("UploadSpeed");
        ResponseLabel.Text = T("SpeedResponse");
        DownloadHint.Text = T("SpeedDownloadHint");
        UploadHint.Text = T("SpeedUploadHint");
        ResponseHint.Text = T("SpeedResponseHint");
        SetMetric(DownloadValue, DownloadState, session.Download, "F1", T);
        SetMetric(UploadValue, UploadState, session.Upload, "F1", T);
        SetMetric(ResponseValue, ResponseState, session.Latency, "F0", T);
        ResponseStepLabel.Text = "01  " + T("SpeedPhaseLatency");
        DownloadStepLabel.Text = "02  " + T("DownloadSpeed");
        UploadStepLabel.Text = "03  " + T("UploadSpeed");
        ResponseStepState.Text = T("SpeedMetric" + session.Latency.State);
        DownloadStepState.Text = T("SpeedMetric" + session.Download.State);
        UploadStepState.Text = T("SpeedMetric" + session.Upload.State);
        RunButton.Content = T(session.Status == SpeedTestStatus.Idle ? "RunSpeedTest" : "SpeedRunAgain");
        RunButton.IsEnabled = !session.IsRunning;
        CancelButton.Content = T(cancelling ? "SpeedCancelling" : "Cancel");
        CancelButton.IsEnabled = !cancelling;
        CancelButton.Visibility = session.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        RunningIndicator.Visibility = session.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(RunningIndicator, T("SpeedTesting"));
        var status = cancelling ? T("SpeedCancelling") : session.IsRunning
            ? T("SpeedPhase" + session.Phase)
            : T("SpeedStatus" + session.Status);
        var hint = session.NetworkChanged ? T("SpeedNetworkChanged")
            : session.IsRunning ? T("SpeedHint" + session.Phase)
            : session.Status == SpeedTestStatus.Partial && session.FailureDetail is null ? T("SpeedResponseMissing")
            : session.Status == SpeedTestStatus.Partial ? string.Format(T("SpeedPartialHint"), T("SpeedPhase" + session.Phase))
            : T("SpeedHint" + session.Status);
        var statusChanged = StatusText.Text != status || StatusHint.Text != hint;
        StatusText.Text = status;
        StatusHint.Text = hint;
        AutomationProperties.SetName(StatusText, $"{status}。{hint}");
        if (statusChanged) NotifyLiveRegion(StatusText);
        ElapsedText.Text = session.StartedAt.HasValue ? string.Format(T("SpeedElapsed"), (int)elapsed.TotalSeconds) : "";
        CheckedAtText.Visibility = session.FinishedAt.HasValue ? Visibility.Visible : Visibility.Collapsed;
        CheckedAtText.Text = session.FinishedAt.HasValue
            ? string.Format(T("SpeedCheckedAt"), session.FinishedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")) : "";
        FailureExpander.Header = T("SpeedFailureDetail");
        FailureExpander.Visibility = session.FailureDetail is null ? Visibility.Collapsed : Visibility.Visible;
        FailureText.Text = session.FailureDetail ?? "";
        MethodTitle.Text = T("SpeedMethodTitle");
        TrafficText.Text = T("SpeedTraffic");
        TargetText.Text = T("SpeedTargets");
        MethodText.Text = T("SpeedMethodSummary");
        MethodExpander.Header = T("SpeedMethodMore");
        MethodDetail.Text = T("SpeedMethodDetail");
        CopyButton.Content = T("SpeedCopy");
        CopyButton.IsEnabled = session.CanCopy;
        if (_displayedRunId != session.RunId)
        {
            CopyFeedback.Text = "";
            FailureExpander.IsExpanded = false;
            _displayedRunId = session.RunId;
        }
        var responseSamples = string.Format(T("SpeedSamples"), session.LatencySampleCount);
        ResultSummary = $"NetGuardian · {T("SpeedPageTitle")}\n{StatusText.Text}\n{StatusHint.Text}\n"
            + $"{T("DownloadSpeed")}: {SummaryMetric(session.Download, "F1", "Mbps", T)}\n"
            + $"{T("UploadSpeed")}: {SummaryMetric(session.Upload, "F1", "Mbps", T)}\n"
            + $"{T("SpeedResponse")}: {SummaryMetric(session.Latency, "F0", "ms", T)} ({responseSamples})\n"
            + $"{CheckedAtText.Text}\n{TargetText.Text}\n{MethodText.Text}\n{MethodDetail.Text}";
        AutomationProperties.SetName(DownloadValue, $"{T("DownloadSpeed")} {SummaryMetric(session.Download, "F1", "Mbps", T)}");
        AutomationProperties.SetName(UploadValue, $"{T("UploadSpeed")} {SummaryMetric(session.Upload, "F1", "Mbps", T)}");
        AutomationProperties.SetName(ResponseValue, $"{T("SpeedResponse")} {SummaryMetric(session.Latency, "F0", "ms", T)}");
        ResponseState.ToolTip = responseSamples;
    }

    public void ShowCopyFeedback(string message)
    {
        CopyFeedback.Text = message;
        NotifyLiveRegion(CopyFeedback);
    }

    private static void NotifyLiveRegion(TextBlock text)
    {
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            UIElementAutomationPeer.CreatePeerForElement(text)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static void SetMetric(TextBlock value, TextBlock state, SpeedTestMetric metric, string format, Func<string, string> t)
    {
        value.Text = metric.Value?.ToString(format, CultureInfo.CurrentCulture) ?? "—";
        state.Text = t("SpeedMetric" + metric.State);
    }

    private static string SummaryMetric(SpeedTestMetric metric, string format, string unit, Func<string, string> t) =>
        metric.State == SpeedMetricState.Complete && metric.Value.HasValue
            ? $"{metric.Value.Value.ToString(format, CultureInfo.CurrentCulture)} {unit}" : t("SpeedMetric" + metric.State);

    private void ContentPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 680;
        var cards = new[] { DownloadCard, UploadCard, ResponseCard };
        for (var index = 0; index < cards.Length; index++)
        {
            Grid.SetColumn(cards[index], stacked ? 0 : index);
            Grid.SetRow(cards[index], stacked ? index : 0);
            Grid.SetColumnSpan(cards[index], stacked ? 3 : 1);
            cards[index].Margin = stacked
                ? new Thickness(0, 0, 0, index == 2 ? 0 : 10)
                : new Thickness(0, 0, index == 2 ? 0 : 10, 0);
        }
    }

    private void Run_Click(object sender, RoutedEventArgs e) => RunRequested?.Invoke(this, EventArgs.Empty);
    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);
}
