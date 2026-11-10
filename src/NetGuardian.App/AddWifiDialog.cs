using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;

namespace NetGuardian.App;

public sealed class AddWifiDialog : FluentDialogWindow
{
    private readonly TextBox _ssidBox = new() { Name = "SsidInput" };
    private readonly ComboBox _authBox = new() { Name = "AuthenticationInput", IsEditable = true, IsTextSearchEnabled = false };
    private readonly Wpf.Ui.Controls.PasswordBox _passwordBox = new() { Name = "PasswordInput", RevealButtonEnabled = true };
    private readonly CheckBox _saveToWindowsBox = new();
    private readonly TextBlock _errorText = new();

    public AddWifiDialog(Localizer localizer, WifiNetwork? network, string? initialPassword = null,
        IEnumerable<string>? authenticationOptions = null)
    {
        Title = localizer.T("AddWifi");
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(localizer.Language);
        var form = new Grid();
        for (var row = 0; row < 6; row++)
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Body.Content = form;

        var title = new TextBlock { Text = localizer.T("AddWifi"), FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(FontSizeProperty, "FontSubtitle");
        title.SetResourceReference(MarginProperty, "SpacingSectionGap");
        form.Children.Add(title);
        var ssidPanel = AddLabeledControl(form, 1, localizer.T("Ssid"), _ssidBox);
        AddLabeledControl(form, 2, localizer.T("Authentication"), _authBox);
        AddLabeledControl(form, 3, localizer.T("Password"), _passwordBox);
        _ssidBox.SetResourceReference(StyleProperty, "DialogTextBoxStyle");
        _authBox.SetResourceReference(StyleProperty, "DialogComboBoxStyle");
        _passwordBox.SetResourceReference(StyleProperty, "DialogPasswordBoxStyle");
        _ssidBox.Text = network?.Ssid ?? "";
        // 只提供已有观测值供选择；未知字符串仍原样编辑、返回，不新增协议映射。
        _authBox.ItemsSource = (authenticationOptions ?? Array.Empty<string>()).Append(network?.Authentication ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        _authBox.Text = network?.Authentication ?? "";
        _passwordBox.Password = initialPassword ?? "";
        _passwordBox.ToolTip = localizer.T("PasswordHint");
        AutomationProperties.SetHelpText(_passwordBox, localizer.T("PasswordHint"));
        FormField.SetRevealLabel(_passwordBox, localizer.T("Password"));
        _saveToWindowsBox.Content = localizer.T("SaveToWindows");
        _saveToWindowsBox.IsChecked = true;
        _saveToWindowsBox.SetResourceReference(MarginProperty, "SpacingOptionGap");
        Grid.SetRow(_saveToWindowsBox, 4);
        form.Children.Add(_saveToWindowsBox);

        _errorText.SetResourceReference(TextBlock.ForegroundProperty, "StatusDangerBrush");
        _errorText.SetResourceReference(MarginProperty, "SpacingValidationGap");
        _errorText.TextWrapping = TextWrapping.Wrap;
        _errorText.Text = localizer.T("SsidRequired");
        _errorText.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(_errorText, AutomationLiveSetting.Polite);
        ssidPanel.Children.Add(_errorText);
        var hint = new TextBlock { Text = localizer.T("PasswordHint"), TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        Grid.SetRow(hint, 5);
        form.Children.Add(hint);

        var okButton = new Wpf.Ui.Controls.Button
        {
            Content = localizer.T("Save"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, IsDefault = true
        };
        var cancelButton = new Wpf.Ui.Controls.Button
        {
            Content = localizer.T("Cancel"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true
        };
        foreach (var button in new[] { okButton, cancelButton })
        {
            button.SetResourceReference(StyleProperty, "DialogButtonStyle");
            Actions.Children.Add(button);
        }
        void UpdateAvailability()
        {
            var invalid = string.IsNullOrWhiteSpace(_ssidBox.Text);
            okButton.IsEnabled = !invalid;
            okButton.ToolTip = invalid ? localizer.T("SsidRequired") : null;
            AutomationProperties.SetHelpText(okButton, invalid ? localizer.T("SsidRequired") : "");
            if (invalid) return;
            _errorText.Visibility = Visibility.Collapsed;
            FormField.SetIsInvalid(_ssidBox, false);
            AutomationProperties.SetHelpText(_ssidBox, "");
        }
        void ShowValidation()
        {
            if (!string.IsNullOrWhiteSpace(_ssidBox.Text)) return;
            _errorText.Visibility = Visibility.Visible;
            FormField.SetIsInvalid(_ssidBox, true);
            AutomationProperties.SetHelpText(_ssidBox, _errorText.Text);
            UIElementAutomationPeer.CreatePeerForElement(_errorText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        _ssidBox.TextChanged += (_, _) => UpdateAvailability();
        _ssidBox.LostKeyboardFocus += (_, _) => ShowValidation();
        okButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_ssidBox.Text))
            {
                ShowValidation();
                _ssidBox.Focus();
                _ssidBox.BringIntoView();
                return;
            }
            DialogResult = true;
        };
        cancelButton.Click += (_, _) => DialogResult = false;
        UpdateAvailability();
        Loaded += (_, _) => _ssidBox.Focus();
    }

    public string Ssid => _ssidBox.Text;
    public string Authentication => _authBox.Text;
    public string? Password => string.IsNullOrEmpty(_passwordBox.Password) ? null : _passwordBox.Password;
    public bool SaveToWindows => _saveToWindowsBox.IsChecked == true;

    private static StackPanel AddLabeledControl(Grid root, int row, string label, Control control)
    {
        var panel = new StackPanel();
        panel.SetResourceReference(MarginProperty, "SpacingFieldGap");
        var caption = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold };
        caption.SetResourceReference(MarginProperty, "SpacingFieldCaptionGap");
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(caption);
        AutomationProperties.SetLabeledBy(control, caption);
        panel.Children.Add(control);
        Grid.SetRow(panel, row);
        root.Children.Add(panel);
        return panel;
    }
}
