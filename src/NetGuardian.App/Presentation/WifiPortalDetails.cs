using System.Windows;
using System.Windows.Controls;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Models;

namespace NetGuardian.App.Presentation;

/// <summary>在 Wi-Fi 恢复列表行详情中展示脱敏的网页自动认证状态。</summary>
public sealed class WifiPortalDetails : Grid
{
    public static readonly DependencyProperty UiLanguageProperty = DependencyProperty.RegisterAttached(
        "UiLanguage", typeof(string), typeof(WifiPortalDetails),
        new FrameworkPropertyMetadata("zh-CN", FrameworkPropertyMetadataOptions.Inherits, OnChanged));

    public static void SetUiLanguage(DependencyObject target, string language) => target.SetValue(UiLanguageProperty, language);
    public static string GetUiLanguage(DependencyObject target) => (string)target.GetValue(UiLanguageProperty);

    public static readonly DependencyProperty PortalAuthProperty = DependencyProperty.Register(
        nameof(PortalAuth), typeof(PortalAuthConfig), typeof(WifiPortalDetails), new PropertyMetadata(null, OnChanged));

    public PortalAuthConfig? PortalAuth
    {
        get => (PortalAuthConfig?)GetValue(PortalAuthProperty);
        set => SetValue(PortalAuthProperty, value);
    }

    private readonly Localizer _localizer = new();
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    public WifiPortalDetails()
    {
        SetResourceReference(MarginProperty, "WifiDetailsMargin");
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _label.SetResourceReference(MarginProperty, "WifiDetailLabelMargin");
        _label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        _status.SetResourceReference(MarginProperty, "WifiDetailValueMargin");
        SetColumn(_status, 1);
        Children.Add(_label);
        Children.Add(_status);
        DataContextChanged += (_, _) => Refresh();
        Refresh();
    }

    private static void OnChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is WifiPortalDetails details) details.Refresh();
    }

    private void Refresh()
    {
        var config = PortalAuth;
        if (config is null || (!config.Enabled && !config.Ready && !config.Paused && string.IsNullOrWhiteSpace(config.AllowedOrigin)))
        {
            Visibility = Visibility.Collapsed;
            _status.Text = "";
            return;
        }

        _localizer.SetLanguage(GetUiLanguage(this));
        _label.Text = _localizer.T("PortalStatusLabel");

        if (config.Paused)
        {
            _status.Text = string.Format(_localizer.T("PortalStatusPaused"), config.PauseReason ?? "—");
            _status.SetResourceReference(TextBlock.ForegroundProperty, "StatusWarningBrush");
        }
        else if (config.CanAutoAuthenticate)
        {
            _status.Text = string.Format(_localizer.T("PortalStatusReady"), config.AllowedOrigin);
            _status.SetResourceReference(TextBlock.ForegroundProperty, "StatusHealthyBrush");
        }
        else
        {
            _status.Text = string.Format(_localizer.T("PortalStatusPending"), string.IsNullOrWhiteSpace(config.AllowedOrigin) ? "—" : config.AllowedOrigin);
            _status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        }

        Visibility = Visibility.Visible;
    }
}
