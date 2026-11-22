using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace NetGuardian.App.Presentation;

/// <summary>按可用宽度和字号换行，让三个读数等宽且保留说明。</summary>
public sealed class DashboardMetricGrid : UniformGrid
{
    protected override Size MeasureOverride(Size constraint)
    {
        var minimum = 12 * (double)GetValue(Control.FontSizeProperty);
        var columns = double.IsFinite(constraint.Width) ? Math.Clamp((int)(constraint.Width / minimum), 1, 3) : 3;
        if (Columns != columns) Columns = columns;
        return base.MeasureOverride(constraint);
    }
}
