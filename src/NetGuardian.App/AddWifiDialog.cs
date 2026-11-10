using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;

namespace NetGuardian.App;

public sealed class AddWifiDialog : FluentDialogWindow
{
    private readonly TextBox _ssidBox = new();
    private readonly TextBox _authBox = new();
    private readonly PasswordBox _passwordBox = new();
    private readonly CheckBox _saveToWindowsBox = new();
    private readonly TextBlock _errorText = new();

    public AddWifiDialog(Localizer localizer, WifiNetwork? network, string? initialPassword = null)
    {
        Title = localizer.T("AddWifi");
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(localizer.Language);
        var form = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        for (var row = 0; row < 6; row++)
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Body.Content = form;

        var title = new TextBlock
        {
            Text = localizer.T("AddWifi"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 20)
        };
        form.Children.Add(title);

        var ssidPanel = AddLabeledControl(form, 1, localizer.T("Ssid"), _ssidBox);
        AddLabeledControl(form, 2, localizer.T("Authentication"), _authBox);
        AddLabeledControl(form, 3, localizer.T("Password"), _passwordBox);

        _ssidBox.Text = network?.Ssid ?? "";
        _authBox.Text = network?.Authentication ?? "";
        _passwordBox.Password = initialPassword ?? "";
        _passwordBox.ToolTip = localizer.T("PasswordHint");
        AutomationProperties.SetHelpText(_passwordBox, localizer.T("PasswordHint"));
        StyleInput(_ssidBox);
        StyleInput(_authBox);
        StyleInput(_passwordBox);
        _saveToWindowsBox.Content = localizer.T("SaveToWindows");
        _saveToWindowsBox.IsChecked = true;
        _saveToWindowsBox.Margin = new Thickness(0, 4, 0, 8);
        Grid.SetRow(_saveToWindowsBox, 4);
        form.Children.Add(_saveToWindowsBox);

        _errorText.SetResourceReference(TextBlock.ForegroundProperty, "StatusDangerBrush");
        _errorText.TextWrapping = TextWrapping.Wrap;
        _errorText.Margin = new Thickness(0, 4, 0, 0);
        _errorText.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(_errorText, AutomationLiveSetting.Polite);
        ssidPanel.Children.Add(_errorText);
        _ssidBox.TextChanged += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_ssidBox.Text)) return;
            _errorText.Visibility = Visibility.Collapsed;
            AutomationProperties.SetHelpText(_ssidBox, "");
        };

        var hint = new TextBlock
        {
            Text = localizer.T("PasswordHint"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        var hintPanel = new StackPanel();
        hintPanel.Children.Add(hint);
        Grid.SetRow(hintPanel, 5);
        form.Children.Add(hintPanel);

        var buttons = Actions;
        var cancelButton = new Wpf.Ui.Controls.Button { Content = localizer.T("Cancel"), Width = 96, MinHeight = 38, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        StyleButton(cancelButton, false);
        cancelButton.Click += (_, _) => DialogResult = false;
        var okButton = new Wpf.Ui.Controls.Button { Content = localizer.T("Save"), Width = 96, MinHeight = 38, IsDefault = true };
        StyleButton(okButton, true);
        okButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_ssidBox.Text))
            {
                _errorText.Text = localizer.T("SsidRequired");
                _errorText.Visibility = Visibility.Visible;
                AutomationProperties.SetHelpText(_ssidBox, _errorText.Text);
                UIElementAutomationPeer.CreatePeerForElement(_errorText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                _ssidBox.Focus();
                _ssidBox.BringIntoView();
                return;
            }

            DialogResult = true;
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);
        Loaded += (_, _) => _ssidBox.Focus();
    }

    public string Ssid => _ssidBox.Text;
    public string Authentication => _authBox.Text;
    public string? Password => string.IsNullOrEmpty(_passwordBox.Password) ? null : _passwordBox.Password;
    public bool SaveToWindows => _saveToWindowsBox.IsChecked == true;

    private static StackPanel AddLabeledControl(Grid root, int row, string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var caption = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(caption);
        AutomationProperties.SetLabeledBy(control, caption);
        control.MinHeight = 36;
        panel.Children.Add(control);
        Grid.SetRow(panel, row);
        root.Children.Add(panel);
        return panel;
    }

    private static void StyleInput(Control control)
    {
        control.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        control.SetResourceReference(Control.BackgroundProperty, "ControlFillColorDefaultBrush");
        control.SetResourceReference(Control.BorderBrushProperty, "StrokeInputBrush");
        control.Padding = new Thickness(8, 4, 8, 4);
    }

    private static void StyleButton(Wpf.Ui.Controls.Button button, bool primary)
    {
        button.Appearance = primary ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
        button.SetResourceReference(StyleProperty, "DialogButtonStyle");
    }

}
