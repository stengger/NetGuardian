using System.Windows;
using System.Windows.Controls;

namespace NetGuardian.App;

public sealed class SsidDialog : Window
{
    private readonly TextBox _ssidBox = new();

    public SsidDialog(string title)
    {
        Title = title;
        Width = 360;
        Height = 170;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Media.Brushes.White;

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "SSID",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(_ssidBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 82, Margin = new Thickness(4) };
        cancelButton.Click += (_, _) => DialogResult = false;
        var okButton = new Button { Content = "OK", MinWidth = 82, Margin = new Thickness(4) };
        okButton.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);
        root.Children.Add(buttons);

        Content = root;
    }

    public string Ssid => _ssidBox.Text;
}

