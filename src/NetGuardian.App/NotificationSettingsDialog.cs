using System;
using System.Windows;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Notifications;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace NetGuardian.App;

/// <summary>远程告警渠道、触发事件与 Webhook 测试配置弹窗。</summary>
public sealed class NotificationSettingsDialog : FluentDialogWindow
{
    private readonly Localizer _localizer;
    private readonly AlertNotificationService _notificationService;
    private readonly string? _currentSsid;

    public CheckBox EnableCheckBox { get; }
    public ComboBox ChannelComboBox { get; }
    public TextBox WebhookUrlTextBox { get; }
    public CheckBox NotifyOfflineCheckBox { get; }
    public CheckBox NotifyRecoveredCheckBox { get; }
    public CheckBox NotifyFailedCheckBox { get; }
    public CheckBox NotifyHighRiskCheckBox { get; }
    public Button TestSendButton { get; }
    public TextBlock TestResultText { get; }
    public Button SaveButton { get; }
    public Button CancelButton { get; }

    public NotificationSettings ResultSettings { get; private set; }

    public NotificationSettingsDialog(
        NotificationSettings initial,
        Localizer localizer,
        AlertNotificationService notificationService,
        string? currentSsid = null)
    {
        _localizer = localizer;
        _notificationService = notificationService;
        _currentSsid = currentSsid;

        var copy = Clone(initial);
        copy.Normalize();
        ResultSettings = copy;

        Title = _localizer.T("NotificationDialogTitle");
        Width = 540;

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        EnableCheckBox = new CheckBox
        {
            Content = _localizer.T("NotificationEnableCheck"),
            IsChecked = copy.Enabled,
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(EnableCheckBox);

        panel.Children.Add(new TextBlock
        {
            Text = _localizer.T("NotificationChannelLabel"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        ChannelComboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        ChannelComboBox.Items.Add(new ComboBoxItem { Tag = NotificationChannelType.GenericWebhook, Content = _localizer.T("ChannelGenericWebhook") });
        ChannelComboBox.Items.Add(new ComboBoxItem { Tag = NotificationChannelType.WeCom, Content = _localizer.T("ChannelWeCom") });
        ChannelComboBox.Items.Add(new ComboBoxItem { Tag = NotificationChannelType.Feishu, Content = _localizer.T("ChannelFeishu") });
        ChannelComboBox.Items.Add(new ComboBoxItem { Tag = NotificationChannelType.DingTalk, Content = _localizer.T("ChannelDingTalk") });
        SelectChannel(copy.Channel);
        panel.Children.Add(ChannelComboBox);

        panel.Children.Add(new TextBlock
        {
            Text = _localizer.T("WebhookUrlLabel"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        WebhookUrlTextBox = new TextBox
        {
            Text = copy.WebhookUrl,
            PlaceholderText = "https://...",
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(WebhookUrlTextBox);

        panel.Children.Add(new TextBlock
        {
            Text = _localizer.T("NotifyEventsLabel"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 6)
        });
        NotifyOfflineCheckBox = new CheckBox { Content = _localizer.T("NotifyOnOfflineCheck"), IsChecked = copy.NotifyOnOffline, Margin = new Thickness(0, 0, 0, 4) };
        NotifyRecoveredCheckBox = new CheckBox { Content = _localizer.T("NotifyOnRecoveredCheck"), IsChecked = copy.NotifyOnRecovered, Margin = new Thickness(0, 0, 0, 4) };
        NotifyFailedCheckBox = new CheckBox { Content = _localizer.T("NotifyOnFailedCheck"), IsChecked = copy.NotifyOnRecoveryFailed, Margin = new Thickness(0, 0, 0, 4) };
        NotifyHighRiskCheckBox = new CheckBox { Content = _localizer.T("NotifyOnHighRiskCheck"), IsChecked = copy.NotifyOnHighRisk, Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(NotifyOfflineCheckBox);
        panel.Children.Add(NotifyRecoveredCheckBox);
        panel.Children.Add(NotifyFailedCheckBox);
        panel.Children.Add(NotifyHighRiskCheckBox);

        var pendingCount = _notificationService.GetPendingSnapshot().Count;
        var queueHint = new TextBlock
        {
            Text = string.Format(_localizer.T("PendingQueueHint"), copy.MaxQueuedItems, pendingCount),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8)
        };
        queueHint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(queueHint);

        TestResultText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 4)
        };
        TestResultText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(TestResultText);

        Body.Content = panel;

        TestSendButton = new Button
        {
            Content = _localizer.T("TestNotificationButton"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        TestSendButton.Click += TestSend_Click;

        SaveButton = new Button
        {
            Content = _localizer.T("SaveButtonText"),
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        SaveButton.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        SaveButton.Click += Save_Click;

        CancelButton = new Button
        {
            Content = _localizer.T("CancelButtonText"),
            IsCancel = true
        };
        CancelButton.Click += (_, _) => DialogResult = false;

        Actions.Children.Add(TestSendButton);
        Actions.Children.Add(SaveButton);
        Actions.Children.Add(CancelButton);
    }

    private NotificationSettings BuildCandidate()
    {
        var channel = (ChannelComboBox.SelectedItem as ComboBoxItem)?.Tag is NotificationChannelType t
            ? t
            : NotificationChannelType.GenericWebhook;
        var candidate = new NotificationSettings
        {
            Enabled = EnableCheckBox.IsChecked == true,
            Channel = channel,
            WebhookUrl = WebhookUrlTextBox.Text?.Trim() ?? string.Empty,
            NotifyOnOffline = NotifyOfflineCheckBox.IsChecked == true,
            NotifyOnRecovered = NotifyRecoveredCheckBox.IsChecked == true,
            NotifyOnRecoveryFailed = NotifyFailedCheckBox.IsChecked == true,
            NotifyOnHighRisk = NotifyHighRiskCheckBox.IsChecked == true,
            TimeoutSeconds = ResultSettings.TimeoutSeconds,
            MaxQueuedItems = ResultSettings.MaxQueuedItems
        };
        candidate.Normalize();
        return candidate;
    }

    private async void TestSend_Click(object sender, RoutedEventArgs e)
    {
        TestSendButton.IsEnabled = false;
        TestResultText.Text = _localizer.T("TestNotificationSending");
        try
        {
            var candidate = BuildCandidate();
            var result = await _notificationService.SendTestAsync(candidate, _currentSsid);
            TestResultText.Text = result.Succeeded
                ? _localizer.T("TestNotificationSucceeded")
                : string.Format(_localizer.T("TestNotificationFailed"), result.ErrorMessage ?? "Unknown");
        }
        finally
        {
            TestSendButton.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultSettings = BuildCandidate();
        DialogResult = true;
    }

    private void SelectChannel(NotificationChannelType channel)
    {
        foreach (ComboBoxItem item in ChannelComboBox.Items)
        {
            if (item.Tag is NotificationChannelType t && t == channel)
            {
                ChannelComboBox.SelectedItem = item;
                return;
            }
        }
        if (ChannelComboBox.Items.Count > 0)
        {
            ChannelComboBox.SelectedIndex = 0;
        }
    }

    private static NotificationSettings Clone(NotificationSettings source) => new()
    {
        Enabled = source.Enabled,
        Channel = source.Channel,
        WebhookUrl = source.WebhookUrl,
        NotifyOnOffline = source.NotifyOnOffline,
        NotifyOnRecovered = source.NotifyOnRecovered,
        NotifyOnRecoveryFailed = source.NotifyOnRecoveryFailed,
        NotifyOnHighRisk = source.NotifyOnHighRisk,
        TimeoutSeconds = source.TimeoutSeconds,
        MaxQueuedItems = source.MaxQueuedItems
    };
}
