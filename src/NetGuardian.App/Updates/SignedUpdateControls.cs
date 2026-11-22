using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NetGuardian.Core.Updates;
using NetGuardian.Core.Localization;
using NetGuardian.App.Presentation;

namespace NetGuardian.App.Updates;

internal sealed class SignedUpdateControls
{
    private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox picker = new() { MinWidth = 110 };
    internal SignedUpdateControls(TextBlock status, Action<UpdateSourceMode> changed)
    {
        var row = new WrapPanel();
        row.SetResourceReference(FrameworkElement.MarginProperty, "SpacingBlockStart");
        picker.SetResourceReference(FrameworkElement.MarginProperty, "SpacingLeadingGap");
        AutomationProperties.SetLabeledBy(picker, label);
        row.Children.Add(label);
        row.Children.Add(picker);
        var parent = (Panel)status.Parent;
        parent.Children.Insert(parent.Children.IndexOf(status), row);
        foreach (var mode in Enum.GetValues<UpdateSourceMode>()) picker.Items.Add(new ComboBoxItem { Tag = mode });
        picker.SelectedIndex = 0;
        picker.SelectionChanged += (_, _) => changed((UpdateSourceMode)((ComboBoxItem)picker.SelectedItem).Tag);
    }

    internal void Apply(bool english, bool enabled)
    {
        label.Text = english ? "Update source" : "更新来源";
        var labels = english ? new[] { "Automatic", "Primary", "Backup" } : new[] { "自动", "主源", "备用源" };
        for (int i = 0; i < labels.Length; i++) ((ComboBoxItem)picker.Items[i]).Content = labels[i];
        picker.IsEnabled = enabled;
    }
}

internal static class SignedUpdateText
{
    internal static void Apply(Window window, UpdateSession session, Version version, Localizer localizer, bool installed, UpdateRouteStatus? routes, UpdateRouteStatus? previous = null)
    {
        UpdatePresentation.Apply(window, session, version, localizer, installed);
        var status = (TextBlock)window.FindName("UpdateStatusText");
        bool english = localizer.Language == "en-US";
        if (routes is null)
        {
            status.Text += english ? "  GitHub only. The backup source is not enabled." : "  当前使用 GitHub，备用源尚未启用。";
            return;
        }
        if (session.Phase == UpdatePhase.Idle && previous is not null)
            status.Text += (english ? "  Last successful route" : "  上次成功线路") + Detail(previous, null, localizer.Language);
        if (session.Phase != UpdatePhase.Idle) status.Text += Detail(routes, session.Error, localizer.Language);
        if (session.Phase == UpdatePhase.Failed)
        {
            var retry = (Button)window.FindName(session.Release is null ? "UpdateCheckButton" : "UpdateDownloadButton");
            retry.Content = retry.ToolTip = english ? "Retry" : "重试";
        }
    }

    internal static string Detail(UpdateRouteStatus status, Exception? error, string language)
    {
        bool english = language == "en-US";
        string Route(UpdateRoute route) => route switch
        {
            UpdateRoute.PrimarySystem => english ? "Primary · system route" : "主源 · 系统线路",
            UpdateRoute.MirrorSystem => english ? "Backup · system route" : "备用源 · 系统线路",
            _ => english ? "Backup · direct" : "备用源 · 直连"
        };
        string Step(UpdateStep step) => step == UpdateStep.Manifest ? (english ? "Manifest" : "清单") : (english ? "Download" : "下载");
        var lines = new List<string>();
        if (status.Route is { } route) lines.Add(Route(route) + " · " + Step(status.Step));
        foreach (var failure in status.Failures)
            lines.Add($"{Route(failure.Route)} · {Step(failure.Step)} — {failure.Reason}");
        if (error is UpdateException issue)
        {
            lines.Add(issue.Message);
            if (status.Step == UpdateStep.Download || status.Failures.Any(failure => failure.Step == UpdateStep.Download))
                lines.Add((english ? "Saved " : "已保存 ") + $"{issue.SavedProgress?.Received ?? 0:N0} B");
        }
        if (error is UpdateException { Error: UpdateError.Expired } && status.LastChecked is { } last)
            lines.Add((english ? "Last successful check " : "上次成功检查 ") + last.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"));
        return lines.Count == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, lines);
    }
}
