using System;
using System.Globalization;
using System.Windows.Controls;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Localization;

namespace NetGuardian.App.Presentation;

internal static class DashboardPresentation
{
    internal static string SuccessRate(int successes, int attempts) => attempts <= 0 ? "—"
        : (100d * successes / attempts).ToString("0", CultureInfo.CurrentCulture) + "%";

    internal static string LatencyBrushKey(double? latency, double good, double warning) =>
        latency is not { } value || !double.IsFinite(value) || value < 0 ? "TextFillColorSecondaryBrush"
        : value <= good ? "StatusHealthyBrush" : value <= warning ? "StatusWarningBrush" : "StatusDangerBrush";

    internal static void RenderMeasurement(TextBlock value, TextBlock samples, DashboardMeasurement? sample,
        string kind, Localizer localizer, DateTimeOffset now, int intervalSeconds, bool skipped)
    {
        RenderTiming(value, samples, sample?.Timing, kind, localizer);
        if (sample is null)
        {
            value.ToolTip = samples.ToolTip = samples.Text = localizer.T("MetricWaiting");
            return;
        }
        var details = samples.Text;
        var at = sample.Report.CheckedAt.ToLocalTime();
        var caption = at.ToString("HH:mm:ss");
        if (sample.Timing.Succeeded == 0)
        {
            value.Text = localizer.T("MetricFailed");
            if (sample.LastValidMs is { } last)
                caption += " · " + string.Format(localizer.T("MetricPrevious"), last, sample.LastValidAt?.ToLocalTime());
        }
        else if (sample.Timing.Succeeded < sample.Timing.Attempted)
            caption += $" · {sample.Timing.Succeeded}/{sample.Timing.Attempted}";
        if (sample.Expired(now, intervalSeconds))
        {
            caption += " · " + localizer.T("MetricExpired");
            AppThemePolicy.BindBrush(value, TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        }
        else if (skipped) caption += " · " + localizer.T("MetricSkipped");
        samples.Text = caption;
        value.ToolTip = samples.ToolTip = $"{at:g} · {details} · {caption}";
    }

    internal static void RenderTiming(TextBlock value, TextBlock samples, ProbeTimingSummary? timing,
        string kind, Localizer localizer)
    {
        var good = (double)value.FindResource("Latency" + kind + "Good");
        var warning = (double)value.FindResource("Latency" + kind + "Warning");
        var key = LatencyBrushKey(timing?.AverageMs, good, warning);
        var hasAttempts = timing is { Attempted: > 0 };
        if (hasAttempts && timing!.Succeeded == 0) key = "StatusDangerBrush";
        else if (hasAttempts && timing!.Succeeded < timing.Attempted && key == "StatusHealthyBrush") key = "StatusWarningBrush";
        var glyph = key switch { "StatusHealthyBrush" => "✓ ", "StatusWarningBrush" => "! ", "StatusDangerBrush" => "× ", _ => "" };
        value.Text = glyph + (timing?.AverageMs is { } average && double.IsFinite(average) && average >= 0 ? $"{average:F0} ms" : "—");
        AppThemePolicy.BindBrush(value, TextBlock.ForegroundProperty, key);
        samples.Text = !hasAttempts ? localizer.T("QualityNotMeasured")
            : string.Format(localizer.T("QualityTimingSamples"), timing!.Succeeded, timing.Attempted, timing.Measured);
        value.ToolTip = samples.Text;
    }
}
