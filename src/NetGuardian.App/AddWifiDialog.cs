using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private readonly TextBlock _errorText = new();

    public AddWifiDialog(Localizer localizer, WifiNetwork? network, string? initialPassword = null)
    {
        Title = localizer.T("AddWifi");
        Width = 480;
        Height = 430;
        MinWidth = 480;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brush("#181D17");
        Foreground = Brush("#F4F1E8");

        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new Border
        {
            Background = Brush("#181D17"),
            BorderBrush = Brush("#344034"),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        titleBar.MouseLeftButtonDown += (_, _) => DragMove();
        var titleGrid = new Grid();
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        titleGrid.Children.Add(new TextBlock
        {
            Text = localizer.T("AddWifi"),
            Foreground = Brush("#DADDD3"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        });
        var closeButton = new Button
        {
            Content = "x",
            MinHeight = 38,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brush("#181D17"),
            Foreground = Brush("#F4F1E8")
        };
        closeButton.Click += (_, _) => DialogResult = false;
        Grid.SetColumn(closeButton, 1);
        titleGrid.Children.Add(closeButton);
        titleBar.Child = titleGrid;
        shell.Children.Add(titleBar);

        var root = new Grid { Margin = new Thickness(24, 22, 24, 22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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
            Margin = new Thickness(0, 0, 0, 20)
        };
        root.Children.Add(title);

        AddLabeledControl(root, 1, localizer.T("Ssid"), _ssidBox);
        AddLabeledControl(root, 2, localizer.T("Authentication"), _authBox);
        AddLabeledControl(root, 3, localizer.T("Password"), _passwordBox);

        _ssidBox.Text = network?.Ssid ?? "";
        _authBox.Text = network?.Authentication ?? "";
        _passwordBox.Password = initialPassword ?? "";
        _passwordBox.ToolTip = localizer.T("PasswordHint");
        StyleInput(_ssidBox);
        StyleInput(_authBox);
        StyleInput(_passwordBox);
        _saveToWindowsBox.Content = localizer.T("SaveToWindows");
        _saveToWindowsBox.Foreground = Brush("#F4F1E8");
        _saveToWindowsBox.IsChecked = true;
        _saveToWindowsBox.Margin = new Thickness(0, 4, 0, 8);
        Grid.SetRow(_saveToWindowsBox, 4);
        root.Children.Add(_saveToWindowsBox);

        _errorText.Foreground = Brush("#EF6B5B");
        _errorText.TextWrapping = TextWrapping.Wrap;
        _errorText.Margin = new Thickness(0, 0, 0, 8);
        _errorText.Visibility = Visibility.Collapsed;

        var hint = new TextBlock
        {
            Text = localizer.T("PasswordHint"),
            Foreground = Brush("#AEB8AA"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        };
        var hintPanel = new StackPanel();
        hintPanel.Children.Add(_errorText);
        hintPanel.Children.Add(hint);
        Grid.SetRow(hintPanel, 5);
        root.Children.Add(hintPanel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var cancelButton = new Button { Content = localizer.T("Cancel"), Width = 96, Height = 38, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        StyleButton(cancelButton, false);
        cancelButton.Click += (_, _) => DialogResult = false;
        var okButton = new Button { Content = localizer.T("Save"), Width = 96, Height = 38, IsDefault = true };
        StyleButton(okButton, true);
        okButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_ssidBox.Text))
            {
                _errorText.Text = localizer.T("SsidRequired");
                _errorText.Visibility = Visibility.Visible;
                _ssidBox.Focus();
                return;
            }

            DialogResult = true;
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);
        Grid.SetRow(buttons, 6);
        root.Children.Add(buttons);

        Grid.SetRow(root, 1);
        shell.Children.Add(root);
        Content = shell;
    }

    public string Ssid => _ssidBox.Text;
    public string Authentication => _authBox.Text;
    public string? Password => string.IsNullOrEmpty(_passwordBox.Password) ? null : _passwordBox.Password;
    public bool SaveToWindows => _saveToWindowsBox.IsChecked == true;

    private static void AddLabeledControl(Grid root, int row, string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#DADDD3"),
            Margin = new Thickness(0, 0, 0, 5)
        });
        control.MinHeight = 36;
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
