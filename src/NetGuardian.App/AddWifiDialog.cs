using System;
using System.Windows;
using System.Windows.Controls;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;

namespace NetGuardian.App;

public sealed class AddWifiDialog : Window
{
    private readonly TextBox _ssidBox = new();
    private readonly TextBox _authBox = new();
    private readonly PasswordBox _passwordBox = new();
    private readonly CheckBox _saveToWindowsBox = new();

    public AddWifiDialog(Localizer localizer, WifiNetwork? network)
    {
        Title = localizer.T("AddWifi");
        Width = 440;
        Height = 360;
        MinWidth = 440;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Media.Brushes.White;

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = localizer.T("AddWifi"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 18)
        };
        root.Children.Add(title);

        AddLabeledControl(root, 1, localizer.T("Ssid"), _ssidBox);
        AddLabeledControl(root, 2, localizer.T("Authentication"), _authBox);
        AddLabeledControl(root, 3, localizer.T("Password"), _passwordBox);

        _ssidBox.Text = network?.Ssid ?? "";
        _authBox.Text = network?.Authentication ?? "";
        _passwordBox.ToolTip = localizer.T("PasswordHint");
        _saveToWindowsBox.Content = localizer.T("SaveToWindows");
        _saveToWindowsBox.IsChecked = true;
        _saveToWindowsBox.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(_saveToWindowsBox, 4);
        root.Children.Add(_saveToWindowsBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 86, Margin = new Thickness(4) };
        cancelButton.Click += (_, _) => DialogResult = false;
        var okButton = new Button { Content = "OK", MinWidth = 86, Margin = new Thickness(4) };
        okButton.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);
        Grid.SetRow(buttons, 5);
        root.Children.Add(buttons);

        Content = root;
    }

    public string Ssid => _ssidBox.Text;
    public string Authentication => _authBox.Text;
    public string? Password => string.IsNullOrEmpty(_passwordBox.Password) ? null : _passwordBox.Password;
    public bool SaveToWindows => _saveToWindowsBox.IsChecked == true;

    private static void AddLabeledControl(Grid root, int row, string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        });
        control.MinHeight = 34;
        panel.Children.Add(control);
        Grid.SetRow(panel, row);
        root.Children.Add(panel);
    }
}
