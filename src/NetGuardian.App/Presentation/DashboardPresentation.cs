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
