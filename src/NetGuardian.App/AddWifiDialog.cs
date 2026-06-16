using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        Background = Brush("#181D17");
        Foreground = Brush("#F4F1E8");

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
            Foreground = Brush("#F4F1E8"),
            Margin = new Thickness(0, 0, 0, 18)
        };
        root.Children.Add(title);

        AddLabeledControl(root, 1, localizer.T("Ssid"), _ssidBox);
        AddLabeledControl(root, 2, localizer.T("Authentication"), _authBox);
        AddLabeledControl(root, 3, localizer.T("Password"), _passwordBox);

        _ssidBox.Text = network?.Ssid ?? "";
        _authBox.Text = network?.Authentication ?? "";
        _passwordBox.ToolTip = localizer.T("PasswordHint");
        StyleInput(_ssidBox);
        StyleInput(_authBox);
        StyleInput(_passwordBox);
        _saveToWindowsBox.Content = localizer.T("SaveToWindows");
        _saveToWindowsBox.Foreground = Brush("#F4F1E8");
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
        StyleButton(cancelButton, false);
        cancelButton.Click += (_, _) => DialogResult = false;
        var okButton = new Button { Content = "OK", MinWidth = 86, Margin = new Thickness(4) };
        StyleButton(okButton, true);
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
            Foreground = Brush("#DADDD3"),
            Margin = new Thickness(0, 0, 0, 5)
        });
        control.MinHeight = 34;
        panel.Children.Add(control);
        Grid.SetRow(panel, row);
        root.Children.Add(panel);
    }

    private static void StyleInput(Control control)
    {
        control.Foreground = Brush("#F4F1E8");
        control.Background = Brush("#20251F");
        control.BorderBrush = Brush("#4A5146");
        control.Padding = new Thickness(8, 4, 8, 4);
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.Foreground = Brush(primary ? "#101510" : "#F4F1E8");
        button.Background = Brush(primary ? "#69D18B" : "#30362E");
        button.BorderBrush = Brush(primary ? "#69D18B" : "#42483F");
        button.Padding = new Thickness(14, 7, 14, 7);
        button.MinHeight = 36;
    }

    private static SolidColorBrush Brush(string value)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    }
}
