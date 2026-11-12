using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NetGuardian.Core.Localization;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace NetGuardian.App.Presentation;

// 仅呈现应用内反馈，确认窗口和系统托盘通知保留原有通道。
public sealed class FeedbackPresenter : StackPanel
{
    private Localizer _localizer = new();
    public FeedbackPresenter() => Visibility = Visibility.Collapsed;
    internal void Configure(Localizer localizer) => _localizer = localizer;

    public void ShowMessage(string title, string message, string severity, string? actionText = null,
        Action? action = null, string? identity = null)
    {
        var card = new FeedbackCard(title, message, severity, _localizer, actionText, action);
        Children.Add(card);
        Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(severity == "Success" ? 2 : 5) };
        void Remove()
        {
            timer.Stop();
            Children.Remove(card);
            if (Children.Count == 0) Visibility = Visibility.Collapsed;
        }
        timer.Tick += (_, _) => Remove();
        card.Dismiss += Remove;
        card.Unloaded += (_, _) => timer.Stop();
        timer.Start();
    }
}

internal sealed class FeedbackCard : Border
{
    internal event Action? Dismiss;

    internal FeedbackCard(string title, string message, string severity, Localizer localizer,
        string? actionText, Action? action)
    {
        Name = "FeedbackCard";
        SetResourceReference(BackgroundProperty, "SurfaceCardBrush");
        SetResourceReference(BorderBrushProperty, "CardStrokeColorDefaultBrush");
        SetResourceReference(BorderThicknessProperty, "StrokeControlThickness");
        SetResourceReference(CornerRadiusProperty, "CornerCard");
        SetResourceReference(PaddingProperty, "SpacingCardPadding");
        SetResourceReference(MarginProperty, "SpacingFeedbackGap");
        SetResourceReference(EffectProperty, "FeedbackShadow");
        var (brush, symbol) = severity switch
        {
            "Success" => ("StatusHealthyBrush", SymbolRegular.CheckmarkCircle24),
            "Warning" => ("StatusWarningBrush", SymbolRegular.Warning24),
            "Danger" => ("StatusDangerBrush", SymbolRegular.DismissCircle24),
            _ => ("StatusInfoBrush", SymbolRegular.Info24)
        };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon(symbol) { VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(Control.ForegroundProperty, brush);
        icon.SetResourceReference(Control.FontSizeProperty, "FeedbackIconSize");
        icon.SetResourceReference(MarginProperty, "SpacingInlineGap");
        layout.Children.Add(icon);
        var body = new StackPanel();
        Grid.SetColumn(body, 1);
        layout.Children.Add(body);
        var heading = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "FontBody");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        body.Children.Add(heading);
        var text = new TextBlock { Name = "FeedbackBody", Text = message, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.FontSizeProperty, "FontFeedbackBody");
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        text.SetResourceReference(MarginProperty, "SpacingValidationGap");
        body.Children.Add(text);
        if (action is not null && actionText is not null)
        {
            var button = new Button { Content = actionText, HorizontalAlignment = HorizontalAlignment.Left };
            button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            button.SetResourceReference(MarginProperty, "SpacingFeedbackAction");
            button.Click += (_, _) => { action(); button.IsEnabled = false; Dismiss?.Invoke(); };
            body.Children.Add(button);
        }
        var close = new Button { Name = "FeedbackClose", Content = new SymbolIcon(SymbolRegular.Dismiss24),
            VerticalAlignment = VerticalAlignment.Top, ToolTip = localizer.T("Close") };
        close.SetResourceReference(StyleProperty, "IconButtonStyle");
        AutomationProperties.SetName(close, localizer.T("Close"));
        close.Click += (_, _) => Dismiss?.Invoke();
        Grid.SetColumn(close, 2);
        layout.Children.Add(close);
        Child = layout;
        AutomationProperties.SetName(this, $"{title}：{message}");
        AutomationProperties.SetLiveSetting(this, severity == "Danger" ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
    }
}
