using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NetGuardian.App.Views;

public partial class TrayContextMenu : ContextMenu
{
    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText), typeof(string), typeof(TrayContextMenu), new PropertyMetadata(""));
    private readonly Dictionary<string, MenuItem> _commands;

    public TrayContextMenu()
    {
        InitializeComponent();
        _commands = Items.OfType<MenuItem>().ToDictionary(item => (string)item.Tag);
        foreach (var (key, item) in _commands)
        {
            item.MinHeight = 36;
            item.Padding = new Thickness(10, 8, 10, 8);
            ToolTipService.SetShowOnDisabled(item, true);
            item.Click += (_, _) =>
            {
                if (!item.IsEnabled) return;
                IsOpen = false;
                CommandRequested?.Invoke(key);
            };
        }
    }

    public string StatusText { get => (string)GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }
    public event Action<string>? CommandRequested;
    public MenuItem GetCommand(string key) => _commands[key];
}
