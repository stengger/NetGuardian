using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Automation.Peers;
using System.Windows.Threading;
using NetGuardian.Core.Localization;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace NetGuardian.App.Presentation;

// 仅呈现应用内反馈，确认窗口和系统托盘通知保留原有通道。
public sealed class FeedbackPresenter : StackPanel
{
    private Localizer _localizer = new();
    private FeedbackQueue? _queue;
    private readonly Dictionary<FeedbackMessage, FeedbackCard> _cards = [];
    private readonly DispatcherTimer _timer = new();
    private long _lastTick;
    public FeedbackPresenter()
    {
        Visibility = Visibility.Collapsed;
        _timer.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            foreach (var (message, card) in _cards)
                message.Paused = card.IsMouseOver || card.IsKeyboardFocusWithin || card.IsDetailsExpanded;
            _queue?.Advance(Stopwatch.GetElapsedTime(_lastTick, now));
            _lastTick = now;
        };
        Loaded += (_, _) => StartTimer();
        Unloaded += (_, _) => _timer.Stop();
    }
    internal void Configure(Localizer localizer) => _localizer = localizer;

    public void ShowMessage(string title, string message, string severity, string? actionText = null,
        Action? action = null, string? identity = null)
    {
        if (_queue is null)
        {
            _queue = new FeedbackQueue((int)(double)FindResource("FeedbackVisibleLimit"),
                (TimeSpan)FindResource("FeedbackNormalTimeout"), (TimeSpan)FindResource("FeedbackWarningTimeout"));
            _queue.Changed += Synchronize;
        }
        _queue.Enqueue(title, message, severity, actionText, action, identity);
        StartTimer();
    }

    private void StartTimer()
    {
        if (!IsLoaded || _queue is null || _queue.Visible.Count == 0 || _timer.IsEnabled) return;
        _timer.Interval = (TimeSpan)FindResource("FeedbackTickInterval");
        _lastTick = Stopwatch.GetTimestamp();
        _timer.Start();
    }

    private void Synchronize()
    {
        var messages = _queue!.Visible;
        foreach (var (message, card) in _cards.ToArray())
        {
            if (messages.Contains(message) || card.IsExiting) continue;
            card.IsExiting = true;
            Animate(card, false, () =>
            {
                Children.Remove(card);
                _cards.Remove(message);
                Synchronize();
            });
        }
        foreach (var message in messages)
        {
            if (_cards.TryGetValue(message, out var current)) { current.SetCount(message.Count); continue; }
            if (Children.Count >= (double)FindResource("FeedbackVisibleLimit")) break;
            var card = new FeedbackCard(message.Title, message.Body, message.Severity, _localizer, message.ActionText, message.Action);
            card.Dismiss += () => _queue.Dismiss(message);
            _cards.Add(message, card);
            Children.Add(card);
            Visibility = Visibility.Visible;
            Animate(card, true, null);
            // LiveRegionChanged 使辅助技术获知新提示，显示时不抢夺键盘焦点。
            UIElementAutomationPeer.CreatePeerForElement(card)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        if (Children.Count == 0) Visibility = Visibility.Collapsed;
        if (messages.Count == 0) _timer.Stop();
    }

    private void Animate(FeedbackCard card, bool entering, Action? completed)
    {
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation)
        {
            card.Opacity = 1;
            completed?.Invoke();
            return;
        }
        var duration = (Duration)FindResource(entering ? "FeedbackEnterDuration" : "FeedbackExitDuration");
        var animation = new DoubleAnimation(entering ? 0 : card.Opacity, entering ? 1 : 0, duration);
        if (completed is not null) animation.Completed += (_, _) => completed();
        card.BeginAnimation(OpacityProperty, animation);
        if (entering)
        {
            var offset = new TranslateTransform();
            card.RenderTransform = offset;
            offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation((double)FindResource("FeedbackSlideOffset"), 0, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
}

internal sealed class FeedbackCard : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    internal event Action? Dismiss;
    private readonly TextBlock _heading;
    private readonly string _title;
    private Expander? _details;
    internal bool IsDetailsExpanded => _details?.IsExpanded == true;
    internal bool IsExiting { get; set; }
    internal void SetCount(int count) => _heading.Text = count > 1 ? $"{_title} ×{count}" : _title;

    internal FeedbackCard(string title, string message, string severity, Localizer localizer,
        string? actionText, Action? action)
    {
        Name = "FeedbackCard";
        _title = title;
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
            "Danger" or "Error" => ("StatusDangerBrush", SymbolRegular.DismissCircle24),
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
        _heading = heading;
        heading.SetResourceReference(TextBlock.FontSizeProperty, "FontBody");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        body.Children.Add(heading);
        var text = new TextBlock { Name = "FeedbackBody", Text = message, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.FontSizeProperty, "FontFeedbackBody");
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        text.SetResourceReference(MarginProperty, "SpacingValidationGap");
        body.Children.Add(text);
        if (message.Contains('\n'))
        {
            // 多行原文放入详情；摘要保留完整首段，单行长文本仍自然换行。
            text.Text = message.Split('\n', 2)[0];
            var original = new TextBox { Text = message, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            original.SetResourceReference(StyleProperty, "DialogTextBoxStyle");
            original.SetResourceReference(MaxHeightProperty, "FeedbackDetailsMaxHeight");
            AutomationProperties.SetName(original, localizer.T("FeedbackDetails"));
            _details = new Expander { Header = localizer.T("FeedbackDetails"), Content = original };
            _details.SetResourceReference(StyleProperty, "ContentExpanderStyle");
            body.Children.Add(_details);
        }
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
