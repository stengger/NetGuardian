using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NetGuardian.Core.Localization;

namespace NetGuardian.App.Presentation;

// 密码仅保存在当前详情控件中，不进入配置、表格绑定或日志。
public sealed class WifiPasswordDetails : Grid
{
    public static readonly DependencyProperty PasswordReaderProperty = DependencyProperty.RegisterAttached(
        "PasswordReader", typeof(Func<string, CancellationToken, Task<string?>>), typeof(WifiPasswordDetails),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, ResetChanged));
    public static void SetPasswordReader(DependencyObject target, Func<string, CancellationToken, Task<string?>> reader) => target.SetValue(PasswordReaderProperty, reader);
    public static Func<string, CancellationToken, Task<string?>>? GetPasswordReader(DependencyObject target) =>
        (Func<string, CancellationToken, Task<string?>>?)target.GetValue(PasswordReaderProperty);

    public static readonly DependencyProperty UiLanguageProperty = DependencyProperty.RegisterAttached(
        "UiLanguage", typeof(string), typeof(WifiPasswordDetails),
        new FrameworkPropertyMetadata("zh-CN", FrameworkPropertyMetadataOptions.Inherits, ResetChanged));
    public static void SetUiLanguage(DependencyObject target, string language) => target.SetValue(UiLanguageProperty, language);
    public static string GetUiLanguage(DependencyObject target) => (string)target.GetValue(UiLanguageProperty);

    public static readonly DependencyProperty SsidProperty = DependencyProperty.Register(nameof(Ssid), typeof(string), typeof(WifiPasswordDetails), new PropertyMetadata("", ResetChanged));
    public string Ssid { get => (string)GetValue(SsidProperty); set => SetValue(SsidProperty, value); }
    public static readonly DependencyProperty CredentialSavedProperty = DependencyProperty.Register(nameof(CredentialSaved), typeof(bool), typeof(WifiPasswordDetails), new PropertyMetadata(false, ResetChanged));
    public bool CredentialSaved { get => (bool)GetValue(CredentialSavedProperty); set => SetValue(CredentialSavedProperty, value); }
    public static readonly DependencyProperty SecurityProperty = DependencyProperty.Register(nameof(Security), typeof(string), typeof(WifiPasswordDetails), new PropertyMetadata("", ResetChanged));
    public string Security { get => (string)GetValue(SecurityProperty); set => SetValue(SecurityProperty, value); }
    public static readonly DependencyProperty RowDetailsVisibilityProperty = DependencyProperty.Register(nameof(RowDetailsVisibility), typeof(Visibility), typeof(WifiPasswordDetails), new PropertyMetadata(Visibility.Visible, ResetChanged));
    public Visibility RowDetailsVisibility { get => (Visibility)GetValue(RowDetailsVisibilityProperty); set => SetValue(RowDetailsVisibilityProperty, value); }

    private readonly Localizer localizer = new();
    private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button toggle = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox value = new() { IsReadOnly = true, Visibility = Visibility.Collapsed };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private CancellationTokenSource? request;
    private DataGrid? table;

    public WifiPasswordDetails()
    {
        SetResourceReference(MarginProperty, "WifiDetailsMargin");
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        label.SetResourceReference(MarginProperty, "WifiDetailLabelMargin");
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        toggle.SetResourceReference(StyleProperty, "ToolbarButtonStyle");
        value.SetResourceReference(StyleProperty, "WifiDetailValueStyle");
        value.SetResourceReference(MarginProperty, "WifiDetailValueMargin");
        value.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        status.SetResourceReference(MarginProperty, "WifiDetailValueMargin");
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        SetColumn(toggle, 1);
        SetRow(value, 1); SetColumnSpan(value, 2);
        SetRow(status, 1); SetColumnSpan(status, 2);
        Children.Add(label); Children.Add(toggle); Children.Add(value); Children.Add(status);
        toggle.Click += Toggle_Click;
        Loaded += (_, _) =>
        {
            table = ResultTableInteraction.Ancestor<DataGrid>(this);
            if (table is not null) table.SelectionChanged += SelectionChanged;
            Reset();
        };
        Unloaded += (_, _) =>
        {
            if (table is not null) table.SelectionChanged -= SelectionChanged;
            table = null;
            Reset();
        };
        DataContextChanged += (_, _) => Reset();
        IsVisibleChanged += (_, _) => { if (!IsVisible) Reset(); };
        Reset();
    }

    private static void ResetChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is WifiPasswordDetails details) details.Reset();
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        // 行头可独立展开未选中的行；任何行选择变化都结束当前密码查看。
        if (args.AddedItems.Count > 0 || args.RemovedItems.Count > 0) Reset();
    }

    private void Reset()
    {
        request?.Cancel();
        request = null;
        value.Clear();
        value.Visibility = status.Visibility = Visibility.Collapsed;
        status.Text = "";
        localizer.SetLanguage(GetUiLanguage(this));
        label.Text = localizer.T("Password");
        AutomationProperties.SetName(value, label.Text);
        toggle.Content = localizer.T("ShowWifiPassword");
        Visibility = CredentialSaved ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Toggle_Click(object sender, RoutedEventArgs args)
    {
        if (request is not null || value.Visibility == Visibility.Visible)
        {
            Reset();
            return;
        }
        if (!CredentialSaved || !IsVisible || RowDetailsVisibility != Visibility.Visible) return;
        if (Security.Equals("Open", StringComparison.OrdinalIgnoreCase) || Security.Contains("开放", StringComparison.Ordinal))
        {
            ShowStatus(localizer.T("WifiPasswordOpenNetwork"));
            return;
        }
        var reader = GetPasswordReader(this);
        if (reader is null) return;
        var ssid = Ssid;
        using var current = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        request = current;
        toggle.Content = localizer.T("Cancel");
        ShowStatus(localizer.T("WifiPasswordReading"));
        try
        {
            // 即使读取实现未及时响应取消，也不再接收旧行或已收起详情的结果。
            var password = await reader(ssid, current.Token).WaitAsync(current.Token);
            if (request != current || !IsVisible || RowDetailsVisibility != Visibility.Visible || Ssid != ssid) return;
            if (string.IsNullOrEmpty(password)) ShowReadFailure(ssid);
            else
            {
                status.Text = "";
                status.Visibility = Visibility.Collapsed;
                value.Text = password;
                value.Visibility = Visibility.Visible;
            }
        }
        catch (Exception)
        {
            // 不把命令输出或异常原文带入日志与页面，避免意外暴露凭据。
            if (request == current) ShowReadFailure(ssid);
        }
        finally
        {
            if (request == current)
            {
                request = null;
                toggle.Content = localizer.T(value.Visibility == Visibility.Visible ? "HideWifiPassword" : "ShowWifiPassword");
            }
        }
    }

    private void ShowReadFailure(string ssid) => ShowStatus(string.Format(localizer.T("WifiPasswordImportFailed"), ssid));

    private void ShowStatus(string text)
    {
        status.Text = text;
        status.Visibility = Visibility.Visible;
    }
}
